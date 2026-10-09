using System;
using System.IO;
using System.Linq;

namespace FGOLocalPlatform;

/// <summary>The game build the English patch is made for: 11.00, before Cloud23333's 1.10 update.</summary>
internal static class GameBuild
{
	private const string Header1100 = "643F4ECD:02034000";

	/// <summary>
	/// Package files installed only on 11.00: its English set, and English copies of Cloud23333's 1.02 server and account
	/// scripts, which his 1.10 update replaces with his own. A name ending in \ is a folder. Mirrors $Game1100Only in
	/// patch\Apply-EN-Patch.ps1.
	/// </summary>
	internal static readonly string[] Only1100Files = new string[6]
	{
		"App\\zh\\", "Server\\Start-FGOLocalServer.ps1", "Server\\Stop-FGOLocalServerWhenIdle.ps1", "Server\\tools\\fgo_account.py",
		"Server\\tools\\fgo_server_config.py", "Server\\artemis\\titles\\fgo\\data\\summon_candidates.json"
	};

	/// <summary>True for a package path that is installed only on 11.00.</summary>
	internal static bool Only1100(string relative)
	{
		return Only1100Files.Any((string name) => name.EndsWith('\\') ? relative.StartsWith(name, StringComparison.OrdinalIgnoreCase) : relative.Equals(name, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>True when update-1.10.json is absent and App\ago.exe has the 11.00 TimeDateStamp and SizeOfImage.</summary>
	internal static bool Is1100(string installRoot)
	{
		return !File.Exists(Path.Combine(installRoot, "update-1.10.json")) && Header(Path.Combine(installRoot, "App", "ago.exe")) == Header1100;
	}

	/// <summary>The exe's TimeDateStamp and SizeOfImage as two hex numbers, "unreadable" when it is missing or not a PE image.</summary>
	internal static string Header(string exePath)
	{
		byte[] header = new byte[4096];
		int read = 0;
		try
		{
			using FileStream stream = File.OpenRead(exePath);
			int count;
			while (read < header.Length && (count = stream.Read(header, read, header.Length - read)) > 0)
			{
				read += count;
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			return "unreadable";
		}
		if (read < 0x40 || header[0] != 'M' || header[1] != 'Z')
		{
			return "unreadable";
		}
		int pe = BitConverter.ToInt32(header, 0x3C);
		if (pe < 0x40 || pe + 24 + 60 > read || BitConverter.ToUInt32(header, pe) != 0x00004550u)
		{
			return "unreadable";
		}
		return BitConverter.ToUInt32(header, pe + 8).ToString("X8") + ":" + BitConverter.ToUInt32(header, pe + 24 + 56).ToString("X8");
	}
}
