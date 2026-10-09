using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

/// <summary>
/// Carries the game's connections to the community server while the game runs. It listens on 127.0.0.1
/// on the title, billing, aime and ms ports; each accepted connection gets its own TLS connection to the
/// server's secure port, opened with "FGOAC-TUNNEL 1 &lt;service&gt; &lt;access code&gt;" and LF, and
/// carries raw bytes both ways once the server answers "OK". There is no unencrypted TCP path.
/// After the first "OK" a few such connections are kept past "OK" per service, so the game's next
/// connection starts on one without the connect, handshake and line round trips.
/// The ping and dist UDP ports are relayed as they are to the server's UDP ports, one upstream socket per
/// game socket; they carry no secret.
/// </summary>
public sealed partial class CommunityTunnel : IDisposable
{
	private sealed class Mapping
	{
		/// <summary>Replaced under the mapping's lock when it fails beyond one datagram.</summary>
		public volatile UdpClient Upstream;

		public long LastActive = Environment.TickCount64;

		public int Out;

		public int Back;

		/// <summary>When the first datagram out since the last one back was sent; 0 while every one is answered.</summary>
		public long Unanswered;

		/// <summary>True once the current run of unanswered datagrams has been reported.</summary>
		public volatile bool Silent;

		/// <summary>When the upstream socket was last replaced; a new one is made at most once a second.</summary>
		public long Replaced;

		public volatile bool Closed;

		public Mapping(UdpClient upstream)
		{
			Upstream = upstream;
		}
	}

	/// <summary>A connection past the server's OK that waits for the game's next connection of its service.</summary>
	private sealed class Warm
	{
		public readonly TcpClient Server;

		public readonly SslStream Tls;

		public readonly long Since = Environment.TickCount64;

		public Warm(TcpClient server, SslStream tls)
		{
			Server = server;
			Tls = tls;
		}

		/// <summary>Younger than maxAge and nothing readable: the server sends nothing before the game's first bytes, so a readable socket is closed or closing.</summary>
		public bool Live(long now, TimeSpan maxAge)
		{
			try
			{
				return now - Since < (long)maxAge.TotalMilliseconds && !Server.Client.Poll(0, SelectMode.SelectRead);
			}
			catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
			{
				return false;
			}
		}

		public void Close()
		{
			Tls.Dispose();
			Server.Dispose();
		}
	}

	/// <summary>Warm connections kept per service: back-to-back title requests take up to three within one dial, the other services one.</summary>
	private static readonly Dictionary<string, int> WarmSizes = new Dictionary<string, int>
	{
		["title"] = 3,
		["billing"] = 1,
		["aime"] = 1,
		["ms"] = 1
	};

	/// <summary>SIO_UDP_CONNRESET: an ICMP port unreachable must not fail the next receive on the socket.</summary>
	private const int UdpConnectionReset = unchecked((int)0x9800000C);

	/// <summary>How long datagrams out may go unanswered while the game keeps sending before the mapping reports it.</summary>
	private static readonly TimeSpan NoAnswer = TimeSpan.FromSeconds(5.0);

	private readonly CommunityServer.Settings settings;

	private readonly IPAddress address;

	private readonly string accessCode;

	private readonly Action<string, bool> report;

	private readonly TimeSpan silence;

	private readonly TimeSpan warmFor;

	private readonly List<TcpListener> listeners = new List<TcpListener>();

	private readonly List<UdpClient> relays = new List<UdpClient>();

	private readonly Dictionary<(string Service, IPEndPoint Game), Mapping> mappings = new Dictionary<(string, IPEndPoint), Mapping>();

	private readonly CancellationTokenSource stopping = new CancellationTokenSource();

	private readonly HashSet<string> reported = new HashSet<string>();

	/// <summary>1 once the server has refused the access code for good; later game connections are closed without dialling it.</summary>
	private int refused;

	/// <summary>Per service the warm connections, oldest first, and the dials under way to refill them; locked by warm.</summary>
	private readonly Dictionary<string, Queue<Warm>> warm = WarmSizes.Keys.ToDictionary((string service) => service, (string _) => new Queue<Warm>());

