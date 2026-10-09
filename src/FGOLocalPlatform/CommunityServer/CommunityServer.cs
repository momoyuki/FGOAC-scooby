using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace FGOLocalPlatform;

/// <summary>
/// The hosted community server: its "communityServer" block in fgo-launcher.json, the account calls
/// it answers over HTTPS on its secure port, the certificate check shared with CommunityTunnel, and the
/// card this PC keeps for it under DEVICE\community\&lt;host&gt;.
/// The local server settings (serverHost, serverPorts, requiredPorts) are never written here; serverPorts
/// is read only to keep the block off the local server's ports.
/// </summary>
public static class CommunityServer
{
	/// <summary>Whether the Online page and Play Online are offered; off in this release, so every path to them stays closed.</summary>
	internal static readonly bool Offered = false;

	/// <summary>True when the last session was Play Online's and Online is offered; the mode the start-up checks and the first page follow.</summary>
	internal static bool OnlineSession(string gameRoot)
	{
		return Offered && Load(gameRoot).Enabled;
	}

	/// <summary>Host, TlsPort, CertificatePin and the two UDP ports name the server; Http to Dist are the loopback ports the game uses on this PC.</summary>
	public sealed record Settings(bool Enabled, string Host, int TlsPort, string CertificatePin, int UdpPingPort, int UdpDistPort, int Http, int Billing, int Aime, int Ms, int Ping, int Dist);

	/// <summary>MasterName is null when the reply did not name the account's Master.</summary>
	public sealed record Account(string AccessCode, string RecoveryKey, string? MasterName = null);

	/// <summary>The server refused the Master name sent with a new account: name_taken or name_invalid.</summary>
	public sealed class MasterNameRefusedException : IOException
	{
		public MasterNameRefusedException(string message)
			: base(message)
		{
		}
	}

	/// <summary>The TLS versions the tunnel and the account calls accept; never TLS 1.0 or 1.1.</summary>
	public const SslProtocols Protocols = SslProtocols.Tls12 | SslProtocols.Tls13;

	/// <summary>Keeps this launcher's machine id apart from any other hash of the same identifiers.</summary>
	private const string MachineIdSalt = "FGOAC scooby community machine id 1";

	private const string NotRecoveryKey = "That is not a recovery key. Check it against the one you saved: eight groups of four letters and digits.";

	/// <summary>The loopback ports for the game's title, billing and aime connections; the local server keeps 80, 8443 and 22345.</summary>
	private const int DefaultHttp = 40080, DefaultBilling = 48443, DefaultAime = 42345;

	/// <summary>The loopback ports for the game's matching, ping and battle traffic: the server names them to the game, so they are never saved.</summary>
	public const int MsPort = 30110, PingPort = 30111, DistPort = 30112;

	/// <summary>Port lines already written to logs\server-control.log, so the status tick writes each once.</summary>
	private static readonly HashSet<string> loggedPorts = new HashSet<string>();

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern bool GetVolumeInformation(string rootPathName, StringBuilder? volumeName, int volumeNameSize, out uint serialNumber, IntPtr maximumComponentLength, IntPtr fileSystemFlags, StringBuilder? fileSystemName, int fileSystemNameSize);

