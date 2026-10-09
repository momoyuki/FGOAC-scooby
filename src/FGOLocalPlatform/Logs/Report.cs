using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// The zip a player saves to ask for help: the Game log, the logs, the render logs, the settings file, the display
/// snapshot, the graphics and hook files in App\ with their SHA-256 and the newest crash dumps, with the card, the
/// recovery key and the community server's address and fingerprint replaced by &lt;redacted&gt; in every text entry.
/// Never the card file or DEVICE; nothing is sent anywhere.
/// </summary>
public static class Report
{
	/// <summary>What the window knows and no file holds: the Game log panel's text and the last session's exit code.</summary>
	public sealed record Facts(string Version, string Mode, string Status, string GameLog, int? LastExit);

	/// <summary>A text log keeps only its last megabyte.</summary>
	private const int TextTailBytes = 1024 * 1024;

	/// <summary>A crash dump over this size is named in the manifest instead of added.</summary>
	private const long DumpCap = 8L * 1024 * 1024;

	private static readonly string[] NamedLogs = new string[11]
	{
		"fgo-last-launch.log", "fgo-inject-live.log", "environment-check.txt", "update.log", "fgo.log", "fgozh.log", "artemis-stdout.log", "artemis-stderr.log", "mariadb-stdout.log", "mariadb-stderr.log", "mariadb.log"
	};

	/// <summary>Cloud23333's render logs (RenderDiagnostics.cs), all of them, from the logs folder.</summary>
	private static readonly string[] RenderLogPatterns = new string[3] { "aft-render-*.jsonl", "fgo-present-*.csv", "fgo-present-*.json" };

	/// <summary>Cloud23333's graphics and hook files (RenderDiagnostics.cs), then the shim, its target, the add-on and the game, under App\.</summary>
	private static readonly string[] FilesOnDisk = new string[9] { "fgoglcompat.dll", "fgohook.dll", "gpu-runtime/opengl32.dll", "FGO_GpuRouting.ps1", "gpu-runtime/FgoGpuProbe.dll", "opengl32.dll", "opengl32real.dll", "fgoacaddon.dll", "ago.exe" };

	public static string FileName(DateTime now)
	{
		return "FGOAC-report-" + now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + ".zip";
	}

