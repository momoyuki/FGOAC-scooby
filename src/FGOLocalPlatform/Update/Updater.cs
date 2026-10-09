using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

/// <summary>
/// Checks GitHub Releases for a newer build and installs it the way a player would by hand: the
/// release zip is unpacked into the game folder and the Apply-EN-Patch.ps1 that came with it is
/// run. Every failure is silent on screen during the startup check and written to
/// logs\update.log; the manual check on the About page reports what happened in one sentence.
/// </summary>
internal static partial class Updater
{
	internal sealed record Release(string Version, string Tag, string ZipUrl, string ZipName, string SigUrl);

	private const string ZipPrefix = "FGOAC-scooby-v";

	private const string Unverified = "The update could not be verified as signed by FGOAC scooby, so it was not installed. Download it from the releases page, or wait for a signed release.";

	private static readonly HttpClient Client = CreateClient();

	/// <summary>How long the release downloads wait for the next bytes.</summary>
	private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15.0);

	private static HttpClient CreateClient()
	{
		// No client-wide timeout: it would also cut off the download of a release zip that takes
		// minutes. The small API calls carry their own ten-second token.
		HttpClient client = new HttpClient
		{
			Timeout = Timeout.InfiniteTimeSpan
		};
		// GitHub rejects an API request that does not name the program making it.
		client.DefaultRequestHeaders.Add("User-Agent", "FGOAC-scooby/" + UpdateSettings.Version);
		client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
		return client;
	}

	/// <summary>
	/// The newest release when it is newer than the running build, otherwise null. Throws only for
	/// the manual check to report; the startup check swallows it.
	/// </summary>
	public static async Task<Release> CheckAsync()
	{
		Log("Checking " + UpdateSettings.LatestReleaseApiUrl);
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));
		string json = await Client.GetStringAsync(UpdateSettings.LatestReleaseApiUrl, timeout.Token);
		return ParseRelease(json);
	}

	/// <summary>
	/// The release a releases/latest answer describes when it is newer than the running build,
	/// otherwise null. SigUrl is null when the release has no signature for its zip.
	/// </summary>
	public static Release ParseRelease(string json)
	{
		JsonNode node = JsonNode.Parse(json);
		string tag = (string)node?["tag_name"] ?? "";
		Version latest = ParseVersion(tag);
		if (latest == null)
		{
			Log("The latest release is tagged " + tag + ", which is not a version number.");
			return null;
		}
		Version running = ParseVersion(UpdateSettings.Version);
		if (latest <= running)
		{
			Log($"The latest release is {tag} and this build is {UpdateSettings.Version}, so there is nothing to install.");
			return null;
		}
		JsonArray assets = node["assets"] as JsonArray ?? new JsonArray();
		// The zip is taken only under the name its tag gives it; the name is never a path.
		string zipName = ZipPrefix + FormatVersion(latest) + ".zip";
		string zipUrl = null;
		string sigUrl = null;
		foreach (JsonNode asset in assets)
		{
			string name = (string)asset?["name"] ?? "";
			if (string.Equals(name, zipName, StringComparison.OrdinalIgnoreCase))
			{
				zipUrl = (string)asset["browser_download_url"];
			}
			else if (string.Equals(name, zipName + ".sig", StringComparison.OrdinalIgnoreCase))
			{
				sigUrl = (string)asset["browser_download_url"];
			}
		}
		if (zipUrl == null)
		{
			Log("Release " + tag + " has no " + zipName + ", so it cannot be installed from here.");
			return null;
		}
		if (sigUrl == null)
		{
			Log("Release " + tag + " has no " + zipName + ".sig, so it will be refused.");
		}
		Log("Release " + tag + " is newer than " + UpdateSettings.Version + ": " + zipName);
		return new Release(FormatVersion(latest), tag, zipUrl, zipName, sigUrl);
	}

	/// <summary>
	/// Refuses while the game or the local server runs from the game folder, then downloads the
	/// release, refuses it unless its .sig verifies under one of
	/// UpdateSettings.ReleaseKeys, unzips it into the game folder and runs the Apply-EN-Patch.ps1
	/// it brought. Returns true when the patch finished; <paramref name="report" /> gets one
	/// sentence per step.
	/// </summary>
	public static async Task<bool> InstallAsync(Release release, string installRoot, Action<string> report, CancellationToken cancellation)
	{
		string staging = Path.Combine(Path.GetTempPath(), "FGOAC-scooby-update-" + Guid.NewGuid().ToString("N"));
		string archive = Path.Combine(staging, "update.zip");
		try
		{
			string? running = RunningFromGameFolder(installRoot);
			if (running != null)
			{
				Log("Refused " + release.ZipName + ": " + running + " is running from the game folder.");
				report(StopFirst);
				return false;
			}
			if (release.SigUrl == null)
			{
				Log("Refused " + release.ZipName + ": the release has no signature.");
				report(Unverified);
				return false;
			}
			StagingSecurity().CreateDirectory(staging);
			// A link here would send the elevated write somewhere else.
			if ((File.GetAttributes(staging) & FileAttributes.ReparsePoint) != 0)
			{
				throw new IOException("The update folder " + staging + " is a link, so nothing was downloaded into it");
			}

			report("Downloading " + release.ZipName + "...");
			Log("Downloading " + release.ZipUrl);
			// One handle, shared with no other process, from the download to the end of the
			// extraction: the bytes verified are the bytes unpacked. The file goes with the handle.
			using (FileStream file = new FileStream(archive, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose))
			{
				await DownloadAsync(release.ZipUrl, file, cancellation);

				report("Checking the download...");
				file.Position = 0;
				string actual = HashStream(file);
				string signature = await ReadSignatureAsync(release.SigUrl, cancellation);
				if (!IsSigned(release.Version, actual, signature))
				{
					Log($"Refused {release.ZipName}: its signature does not verify for version {release.Version} and SHA-256 {actual}.");
					report(Unverified);
					return false;
				}
				Log($"Signature verified for version {release.Version} and SHA-256 {actual}.");

				report("Unpacking the update into the game folder...");
				file.Position = 0;
				ExtractIntoGameFolder(file, installRoot);
			}
			string script = Path.Combine(installRoot, "Apply-EN-Patch.ps1");
			if (!File.Exists(script))
			{
				Log("No Apply-EN-Patch.ps1 in " + release.ZipName);
				report("The update package has no installer in it, so nothing was installed. Download the release from GitHub and unzip it into the game folder yourself.");
				return false;
			}

			report("Installing " + release.Version + "...");
			int exitCode = await RunPatchAsync(script, installRoot, installRoot, report, cancellation);
			if (exitCode != 0)
			{
				Log("Apply-EN-Patch.ps1 exited with " + exitCode);
				report("The update did not install (the installer stopped with code " + exitCode + "). The log panel and logs\\update.log say what happened.");
				return false;
			}
			Log("Version " + release.Version + " installed.");
			return true;
		}
		catch (Exception ex)
		{
			Log("Install failed: " + ex);
			report("The update could not be installed: " + ex.Message + ". Try again, or download the release from GitHub yourself.");
			return false;
		}
		finally
		{
			try
			{
				// update.zip went with its handle: this non-recursive delete removes only the folder itself.
				if (Directory.Exists(staging) && (File.GetAttributes(staging) & FileAttributes.ReparsePoint) == 0)
				{
					Directory.Delete(staging);
				}
			}
			catch (Exception ex)
			{
				Log("Could not remove " + staging + ": " + ex.Message);
			}
		}
	}

	/// <summary>
	/// Unzips the release into the game folder, the layout a player gets by unzipping it by hand,
	/// so the folder keeps the installer, the manifest and the payload of the version it is on
	/// and the next start finds nothing to redo. The running launcher cannot be overwritten, so
	/// its new build lands beside it as .new and is swapped in when the launcher closes.
	/// manifest.json goes last: until it is there the folder still describes the previous
	/// package, so an extraction that stops half way is not taken for a complete one. Every
	/// entry is checked before the first is written, so a refused package writes nothing.
	/// </summary>
	private static void ExtractIntoGameFolder(Stream archive, string installRoot)
	{
		string root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		using ZipArchive zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
		List<(ZipArchiveEntry Entry, string Target)> files = new List<(ZipArchiveEntry, string)>();
		foreach (ZipArchiveEntry entry in zip.Entries.OrderBy((ZipArchiveEntry e) => string.Equals(e.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase)))
		{
			if (entry.Name.Length == 0)
			{
				continue;
			}
			string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
			if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException("The update lists a path outside the game folder: " + entry.FullName);
			}
			string relative = target.Substring(root.Length);
			if (IsProtected(relative))
			{
				throw new IOException("The update lists a file it is not allowed to write: " + entry.FullName);
			}
			if (string.Equals(entry.FullName, "FGOAC scooby.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
			{
				target += ".new";
			}
			files.Add((entry, target));
		}
		foreach ((ZipArchiveEntry entry, string target) in files)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(target));
			entry.ExtractToFile(target, overwrite: true);
		}
	}

	private static async Task<int> RunPatchAsync(string script, string packageRoot, string installRoot, Action<string> report, CancellationToken cancellation)
	{
		ProcessStartInfo start = PowerShellHost.CreateStartInfo(installRoot, redirectOutput: true, new string[]
		{
			"-File", script, "-InstallRoot", installRoot, "-PackageRoot", packageRoot, "-NonInteractive",
			"-IgnoreProcessId", Environment.ProcessId.ToString()
		});

		using Process process = Process.Start(start) ?? throw new IOException("PowerShell could not be started.");
		process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
			{
				Log("  " + e.Data);
				report(e.Data.Trim());
			}
		};
		process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
			{
				Log("  ! " + e.Data);
			}
		};
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		await process.WaitForExitAsync(cancellation);
		return process.ExitCode;
	}

	/// <summary>
	/// True when the installer left the new launcher beside the running one because a program
	/// cannot overwrite its own file.
	/// </summary>
	public static bool HasStagedLauncher(out string stagedPath)
	{
		stagedPath = Environment.ProcessPath + ".new";
		return File.Exists(stagedPath);
	}

	/// <summary>
	/// Writes and starts the one-line script that waits for this process to close, puts the staged
	/// launcher in its place and starts it. The caller closes the window straight afterwards.
	/// </summary>
	public static void SwapAndRestart(string stagedPath)
	{
		string launcher = Environment.ProcessPath;
		string swap = Path.Combine(Path.GetTempPath(), "update-swap.cmd");
		// The move is retried for a while: the virus scanner can still be reading the staged file, or
		// the closing process may not have released its own file yet.
		string[] lines = new string[13]
		{
			"@echo off",
			"setlocal",
			"set tries=0",
			":wait",
			"tasklist /fi \"PID eq " + Environment.ProcessId + "\" | find \"" + Environment.ProcessId + "\" >nul && (timeout /t 1 /nobreak >nul & goto wait)",
			":move",
			"move /y \"" + stagedPath + "\" \"" + launcher + "\" >nul 2>&1 && goto run",
			"set /a tries+=1",
			"if %tries% lss 60 (timeout /t 1 /nobreak >nul & goto move)",
			":run",
			"start \"\" \"" + launcher + "\"",
			"del \"%~f0\"",
			""
		};
		File.WriteAllLines(swap, lines, Encoding.ASCII);
		Log("Swapping in " + stagedPath + " through " + swap);
		Process.Start(new ProcessStartInfo(swap)
		{
			UseShellExecute = true,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden
		});
	}

	public static void Log(string message)
	{
		try
		{
			Directory.CreateDirectory(GamePaths.LogsRoot);
			File.AppendAllText(Path.Combine(GamePaths.LogsRoot, "update.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + Redaction.Apply(message) + Environment.NewLine, Encoding.UTF8);
		}
		catch (Exception)
		{
		}
	}

	private static Version ParseVersion(string text)
	{
		string trimmed = (text ?? "").Trim();
		if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
		{
			trimmed = trimmed.Substring(1);
		}
		List<int> parts = new List<int>();
		foreach (string part in trimmed.Split('.'))
		{
			if (!int.TryParse(part, out var value) || value < 0)
			{
				break;
			}
			parts.Add(value);
		}
		if (parts.Count == 0)
		{
			return null;
		}
		while (parts.Count < 3)
		{
			parts.Add(0);
		}
		return new Version(parts[0], parts[1], parts[2]);
	}

	private static string FormatVersion(Version version)
	{
		return $"{version.Major}.{version.Minor}.{version.Build}";
	}
}
