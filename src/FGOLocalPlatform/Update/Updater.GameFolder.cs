using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace FGOLocalPlatform;

internal static partial class Updater
{
	private const string StopFirst = "Stop the game and the local server, then install the update.";

	// Mirrors $ProtectedPrefixes and $ProtectedFiles in patch\Apply-EN-Patch.ps1.
	private static readonly string[] ProtectedPrefixes = { "Server\\state\\", "Server\\data\\", "DEVICE\\", "AMFS\\", "GameData\\", "_en-patch-backup\\", "_update-backup\\", "backup\\", "App\\deck-loadouts\\", "Server\\artemis\\config\\summon-presets\\", "App\\1_MOD\\" };

	private static readonly string[] ProtectedFiles = { "App\\fgo-launcher.json", "App\\fgo-launcher.json.bak", "App\\deck.json", "App\\deck.json.bak" };

	private const uint QueryLimitedInformation = 0x1000;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr handle);

	/// <summary>True for a path, relative to the game folder, that no release entry may write.</summary>
	private static bool IsProtected(string relative)
	{
		return ProtectedPrefixes.Any((string prefix) => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) || ProtectedFiles.Any((string file) => relative.Equals(file, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// The first program running from App\ or Server\ of the game folder, which is the game or the
	/// local server, relative to the game folder; null when there is none.
	/// </summary>
	private static string? RunningFromGameFolder(string installRoot)
	{
		string root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		foreach (Process process in Process.GetProcesses())
		{
			using (process)
			{
				if (process.Id == Environment.ProcessId)
				{
					continue;
				}
				string? image = ImagePath(process.Id);
				if (image == null || !image.StartsWith(root, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string relative = image.Substring(root.Length);
				if (relative.StartsWith("App\\", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("Server\\", StringComparison.OrdinalIgnoreCase))
				{
					return relative;
				}
			}
		}
		return null;
	}

	/// <summary>The program file of a process, readable for elevated processes too; null when the process is gone or closed to this one.</summary>
	private static string? ImagePath(int processId)
	{
		IntPtr handle = OpenProcess(QueryLimitedInformation, inheritHandle: false, processId);
		if (handle == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			StringBuilder name = new StringBuilder(1024);
			int size = name.Capacity;
			return QueryFullProcessImageName(handle, 0, name, ref size) ? name.ToString(0, size) : null;
		}
		finally
		{
			CloseHandle(handle);
		}
	}
}