	/// <summary>Writes the Report to zipPath, replacing a file there; tempRoot is where the patch leaves its transcript.</summary>
	public static void Save(string zipPath, string gameRoot, string tempRoot, Facts facts)
	{
		string logs = Path.GetFullPath(Path.Combine(gameRoot, "..", "logs"));
		(string settings, string[] community) = ReadSettings(Path.Combine(gameRoot, "fgo-launcher.json"));
		Func<string, string> redact = (string text) => community.Aggregate(Redaction.Apply(text), (string current, string secret) => current.Replace(secret, Redaction.Mark, StringComparison.OrdinalIgnoreCase));
		List<string> inside = new List<string>();
		List<string> leftOut = new List<string>();
		List<string> notFound = new List<string>();
		string partial = zipPath + ".partial";
		File.Delete(partial);
		using (ZipArchive zip = ZipFile.Open(partial, ZipArchiveMode.Create))
		{
			void AddText(string entry, string text, string note)
			{
				WriteEntry(zip, entry, Encoding.UTF8.GetBytes(redact(text)));
				inside.Add(entry + note);
			}
			void AddLog(string? path, string entry)
			{
				if (path == null || !File.Exists(path))
				{
					notFound.Add(entry);
					return;
				}
				try
				{
					(string text, bool cut) = ReadTail(path);
					AddText(entry, text, cut ? " (its last megabyte)" : "");
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					leftOut.Add(entry + " (could not be read: " + ex.Message + ")");
				}
			}
			AddText("game-log.txt", facts.GameLog, " (the Game log in the Logs drawer)");
			if (settings.Length > 0)
			{
				AddText("fgo-launcher.json", settings, " (the settings file)");
			}
			AddText("display-presentation.json", DisplayPresentationDiagnostics.Capture(gameRoot).ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}), " (the monitors and the game window when the report was saved)");
			AddText("files-on-disk.json", ListFiles(gameRoot).ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}), " (size and SHA-256 of the graphics and hook files in App\\)");
			AddLog(Path.Combine(logs, LauncherLog.FileName), "logs/" + LauncherLog.FileName);
			string? previous = LauncherLog.Previous(logs);
			if (previous != null)
			{
				AddLog(previous, "logs/" + Path.GetFileName(previous));
			}
			foreach (string name in NamedLogs)
			{
				AddLog(Path.Combine(logs, name), "logs/" + name);
			}
			if (Directory.Exists(logs))
			{
				string[] renderLogs = RenderLogPatterns.SelectMany((string pattern) => Directory.GetFiles(logs, pattern, SearchOption.TopDirectoryOnly)).ToArray();
				Array.Sort(renderLogs, StringComparer.OrdinalIgnoreCase);
				foreach (string renderLog in renderLogs)
				{
					AddLog(renderLog, "logs/" + Path.GetFileName(renderLog));
				}
			}
			string? amdaemonLog = Newest(logs, "amdaemon-*-amdaemon.exe.log", out _);
			if (amdaemonLog != null)
			{
				AddLog(amdaemonLog, "logs/" + Path.GetFileName(amdaemonLog));
			}
			string? transcript = Newest(tempRoot, "fgoa-scooby-patch-*.log", out _);
			if (transcript != null)
			{
				AddLog(transcript, "temp/" + Path.GetFileName(transcript));
			}
			int older = 0;
			foreach (string pattern in new string[2] { "ago-crash-*.dmp", "amdaemon-*-tmp.dmp" })
			{
				string? dump = Newest(logs, pattern, out int others);
				older += others;
				if (dump == null)
				{
					continue;
				}
				string entry = "logs/" + Path.GetFileName(dump);
				long size = new FileInfo(dump).Length;
				if (size > DumpCap)
				{
					leftOut.Add($"{entry} ({Megabytes(size)}, over the {Megabytes(DumpCap)} limit for a crash dump; attach it yourself if asked)");
					continue;
				}
				try
				{
					zip.CreateEntryFromFile(dump, entry, CompressionLevel.Optimal);
					inside.Add($"{entry} ({Megabytes(size)}, a crash dump, not redacted)");
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					leftOut.Add(entry + " (could not be read: " + ex.Message + ")");
				}
			}
			if (older > 0)
			{
				leftOut.Add($"{older} older crash dump{(older == 1 ? "" : "s")}, still in the logs folder");
			}
			WriteEntry(zip, "manifest.txt", Encoding.UTF8.GetBytes(redact(Manifest(facts, inside, leftOut, notFound))));
		}
		File.Move(partial, zipPath, overwrite: true);
	}

	/// <summary>Each of FilesOnDisk with its size, time, version and SHA-256, or exists false; a file that cannot be read gets an error.</summary>
	private static JsonObject ListFiles(string gameRoot)
	{
		JsonArray files = new JsonArray();
		foreach (string name in FilesOnDisk)
		{
			string path = Path.Combine(gameRoot, name.Replace('/', Path.DirectorySeparatorChar));
			FileInfo info = new FileInfo(path);
			JsonObject file = new JsonObject
			{
				["file"] = name,
				["exists"] = info.Exists
			};
			if (info.Exists)
			{
				file["bytes"] = info.Length;
				file["modifiedUtc"] = info.LastWriteTimeUtc;
				try
				{
					file["fileVersion"] = FileVersionInfo.GetVersionInfo(path).FileVersion;
					using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
					using SHA256 sha = SHA256.Create();
					file["sha256"] = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					file["error"] = ex.Message;
				}
			}
			files.Add(file);
		}
		return new JsonObject
		{
			["schema"] = "fgoac-files-on-disk-v1",
			["filesOnDisk"] = files
		};
	}

	private static string Manifest(Facts facts, List<string> inside, List<string> leftOut, List<string> notFound)
	{
		string exit = facts.LastExit switch
		{
			null => "none, no game was started since the launcher opened",
			0 => "0, the launch script finished normally",
			int code => $"{code} - {StartupDiagnostics.Explain(code)}"
		};
		StringBuilder text = new StringBuilder();
		text.Append("FGOAC report, saved ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append("\r\n\r\n");
		text.Append("Launcher version: ").Append(facts.Version).Append("\r\n");
		text.Append("Mode: ").Append(facts.Mode).Append("\r\n");
		text.Append("Status: ").Append(facts.Status).Append("\r\n");
		text.Append("Last exit: ").Append(exit).Append("\r\n\r\n");
		text.Append("The card and the recovery key are replaced by <redacted> in every text file here, and so are the community server's address and certificate fingerprint. The card file and the DEVICE folder are never included. Nothing was uploaded: this zip stays where it was saved until you send it.\r\n\r\n");
		text.Append("Inside:\r\n");
		foreach (string line in inside.Prepend("manifest.txt (this file)"))
		{
			text.Append("  ").Append(line).Append("\r\n");
		}
		if (leftOut.Count > 0)
		{
			text.Append("\r\nLeft out:\r\n");
			foreach (string line in leftOut)
			{
				text.Append("  ").Append(line).Append("\r\n");
			}
		}
		if (notFound.Count > 0)
		{
			text.Append("\r\nNot on this PC: ").Append(string.Join(", ", notFound)).Append("\r\n");
		}
		return text.ToString();
	}

	/// <summary>The settings file with the community server's address and fingerprint removed, and those two values.</summary>
	private static (string Text, string[] Community) ReadSettings(string path)
	{
		if (!File.Exists(path))
		{
			return ("", Array.Empty<string>());
		}
		string raw = File.ReadAllText(path);
		try
		{
			if (JsonNode.Parse(raw) is JsonObject root)
			{
				List<string> community = new List<string>();
				if (root["communityServer"] is JsonObject block)
				{
					foreach (string name in new string[2] { "host", "certificatePin" })
					{
						if (block[name] is JsonValue value && value.TryGetValue(out string? text) && text.Trim().Length > 0)
						{
							community.Add(text.Trim());
							block[name] = Redaction.Mark;
						}
					}
				}
				return (root.ToJsonString(new JsonSerializerOptions
				{
					WriteIndented = true,
					Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
				}), community.ToArray());
			}
		}
		catch (JsonException)
		{
		}
		return (raw, Array.Empty<string>());
	}

	/// <summary>The file's text from its last megabyte, starting at a whole line, and whether anything before it was cut.</summary>
	private static (string Text, bool Cut) ReadTail(string path)
	{
		using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		byte[] head = new byte[3];
		int read = stream.Read(head, 0, 3);
		(Encoding encoding, int preamble) = (read >= 2 && head[0] == 0xFF && head[1] == 0xFE) ? (Encoding.Unicode, 2)
			: (read >= 2 && head[0] == 0xFE && head[1] == 0xFF) ? (Encoding.BigEndianUnicode, 2)
			: (read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) ? (Encoding.UTF8, 3)
			: (Encoding.UTF8, 0);
		long start = Math.Max(preamble, stream.Length - TextTailBytes);
		if (preamble == 2 && (start - preamble) % 2 != 0)
		{
			start++;
		}
		stream.Seek(start, SeekOrigin.Begin);
		using StreamReader reader = new StreamReader(stream, encoding, false);
		string text = reader.ReadToEnd().Replace("\0", "");
		bool cut = start > preamble;
		if (cut)
		{
			text = text.Substring(text.IndexOf('\n') + 1);
		}
		return (text, cut);
	}

	private static string? Newest(string folder, string pattern, out int others)
	{
		others = 0;
		if (!Directory.Exists(folder))
		{
			return null;
		}
		List<FileInfo> files = new DirectoryInfo(folder).EnumerateFiles(pattern).OrderByDescending((FileInfo file) => file.LastWriteTimeUtc).ToList();
		others = Math.Max(0, files.Count - 1);
		return files.FirstOrDefault()?.FullName;
	}

	private static void WriteEntry(ZipArchive zip, string entry, byte[] bytes)
	{
		using Stream stream = zip.CreateEntry(entry, CompressionLevel.Optimal).Open();
		stream.Write(bytes, 0, bytes.Length);
	}

	private static string Megabytes(long bytes)
	{
		return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
	}
}