	private readonly Dictionary<string, int> dialling = WarmSizes.Keys.ToDictionary((string service) => service, (string _) => 0);

	/// <summary>Per service, true while its last dial ended in OK, and for every service from the run's first OK; warm connections are dialled only then, so a failing service gets only the game's own dials.</summary>
	private readonly Dictionary<string, bool> warming = WarmSizes.Keys.ToDictionary((string service) => service, (string _) => false);

	private bool warmed;

	private CommunityTunnel(CommunityServer.Settings settings, IPAddress address, string accessCode, Action<string, bool> report, TimeSpan silence, TimeSpan warmFor)
	{
		this.settings = settings;
		this.address = address;
		this.accessCode = accessCode;
		this.report = report;
		this.silence = silence;
		this.warmFor = warmFor;
	}

	/// <summary>Opens the four TCP listeners and the two UDP relays; report gets each line once, flagged true for a problem, and never the access code.</summary>
	public static CommunityTunnel Start(CommunityServer.Settings settings, string accessCode, Action<string, bool> report)
	{
		return Start(settings, accessCode, report, TimeSpan.FromSeconds(120.0));
	}

	/// <summary>As Start, with a UDP mapping dropped after the given silence in both directions.</summary>
	public static CommunityTunnel Start(CommunityServer.Settings settings, string accessCode, Action<string, bool> report, TimeSpan silence)
	{
		return Start(settings, accessCode, report, silence, TimeSpan.FromSeconds(240.0));
	}

	/// <summary>As Start, with a warm connection replaced once warmFor old; the server closes one that waits 300 s.</summary>
	public static CommunityTunnel Start(CommunityServer.Settings settings, string accessCode, Action<string, bool> report, TimeSpan silence, TimeSpan warmFor)
	{
		(string Service, int Port)[] services = new (string, int)[4]
		{
			("title", settings.Http),
			("billing", settings.Billing),
			("aime", settings.Aime),
			("ms", settings.Ms)
		};
		(string Service, int Port, int ServerPort)[] datagrams = new (string, int, int)[2]
		{
			("ping", settings.Ping, settings.UdpPingPort),
			("dist", settings.Dist, settings.UdpDistPort)
		};
		IPAddress address = Resolve(settings.Host);
		IPGlobalProperties network = IPGlobalProperties.GetIPGlobalProperties();
		// A loopback bind succeeds beside another program's wildcard socket and takes its traffic, so a used port is refused first.
		HashSet<int> usedTcp = network.GetActiveTcpListeners().Where(Overlaps).Select((IPEndPoint endPoint) => endPoint.Port).ToHashSet();
		HashSet<int> usedUdp = network.GetActiveUdpListeners().Where(Overlaps).Select((IPEndPoint endPoint) => endPoint.Port).ToHashSet();
		CommunityTunnel tunnel = new CommunityTunnel(settings, address, accessCode, report, silence, warmFor);
		try
		{
			foreach (var (_, port) in services)
			{
				if (usedTcp.Contains(port))
				{
					throw new IOException(PortUsed(port));
				}
				TcpListener listener = new TcpListener(IPAddress.Loopback, port)
				{
					ExclusiveAddressUse = true
				};
				try
				{
					listener.Start();
				}
				catch (SocketException ex)
				{
					throw new IOException(PortUsed(port), ex);
				}
				tunnel.listeners.Add(listener);
			}
			foreach (var (_, port, _) in datagrams)
			{
				if (usedUdp.Contains(port))
				{
					throw new IOException(PortUsed(port));
				}
				UdpClient relay = new UdpClient(AddressFamily.InterNetwork)
				{
					ExclusiveAddressUse = true
				};
				tunnel.relays.Add(relay);
				relay.Client.IOControl(UdpConnectionReset, new byte[4], null);
				try
				{
					relay.Client.Bind(new IPEndPoint(IPAddress.Loopback, port));
				}
				catch (SocketException ex)
				{
					throw new IOException(PortUsed(port), ex);
				}
			}
		}
		catch
		{
			tunnel.Dispose();
			throw;
		}
		for (int i = 0; i < services.Length; i++)
		{
			_ = tunnel.AcceptAsync(tunnel.listeners[i], services[i].Service);
		}
		for (int i = 0; i < datagrams.Length; i++)
		{
			_ = tunnel.RelayAsync(tunnel.relays[i], datagrams[i].Service, datagrams[i].ServerPort);
		}
		_ = tunnel.ExpireAsync();
		_ = tunnel.RefreshAsync();
		return tunnel;
	}

