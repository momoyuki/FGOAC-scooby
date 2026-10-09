using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

public sealed partial class CommunityTunnel
{
	/// <summary>The refusal of each settings and card the server refused with auth, banned or version during this launcher run.</summary>
	private static readonly Dictionary<(CommunityServer.Settings Settings, string Card), string> keptBack = new Dictionary<(CommunityServer.Settings, string), string>();

	/// <summary>Records a card the server refused for good, so the probe on the Online page does not offer it again.</summary>
	private static void KeepBack(CommunityServer.Settings settings, string accessCode, string refusal)
	{
		lock (keptBack)
		{
			keptBack[(settings, accessCode)] = refusal;
		}
	}

	/// <summary>How long a probe waits for the answer to each ping before it sends the next.</summary>
	private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(1.0);

	/// <summary>How many pings a probe sends before it reports the ping unanswered.</summary>
	private const int PingTries = 3;

	/// <summary>
	/// The Probe on the tunnel Play started, before the game: one title dial within 15 s, then after the server's OK,
	/// which is kept as the first warm title connection, the ping. Null when the ping is answered; otherwise the refusal
	/// or the unanswered ping, reported as every dial's is.
	/// </summary>
	public async Task<string?> ProbeAsync()
	{
		lock (warm)
		{
			dialling["title"]++;
		}
		return await DialWarmAsync("title") ?? await PingAsync();
	}

	/// <summary>
	/// The Probe on the Online page, with no game to carry: nothing is bound on this PC, and one
	/// title dial within 15 s is closed after the server's OK, then the ping is sent. Null when the ping is answered,
	/// otherwise the refusal, the unanswered ping or why the address was not found. A card the server refused with auth,
	/// banned or version in this launcher run is not offered again: its refusal comes back without a dial.
	/// </summary>
	public static async Task<string?> ProbeAsync(CommunityServer.Settings settings, string accessCode, Action<string, bool> report)
	{
		lock (keptBack)
		{
			if (keptBack.TryGetValue((settings, accessCode), out string? kept))
			{
				return kept;
			}
		}
		IPAddress address;
		try
		{
			address = await Task.Run(() => Resolve(settings.Host));
		}
		catch (IOException ex)
		{
			return ex.Message;
		}
		using CommunityTunnel tunnel = new CommunityTunnel(settings, address, accessCode, report, TimeSpan.Zero, TimeSpan.Zero);
		using TcpClient server = new TcpClient(AddressFamily.InterNetwork);
		(SslStream? tls, string? refusal) = await tunnel.OpenAsync(server, "title");
		tls?.Dispose();
		return refusal ?? await tunnel.PingAsync();
	}

	/// <summary>
	/// The ping the game sends before it matches, to the server's UDP ping port: 17 09 20 15 and the time as a big-endian
	/// 32-bit count of seconds, sent again each PingEvery it is unanswered, PingTries times at most, as the game repeats
	/// it. Null when a datagram starting with those 8 bytes comes back; otherwise the sentence, reported as a problem.
	/// </summary>
	private async Task<string?> PingAsync()
	{
		byte[] ping = new byte[8] { 0x17, 0x09, 0x20, 0x15, 0, 0, 0, 0 };
		BinaryPrimitives.WriteUInt32BigEndian(ping.AsSpan(4), (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
		using UdpClient udp = new UdpClient(AddressFamily.InterNetwork);
		try
		{
			udp.Connect(address, settings.UdpPingPort);
			for (int tries = 0; tries < PingTries; tries++)
			{
				await udp.SendAsync(ping, stopping.Token);
				using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
				wait.CancelAfter(PingEvery);
				try
				{
					while (!(await udp.ReceiveAsync(wait.Token)).Buffer.AsSpan().StartsWith(ping))
					{
					}
					return null;
				}
				catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
				{
				}
			}
		}
		catch (Exception ex) when (ex is SocketException || ex is OperationCanceledException)
		{
		}
		string unanswered = $"{settings.Host} did not answer a ping on UDP port {settings.UdpPingPort}, which the game needs for matching. Check the ping port on the Online page; if it is right, a firewall or router may be blocking UDP.";
		Report("ping", unanswered, problem: true);
		return unanswered;
	}
}