	public static Settings Load(string gameRoot)
	{
		Settings settings = new Settings(Enabled: false, Host: "", TlsPort: 443, CertificatePin: "", UdpPingPort: 30111, UdpDistPort: 30112, Http: DefaultHttp, Billing: DefaultBilling, Aime: DefaultAime, Ms: MsPort, Ping: PingPort, Dist: DistPort);
		string path = Path.Combine(gameRoot, "fgo-launcher.json");
		// Read on the status tick, so a hand-edited file falls back to the defaults instead of throwing.
		try
		{
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject root && root["communityServer"] is JsonObject node)
			{
				JsonObject? ports = node["ports"] as JsonObject;
				bool enabled = node["enabled"] is JsonValue value && value.TryGetValue(out bool on) && on;
				settings = new Settings(enabled, Text(node, "host"), Port(gameRoot, node, "tlsPort", settings.TlsPort), Text(node, "certificatePin"), Port(gameRoot, node, "udpPingPort", settings.UdpPingPort), Port(gameRoot, node, "udpDistPort", settings.UdpDistPort), Port(gameRoot, ports, "http", settings.Http), Port(gameRoot, ports, "billing", settings.Billing), Port(gameRoot, ports, "aime", settings.Aime), MsPort, PingPort, DistPort);
				if (ports != null)
				{
					settings = MoveOffLocalPorts(gameRoot, path, root, ports, settings);
				}
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
		{
		}
		return settings;
	}

	/// <summary>
	/// A block whose title, billing and aime ports are the local server's (its saved ports or the
	/// defaults 80, 8443 and 22345) gets the default loopback ports, written back once; when the file
	/// cannot be written the block stays as it is, so the launch script reads the ports the tunnel uses.
	/// </summary>
	private static Settings MoveOffLocalPorts(string gameRoot, string path, JsonObject root, JsonObject ports, Settings settings)
	{
		JsonObject? local = root["serverPorts"] as JsonObject;
		bool onLocal = settings.Http == LocalPort(local, "http", 80) && settings.Billing == LocalPort(local, "billing", 8443) && settings.Aime == LocalPort(local, "aime", 22345);
		bool onOldDefaults = settings.Http == 80 && settings.Billing == 8443 && settings.Aime == 22345;
		bool onDefaults = settings.Http == DefaultHttp && settings.Billing == DefaultBilling && settings.Aime == DefaultAime;
		if (!(onLocal || onOldDefaults) || onDefaults)
		{
			return settings;
		}
		ports["http"] = DefaultHttp;
		ports["billing"] = DefaultBilling;
		ports["aime"] = DefaultAime;
		try
		{
			AtomicFile.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}) + Environment.NewLine);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			return settings;
		}
		LogPort(gameRoot, $"The community server's game ports on this PC were {settings.Http}, {settings.Billing} and {settings.Aime}, the local server's, so they were moved to {DefaultHttp}, {DefaultBilling} and {DefaultAime}.");
		return settings with { Http = DefaultHttp, Billing = DefaultBilling, Aime = DefaultAime };
	}

	private static int LocalPort(JsonObject? local, string name, int fallback)
	{
		return (local?[name] is JsonValue value && value.TryGetValue(out int port)) ? port : fallback;
	}

	private static string Text(JsonObject node, string name)
	{
		if (node[name] is JsonValue value && value.TryGetValue(out string? text))
		{
			return text;
		}
		return "";
	}

	private static int Port(string gameRoot, JsonObject? ports, string name, int fallback)
	{
		if (ports?[name] is JsonValue value && value.TryGetValue(out int port))
		{
			if (port >= 1 && port <= 65535)
			{
				return port;
			}
			LogPort(gameRoot, $"The community server port {name} in App\\fgo-launcher.json is {port}, which is not a port from 1 to 65535, so {fallback} is used. Save the address on the Online page to store a valid port.");
		}
		return fallback;
	}

	private static void LogPort(string gameRoot, string line)
	{
		lock (loggedPorts)
		{
			if (!loggedPorts.Add(line))
			{
				return;
			}
		}
		try
		{
			LauncherLog.Append(Path.GetFullPath(Path.Combine(gameRoot, "..", "logs")), line);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
		}
	}

	public static void Save(string gameRoot, Settings settings)
	{
		string path = Path.Combine(gameRoot, "fgo-launcher.json");
		JsonObject root = (JsonNode.Parse(File.ReadAllText(path)) as JsonObject) ?? throw new InvalidDataException("App\\fgo-launcher.json does not hold a settings object. Restore it from fgo-launcher.json.bak, then save again.");
		root["communityServer"] = new JsonObject
		{
			["enabled"] = settings.Enabled,
			["host"] = settings.Host,
			["tlsPort"] = settings.TlsPort,
			["certificatePin"] = settings.CertificatePin,
			["udpPingPort"] = settings.UdpPingPort,
			["udpDistPort"] = settings.UdpDistPort,
			["ports"] = new JsonObject
			{
				["http"] = settings.Http,
				["billing"] = settings.Billing,
				["aime"] = settings.Aime
			}
		};
		AtomicFile.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}) + Environment.NewLine);
	}

	/// <summary>The address as the card folder and the game use it; throws for anything but a host name or IPv4 address off this PC.</summary>
	public static string NormalizeHost(string text)
	{
		string host = text.Trim().TrimEnd('.').ToLowerInvariant();
		UriHostNameType type = Uri.CheckHostName(host);
		// These are the values the launch script treats as the server on this PC.
		bool local = host is "auto" or "local" or "localhost" or "192.168.100.1";
		if (type == UriHostNameType.IPv4)
		{
			IPAddress address = IPAddress.Parse(host);
			byte first = address.GetAddressBytes()[0];
			local |= IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || (first >= 224 && first <= 239);
		}
		if ((type != UriHostNameType.Dns && type != UriHostNameType.IPv4) || local)
		{
			throw new ArgumentException("Enter the community server's address: a host name such as play.example.org, or an IPv4 address. auto, localhost and 127.0.0.1 mean the server on this PC, which Play uses.");
		}
		return host;
	}

	/// <summary>The certificate's SHA-256 fingerprint as 64 upper-case hex digits without separators, or "" for none.</summary>
	public static string NormalizePin(string text)
	{
		string pin = Regex.Replace(text, "[\\s:]", "").ToUpperInvariant();
		if (pin.Length != 0 && !Regex.IsMatch(pin, "^[0-9A-F]{64}$"))
		{
			throw new ArgumentException("The certificate fingerprint is the SHA-256 fingerprint the server's operator gives: 64 hexadecimal digits, colons allowed. Leave it empty when the operator gives none.");
		}
		return pin;
	}

	/// <summary>Why the certificate is refused, or null: the name must match the host, and the pin, when set, stands in for Windows trust.</summary>
	public static string? CertificateProblem(Settings settings, X509Certificate? certificate, SslPolicyErrors errors)
	{
		if (certificate == null || (errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
		{
			return "it sent no certificate";
		}
		if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
		{
			return "it is made out to another address";
		}
		if (settings.CertificatePin.Length == 0)
		{
			return (errors == SslPolicyErrors.None) ? null : "it is not trusted by Windows";
		}
		string fingerprint = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
		return string.Equals(fingerprint, settings.CertificatePin, StringComparison.OrdinalIgnoreCase) ? null : "it does not match the saved fingerprint";
	}

	public static string CertificateRefused(Settings settings, string problem)
	{
		return $"{settings.Host} failed the certificate check: {problem}. Nothing was sent. Check the fingerprint on the Online page or ask the operator.";
	}

	public static string SecureConnectionFailed(Settings settings)
	{
		return $"Could not make a secure connection to {settings.Host} on port {settings.TlsPort}. Check that this is the secure port the operator gives.";
	}

	public static string CardPath(string gameRoot, string host)
	{
		return Path.GetFullPath(Path.Combine(gameRoot, "..", "DEVICE", "community", NormalizeHost(host), "aime.txt"));
	}

	/// <summary>The access code this PC holds for the server, or null when it holds none.</summary>
	public static string? ReadCard(string gameRoot, string host)
	{
		string path = CardPath(gameRoot, host);
		if (!File.Exists(path))
		{
			return null;
		}
		string code = File.ReadAllText(path).Trim();
		return IsAccessCode(code) ? code : null;
	}

	/// <summary>Writes the card as segatools reads it: 20 digits and one CRLF. A card already there is kept as aime.txt.bak.</summary>
	public static void WriteCard(string gameRoot, string host, string accessCode)
	{
		AtomicFile.WriteAllText(CardPath(gameRoot, host), accessCode + "\r\n");
	}

	/// <summary>The Master name file beside the card: the name the account was made with, one line.</summary>
	public static string MasterNamePath(string gameRoot, string host)
	{
		return Path.Combine(Path.GetDirectoryName(CardPath(gameRoot, host))!, "master-name.txt");
	}

	/// <summary>The Master name this PC knows for its account on the server, or null when it knows none.</summary>
	public static string? ReadMasterName(string gameRoot, string host)
	{
		try
		{
			string path = MasterNamePath(gameRoot, host);
			string name = File.Exists(path) ? File.ReadAllText(path).TrimEnd('\r', '\n') : "";
			return (MasterNameProblem(name) == null) ? name : null;
		}
		catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
		{
			return null;
		}
	}

	/// <summary>Why a Master name is refused before it is sent, or null: 1 to 16 of A-Z, a-z, 0-9, space, _ and -, with no space at either end and never two in a row.</summary>
	public static string? MasterNameProblem(string name)
	{
		if (name.Length == 0)
		{
			return "Enter a Master name.";
		}
		if (name.Length > 16)
		{
			return "A Master name has at most 16 characters.";
		}
		if (!Regex.IsMatch(name, "^[A-Za-z0-9 _-]+\\z"))
		{
			return "A Master name uses only the letters A to Z, digits, spaces, _ and -.";
		}
		if (name[0] == ' ' || name[name.Length - 1] == ' ')
		{
			return "A Master name cannot start or end with a space.";
		}
		if (name.Contains("  "))
		{
			return "A Master name cannot have two spaces in a row.";
		}
		return null;
	}

	/// <summary>
	/// Waits for an account request and stores the card it brings, with its Master name when the account names one;
	/// StoreError says why the card could not be stored, or is null.
	/// </summary>
	public static async Task<(Account Account, string? StoreError)> TakeAccountAsync(string gameRoot, string host, Task<Account> request)
	{
		Account account = await request;
		try
		{
			WriteCard(gameRoot, host, account.AccessCode);
			// A recovered account whose reply names no Master may be another account than the one named here.
			string namePath = MasterNamePath(gameRoot, host);
			if (account.MasterName != null)
			{
				AtomicFile.WriteAllText(namePath, account.MasterName + "\r\n");
			}
			else if (File.Exists(namePath))
			{
				File.Delete(namePath);
			}
			return (account, null);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			return (account, ex.Message);
		}
	}

	/// <summary>Same rule as fgo_account.py: 20 digits, never starting with 3 (the Banapass prefix).</summary>
	private static bool IsAccessCode(string code)
	{
		return Regex.IsMatch(code, "^[0-9]{20}$") && code[0] != '3';
	}

	/// <summary>A new account with the Master name, chosen once; the body carries the machine id only when the player leaves that box on.</summary>
	public static async Task<Account> CreateAccountAsync(Settings settings, string masterName, bool sendMachineId)
	{
		string? problem = MasterNameProblem(masterName);
		if (problem != null)
		{
			throw new ArgumentException(problem);
		}
		JsonObject body = new JsonObject
		{
			["master_name"] = masterName
		};
		if (sendMachineId)
		{
			body["machine_id"] = MachineId();
		}
		JsonObject reply = await PostAsync(settings, "/community/account", body, new Dictionary<HttpStatusCode, string>
		{
			[HttpStatusCode.NotFound] = "The community server " + settings.Host + " does not hand out accounts. Check that the address and the secure port are the community server's.",
			[HttpStatusCode.TooManyRequests] = "The community server " + settings.Host + " has had too many account requests from your address. Wait up to a day, then try again."
		});
		string code = Text(reply, "access_code");
		string key = Text(reply, "recovery_key");
		if (!IsAccessCode(code) || key.Trim().Length == 0)
		{
			throw new IOException(UnreadableReply(settings));
		}
		return new Account(code, key, RepliedMasterName(reply) ?? masterName);
	}

	/// <summary>The Master name a reply carries in master_name, or null when it carries none that the name rule allows.</summary>
	private static string? RepliedMasterName(JsonObject reply)
	{
		string name = Text(reply, "master_name");
		return (MasterNameProblem(name) == null) ? name : null;
	}

	/// <summary>SHA-256 of the salt, the Windows machine GUID and the system drive's volume serial, as 64 lower-case hex digits; neither identifier can be read back from it.</summary>
	private static string MachineId()
	{
		using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
		using RegistryKey? cryptography = machine.OpenSubKey("SOFTWARE\\Microsoft\\Cryptography");
		string? guid = cryptography?.GetValue("MachineGuid") as string;
		if (guid == null || !GetVolumeInformation(Path.GetPathRoot(Environment.SystemDirectory)!, null, 0, out uint serial, IntPtr.Zero, IntPtr.Zero, null, 0))
		{
			throw new IOException("Could not read this PC's machine id. Untick \"Send this PC's machine id\" in the New Account dialog, then try again.");
		}
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(MachineIdSalt + "\n" + guid + "\n" + serial.ToString("X8")));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	/// <summary>Why a recovery key is refused before it is sent, or null: a key as the server issues it is 32 of A-Z and 2-7 once spaces and dashes are dropped.</summary>
	public static string? RecoveryKeyProblem(string key)
	{
		return Regex.IsMatch(Regex.Replace(key, "[\\s-]", "").ToUpperInvariant(), "^[A-Z2-7]{32}\\z") ? null : NotRecoveryKey;
	}

	/// <summary>The account holding the key, with the new card code and the new recovery key the server rotates to; the old key stops working.</summary>
	public static async Task<Account> RecoverAccountAsync(Settings settings, string recoveryKey)
	{
		JsonObject reply = await PostAsync(settings, "/community/account/recover", new JsonObject
		{
			["recovery_key"] = recoveryKey
		}, new Dictionary<HttpStatusCode, string>
		{
			[HttpStatusCode.BadRequest] = NotRecoveryKey,
			[HttpStatusCode.NotFound] = "The community server " + settings.Host + " does not know that recovery key. Check it against the one you saved, then try again.",
			[HttpStatusCode.TooManyRequests] = "The community server " + settings.Host + " has had too many recovery requests from your address. Wait ten minutes, then try again."
		});
		string code = Text(reply, "access_code");
		string key = Text(reply, "recovery_key");
		if (!IsAccessCode(code) || key.Trim().Length == 0)
		{
			throw new IOException(UnreadableReply(settings));
		}
		return new Account(code, key, RepliedMasterName(reply));
	}

	private static string UnreadableReply(Settings settings)
	{
		return "The community server " + settings.Host + " sent a reply this launcher cannot use. Check that the address and the secure port are the community server's.";
	}

	private static async Task<JsonObject> PostAsync(Settings settings, string route, JsonObject body, IReadOnlyDictionary<HttpStatusCode, string> refusals)
	{
		HttpStatusCode status;
		string text;
		string? refusal = null;
		using HttpClient client = new HttpClient(new HttpClientHandler
		{
			SslProtocols = Protocols,
			AllowAutoRedirect = false,
			ServerCertificateCustomValidationCallback = (_, certificate, _, errors) => (refusal = CertificateProblem(settings, certificate, errors)) == null
		})
		{
			Timeout = TimeSpan.FromSeconds(15.0)
		};
		try
		{
			using StringContent content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await client.PostAsync($"https://{settings.Host}:{settings.TlsPort}{route}", content);
			status = response.StatusCode;
			text = await response.Content.ReadAsStringAsync();
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
		{
			if (refusal != null)
			{
				throw new IOException(CertificateRefused(settings, refusal), ex);
			}
			if (ex.InnerException is AuthenticationException || ex.InnerException is IOException)
			{
				throw new IOException(SecureConnectionFailed(settings), ex);
			}
			string reason = (ex is TaskCanceledException) ? "it did not answer within 15 seconds" : ex.Message.TrimEnd('.');
			throw new IOException($"Could not reach the community server {settings.Host} on port {settings.TlsPort}: {reason}. Check the address and the secure port, and that the server is running, then try again.", ex);
		}
		if ((int)status >= 400 && (int)status <= 499 && NameRefusal(text) is string refusedName)
		{
			throw new MasterNameRefusedException(refusedName);
		}
		if (refusals.TryGetValue(status, out string? sentence))
		{
			throw new IOException(sentence);
		}
		if (status == HttpStatusCode.Forbidden && IsBanned(text))
		{
			throw new IOException("The community server " + settings.Host + " has banned this PC or address from creating accounts. Ask the server's operator.");
		}
		if ((int)status < 200 || (int)status > 299)
		{
			throw new IOException($"The community server {settings.Host} turned the request down (HTTP {(int)status}). Try again later; if it keeps happening, ask the server's operator.");
		}
		try
		{
			return JsonNode.Parse(text) as JsonObject ?? throw new IOException(UnreadableReply(settings));
		}
		catch (JsonException)
		{
			throw new IOException(UnreadableReply(settings));
		}
	}

	/// <summary>The sentence for a Master name the server refused, by the reply's error or reason: name_taken or name_invalid; null for any other reply.</summary>
	private static string? NameRefusal(string text)
	{
		try
		{
			string reason = (JsonNode.Parse(text) is JsonObject reply) ? (Text(reply, "error") is { Length: > 0 } error ? error : Text(reply, "reason")) : "";
			return reason switch
			{
				"name_taken" => "That name is taken on this server. Try another.",
				"name_invalid" => "This server does not accept that name. Try another.",
				_ => null
			};
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>True for the {"error": "banned"} body the server sends with 403 for a banned address or machine.</summary>
	private static bool IsBanned(string text)
	{
		try
		{
			return JsonNode.Parse(text) is JsonObject reply && Text(reply, "error") == "banned";
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