	/// <summary>The server's first IPv4 address, looked up once within 15 s so every TCP and UDP leg goes to the same address.</summary>
	private static IPAddress Resolve(string host)
	{
		IPAddress[] addresses;
		try
		{
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15.0));
			addresses = Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, timeout.Token).WaitAsync(timeout.Token).GetAwaiter().GetResult();
		}
		catch (Exception ex) when (ex is SocketException || ex is OperationCanceledException)
		{
			throw new IOException($"Could not find {host}. Check the address on the Online page and this PC's internet connection, then press Play Online.", ex);
		}
		if (addresses.Length == 0)
		{
			throw new IOException($"Could not find {host}. Check the address on the Online page and this PC's internet connection, then press Play Online.");
		}
		return addresses[0];
	}

	private static bool Overlaps(IPEndPoint endPoint)
	{
		return IPAddress.IsLoopback(endPoint.Address) || endPoint.Address.Equals(IPAddress.Any) || endPoint.Address.Equals(IPAddress.IPv6Any);
	}

	private static string PortUsed(int port)
	{
		return $"Port {port} on this PC is used by another program. Close that program, then press Play Online.";
	}

	public CommunityServer.Settings Settings => settings;

	/// <summary>True from Start until Dispose.</summary>
	public bool Running => !stopping.IsCancellationRequested;

	public void Dispose()
	{
		stopping.Cancel();
		foreach (TcpListener listener in listeners)
		{
			listener.Stop();
		}
		foreach (UdpClient relay in relays)
		{
			relay.Dispose();
		}
		lock (mappings)
		{
			foreach (Mapping mapping in mappings.Values)
			{
				Close(mapping);
			}
			mappings.Clear();
		}
		lock (warm)
		{
			foreach (Queue<Warm> pool in warm.Values)
			{
				foreach (Warm connection in pool)
				{
					connection.Close();
				}
				pool.Clear();
			}
		}
	}

	/// <summary>Sends each datagram from the game on through the upstream socket of its source.</summary>
	private async Task RelayAsync(UdpClient relay, string service, int serverPort)
	{
		while (true)
		{
			UdpReceiveResult datagram;
			try
			{
				datagram = await relay.ReceiveAsync(stopping.Token);
			}
			catch (Exception ex)
			{
				if (stopping.IsCancellationRequested)
				{
					return;
				}
				if (!OneDatagram(ex))
				{
					Report(service, $"The launcher could not read the game's UDP datagrams: {ex.Message.TrimEnd('.')}.", problem: true);
					await Task.Delay(100);
				}
				continue;
			}
			Mapping? mapping = MappingFor(relay, service, serverPort, datagram.RemoteEndPoint);
			if (mapping == null)
			{
				continue;
			}
			UdpClient upstream = mapping.Upstream;
			try
			{
				await upstream.SendAsync(datagram.Buffer, stopping.Token);
				Interlocked.Increment(ref mapping.Out);
				long now = Environment.TickCount64;
				Interlocked.Exchange(ref mapping.LastActive, now);
				long since = Interlocked.CompareExchange(ref mapping.Unanswered, now, 0);
				if (since != 0 && now - since >= (long)NoAnswer.TotalMilliseconds && !mapping.Silent)
				{
					mapping.Silent = true;
					Report(service, $"UDP from 127.0.0.1:{datagram.RemoteEndPoint.Port} has had no answer from {settings.Host}:{serverPort} for {(now - since) / 1000} s while the game kept sending; {mapping.Out} datagrams out and {mapping.Back} back so far.", problem: false);
				}
			}
			catch (Exception ex)
			{
				if (!stopping.IsCancellationRequested)
				{
					Replace(service, serverPort, mapping, upstream, ex);
				}
			}
		}
	}

	/// <summary>True for an error about one datagram (ICMP port unreachable or TTL expired, or a datagram too large): the socket still works.</summary>
	private static bool OneDatagram(Exception ex)
	{
		return ex is SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.NetworkReset or SocketError.MessageSize };
	}

	/// <summary>A new socket connected to the server's UDP port, with ICMP port unreachable reports off.</summary>
	private UdpClient Connect(int serverPort)
	{
		UdpClient upstream = new UdpClient(AddressFamily.InterNetwork);
		try
		{
			upstream.Client.IOControl(UdpConnectionReset, new byte[4], null);
			upstream.Connect(address, serverPort);
			return upstream;
		}
		catch
		{
			upstream.Dispose();
			throw;
		}
	}

	/// <summary>After a failure beyond one datagram, puts a new upstream socket in the mapping, at most once a second, and the game's endpoint keeps its mapping; false while the failed socket is still the mapping's.</summary>
	private bool Replace(string service, int serverPort, Mapping mapping, UdpClient failed, Exception ex)
	{
		if (OneDatagram(ex))
		{
			return true;
		}
		lock (mapping)
		{
			long now = Environment.TickCount64;
			if (mapping.Closed || mapping.Upstream != failed)
			{
				return true;
			}
			if (now - mapping.Replaced < 1000)
			{
				return false;
			}
			mapping.Replaced = now;
			Report(service, $"UDP to {settings.Host}:{serverPort} failed: {ex.Message.TrimEnd('.')}. The launcher opens a new socket for it.", problem: true);
			try
			{
				mapping.Upstream = Connect(serverPort);
			}
			catch (SocketException)
			{
				return false;
			}
		}
		failed.Dispose();
		return true;
	}

	/// <summary>Ends the mapping and its upstream socket under the mapping's lock, so no replacement outlives it.</summary>
	private static void Close(Mapping mapping)
	{
		lock (mapping)
		{
			mapping.Closed = true;
			mapping.Upstream.Dispose();
		}
	}

	/// <summary>The mapping of one game socket, made on its first datagram; null once a failure to make it has been reported.</summary>
	private Mapping? MappingFor(UdpClient relay, string service, int serverPort, IPEndPoint game)
	{
		lock (mappings)
		{
			if (mappings.TryGetValue((service, game), out Mapping? known))
			{
				return known;
			}
		}
		UdpClient upstream;
		try
		{
			upstream = Connect(serverPort);
		}
		catch (SocketException ex)
		{
			Report(service, $"Could not relay UDP to the community server {settings.Host} on port {serverPort}: {ex.Message.TrimEnd('.')}. Check the address, and that the server is running.", problem: true);
			return null;
		}
		Mapping mapping = new Mapping(upstream);
		lock (mappings)
		{
			if (stopping.IsCancellationRequested)
			{
				upstream.Dispose();
				return null;
			}
			mappings[(service, game)] = mapping;
		}
		Report(service, $"relaying UDP to {settings.Host}:{serverPort}.", problem: false);
		_ = ReturnAsync(relay, service, serverPort, mapping, game);
		return mapping;
	}

	/// <summary>Sends the server's datagrams on the mapping's upstream socket back to the game socket it belongs to.</summary>
	private async Task ReturnAsync(UdpClient relay, string service, int serverPort, Mapping mapping, IPEndPoint game)
	{
		while (!mapping.Closed)
		{
			UdpClient upstream = mapping.Upstream;
			UdpReceiveResult reply;
			try
			{
				reply = await upstream.ReceiveAsync(stopping.Token);
			}
			catch (Exception ex)
			{
				if (stopping.IsCancellationRequested)
				{
					return;
				}
				if (!Replace(service, serverPort, mapping, upstream, ex))
				{
					await Task.Delay(100);
				}
				continue;
			}
			Interlocked.Exchange(ref mapping.Unanswered, 0);
			mapping.Silent = false;
			try
			{
				await relay.SendAsync(reply.Buffer, game, stopping.Token);
				Interlocked.Increment(ref mapping.Back);
				Interlocked.Exchange(ref mapping.LastActive, Environment.TickCount64);
			}
			catch (Exception ex)
			{
				if (stopping.IsCancellationRequested)
				{
					return;
				}
				if (!OneDatagram(ex))
				{
					Report(service, $"The launcher could not pass the server's UDP datagrams to the game: {ex.Message.TrimEnd('.')}.", problem: true);
				}
			}
		}
	}

	/// <summary>Drops a mapping silent in both directions for longer than the silence, checked every second or half the silence.</summary>
	private async Task ExpireAsync()
	{
		TimeSpan tick = TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(1.0).Ticks, silence.Ticks / 2));
		while (true)
		{
			try
			{
				await Task.Delay(tick, stopping.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			long now = Environment.TickCount64;
			List<((string Service, IPEndPoint Game) Key, Mapping Mapping)> quiet = new List<((string, IPEndPoint), Mapping)>();
			lock (mappings)
			{
				foreach (KeyValuePair<(string Service, IPEndPoint Game), Mapping> pair in mappings)
				{
					if (now - Interlocked.Read(ref pair.Value.LastActive) > (long)silence.TotalMilliseconds)
					{
						quiet.Add((pair.Key, pair.Value));
					}
				}
				foreach (var (key, _) in quiet)
				{
					mappings.Remove(key);
				}
			}
			foreach (var (key, mapping) in quiet)
			{
				Close(mapping);
				Report(key.Service, $"UDP from 127.0.0.1:{key.Game.Port} relayed {mapping.Out} datagrams out and {mapping.Back} back, then went quiet.", problem: false);
			}
		}
	}

	private async Task AcceptAsync(TcpListener listener, string service)
	{
		while (true)
		{
			TcpClient game;
			try
			{
				game = await listener.AcceptTcpClientAsync(stopping.Token);
			}
			catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException || ex is SocketException)
			{
				if (stopping.IsCancellationRequested)
				{
					return;
				}
				Report(service, "The launcher could not take a connection from the game: " + ex.Message, problem: true);
				continue;
			}
			if (Volatile.Read(ref refused) != 0)
			{
				game.Dispose();
				continue;
			}
			_ = ServeAsync(game, service);
		}
	}

	private async Task ServeAsync(TcpClient game, string service)
	{
		Warm? ready = Take(service);
		using TcpClient server = ready?.Server ?? new TcpClient(AddressFamily.InterNetwork);
		// Closing both sockets ends every pending read and write of this connection.
		using CancellationTokenRegistration closing = stopping.Token.Register(delegate
		{
			game.Dispose();
			server.Dispose();
		});
		using (game)
		{
			try
			{
				game.NoDelay = true;
				server.NoDelay = true;
				using SslStream? tls = ready?.Tls ?? (await OpenAsync(server, service)).Tls;
				if (tls != null)
				{
					await ForwardAsync(game, server, tls);
				}
			}
			catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is OperationCanceledException)
			{
			}
		}
	}

	/// <summary>The TLS stream after the server's OK, or the refusal once it has been reported.</summary>
	private async Task<(SslStream? Tls, string? Refusal)> OpenAsync(TcpClient server, string service)
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
		timeout.CancelAfter(TimeSpan.FromSeconds(15.0));
		try
		{
			await server.ConnectAsync(address, settings.TlsPort, timeout.Token);
		}
		catch (Exception ex) when (ex is SocketException || ex is OperationCanceledException)
		{
			Dialled(service, ok: false);
			string unanswered = $"{settings.Host} did not answer. Check your internet, then press Play Online again; if it keeps happening the server may be down.";
			Report(service, unanswered, problem: true);
			return (null, unanswered);
		}
		string? refusal = null;
		string message;
		SslStream tls = new SslStream(server.GetStream(), leaveInnerStreamOpen: false);
		try
		{
			await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
			{
				TargetHost = settings.Host,
				EnabledSslProtocols = CommunityServer.Protocols,
				RemoteCertificateValidationCallback = (_, certificate, _, errors) => (refusal = CommunityServer.CertificateProblem(settings, certificate, errors)) == null
			}, timeout.Token);
			await tls.WriteAsync(Encoding.ASCII.GetBytes("FGOAC-TUNNEL 1 " + service + " " + accessCode + "\n"), timeout.Token);
			string? reply = await ReadLineAsync(tls, timeout.Token);
			if (reply == "OK")
			{
				Report(service, $"carried over TLS to {settings.Host}:{settings.TlsPort}.", problem: false);
				Dialled(service, ok: true);
				return (tls, null);
			}
			message = Refused(service, reply);
			Report(service, message, problem: true);
			// A refused code is never offered again: the server counts each ERR auth against this address.
			if (reply is "ERR auth" or "ERR banned" or "ERR version")
			{
				KeepBack(settings, accessCode, message);
				if (Interlocked.Exchange(ref refused, 1) == 0)
				{
					Report(service, "Further connections are refused on this PC until the game is restarted.", problem: false);
				}
			}
		}
		catch (Exception ex) when (ex is AuthenticationException || ex is IOException || ex is OperationCanceledException)
		{
			message = (refusal != null) ? CommunityServer.CertificateRefused(settings, refusal) : CommunityServer.SecureConnectionFailed(settings);
			Report(service, message, problem: true);
		}
		Dialled(service, ok: false);
		tls.Dispose();
		return (null, message);
	}

	/// <summary>Records how the service's dial ended: OK starts warming it, or every service on the run's first OK, and refills; anything else stops warming it.</summary>
	private void Dialled(string service, bool ok)
	{
		string[] start = Array.Empty<string>();
		lock (warm)
		{
			if (ok)
			{
				start = warmed ? new string[1] { service } : WarmSizes.Keys.ToArray();
				warmed = true;
			}
			foreach (string name in start)
			{
				warming[name] = true;
			}
			warming[service] = ok;
		}
		foreach (string name in start)
		{
			Fill(name);
		}
	}

	/// <summary>The oldest live warm connection of the service, or null; the dead and aged ones are closed, and the pool is refilled.</summary>
	private Warm? Take(string service)
	{
		List<Warm> dead = new List<Warm>();
		Warm? found;
		lock (warm)
		{
			Prune(warm[service], dead);
			warm[service].TryDequeue(out found);
		}
		dead.ForEach((Warm connection) => connection.Close());
		Fill(service);
		return found;
	}

	/// <summary>Moves the pool's warm connections that are dead or as old as warmFor to dead, keeping the order of the rest; the caller holds the warm lock.</summary>
	private void Prune(Queue<Warm> pool, List<Warm> dead)
	{
		long now = Environment.TickCount64;
		for (int i = pool.Count; i > 0; i--)
		{
			Warm next = pool.Dequeue();
			if (next.Live(now, warmFor))
			{
				pool.Enqueue(next);
			}
			else
			{
				dead.Add(next);
			}
		}
	}

	/// <summary>While the service is warming and a game can connect, dials its warm connections up to its size, counting those already dialling.</summary>
	private void Fill(string service)
	{
		int missing;
		lock (warm)
		{
			if (!warming[service] || listeners.Count == 0 || Volatile.Read(ref refused) != 0 || stopping.IsCancellationRequested)
			{
				return;
			}
			missing = WarmSizes[service] - warm[service].Count - dialling[service];
			if (missing > 0)
			{
				dialling[service] += missing;
			}
		}
		for (int i = 0; i < missing; i++)
		{
			_ = DialWarmAsync(service);
		}
	}

	/// <summary>Dials one warm connection the caller counted in dialling: null once it is kept or the tunnel has stopped, otherwise the refusal.</summary>
	private async Task<string?> DialWarmAsync(string service)
	{
		TcpClient server = new TcpClient(AddressFamily.InterNetwork)
		{
			NoDelay = true
		};
		SslStream? tls = null;
		string? refusal = null;
		try
		{
			(tls, refusal) = await OpenAsync(server, service);
		}
		catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is OperationCanceledException)
		{
		}
		lock (warm)
		{
			dialling[service]--;
			if (tls != null && !stopping.IsCancellationRequested)
			{
				warm[service].Enqueue(new Warm(server, tls));
				return null;
			}
		}
		tls?.Dispose();
		server.Dispose();
		return refusal;
	}

	/// <summary>Closes the dead warm connections and those as old as warmFor, then refills; every 5 s or a quarter of warmFor, so none outlives warmFor by more than a tick.</summary>
	private async Task RefreshAsync()
	{
		TimeSpan tick = TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(5.0).Ticks, warmFor.Ticks / 4));
		while (true)
		{
			try
			{
				await Task.Delay(tick, stopping.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			List<Warm> dead = new List<Warm>();
			lock (warm)
			{
				foreach (Queue<Warm> pool in warm.Values)
				{
					Prune(pool, dead);
				}
			}
			dead.ForEach((Warm connection) => connection.Close());
			foreach (string service in WarmSizes.Keys)
			{
				Fill(service);
			}
		}
	}

	/// <summary>One LF-terminated ASCII line without its CR or LF, cut off after 64 bytes; null when the server closes first.</summary>
	private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken token)
	{
		byte[] one = new byte[1];
		StringBuilder line = new StringBuilder();
		while (line.Length < 64)
		{
			if (await stream.ReadAsync(one, token) == 0)
			{
				return null;
			}
			if (one[0] == (byte)'\n')
			{
				return line.ToString().TrimEnd('\r');
			}
			line.Append((char)one[0]);
		}
		return line.ToString();
	}

	private string Refused(string service, string? reply)
	{
		string host = settings.Host;
		return reply switch
		{
			null => $"{host} did not answer. Check your internet, then press Play Online again; if it keeps happening the server may be down.",
			"ERR auth" => $"{host} does not know this PC's account. Choose Recover account on the Online page and paste your recovery key.",
			"ERR banned" => $"{host} has banned this account; ask the server's operator.",
			"ERR busy" => $"{host} is not taking new players right now. Wait a few minutes, then press Play Online.",
			"ERR version" => $"{host} needs a newer launcher; install the update from About.",
			"ERR service" => $"{host} does not carry the game's {service} connection; ask the operator.",
			_ => $"{host} sent a reply this launcher cannot use; check the secure port."
		};
	}

	/// <summary>Copies both ways until both directions end; the end of one direction closes the sending half of the other side.</summary>
	private static async Task ForwardAsync(TcpClient game, TcpClient server, SslStream tls)
	{
		NetworkStream local = game.GetStream();
		Task up = CopyAsync(local, tls, async delegate
		{
			await tls.ShutdownAsync();
			server.Client.Shutdown(SocketShutdown.Send);
		});
		Task down = CopyAsync(tls, local, delegate
		{
			game.Client.Shutdown(SocketShutdown.Send);
			return Task.CompletedTask;
		});
		if ((await Task.WhenAny(up, down)).IsFaulted)
		{
			game.Dispose();
			server.Dispose();
		}
		await Task.WhenAll(up, down);
	}

	private static async Task CopyAsync(Stream from, Stream to, Func<Task> ended)
	{
		await from.CopyToAsync(to);
		await ended();
	}

	private void Report(string service, string message, bool problem)
	{
		lock (reported)
		{
			if (stopping.IsCancellationRequested || !reported.Add(service + " " + message))
			{
				return;
			}
		}
		report(service + ": " + message, problem);
	}
}
