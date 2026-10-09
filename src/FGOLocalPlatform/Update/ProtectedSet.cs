using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// The player's files that no release, update or start of the launcher may lose, and the update
/// backup of them: before the first start of a version, the files of the set but the local database
/// are copied to backup\pre-&lt;version&gt;\files, with backup\pre-&lt;version&gt;\manifest.json listing each
/// one's path, size and SHA-256 and whether Apply-EN-Patch.ps1 -Rollback puts it back.
/// </summary>
internal static class ProtectedSet
{
	/// <summary>The set, relative to the game folder; an entry ending in a separator is a folder and everything under it.</summary>
	private static readonly string[] Entries = { "Server\\data\\", "Server\\state\\", "DEVICE\\aime.txt", "DEVICE\\community\\", "DEVICE\\print\\players\\", "App\\deck.json", "App\\deck-loadouts\\", "App\\fgo-launcher.json", "DEVICE\\runtime\\", "App\\1_MOD\\" };

	/// <summary>Folders under a set folder that Cloud23333's update ships, so they are not the player's files.</summary>
	private static readonly string[] Shipped = { "App\\1_MOD\\Dialogue\\Catalog\\", "App\\1_MOD\\Tools\\" };

	/// <summary>The launcher's own files in the set: the deck file, the loadouts, the settings file and the runtime ini.</summary>
	private static readonly string[] Own = { "App\\deck.json", "App\\deck-loadouts\\", "App\\fgo-launcher.json", "DEVICE\\runtime\\" };

	private const string Database = "Server\\data\\";

	/// <summary>Every file of the set in the game folder, relative to it, in ordinal order; the local database only with <paramref name="withDatabase" />.</summary>
	public static List<string> Files(string gameFolder, bool withDatabase)
	{
		List<string> files = new List<string>();
		foreach (string entry in Entries)
		{
			if (!withDatabase && entry == Database)
			{
				continue;
			}
			string path = Path.Combine(gameFolder, entry);
			if (entry.EndsWith('\\'))
			{
				if (Directory.Exists(path))
				{
					files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Select((string file) => Path.GetRelativePath(gameFolder, file)).Where((string relative) => !Shipped.Any((string folder) => relative.StartsWith(folder, StringComparison.OrdinalIgnoreCase))));
				}
			}
			else if (File.Exists(path))
			{
				files.Add(entry);
			}
		}
		files.Sort(StringComparer.OrdinalIgnoreCase);
		return files;
	}

	/// <summary>True for a file of the set that the launcher itself writes.</summary>
	public static bool IsOwnFile(string relative)
	{
		return Own.Any((string own) => own.EndsWith('\\') ? relative.StartsWith(own, StringComparison.OrdinalIgnoreCase) : relative.Equals(own, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Takes the update backup of <paramref name="version" /> and returns its folder; null when the
	/// folder is already there, which makes it once per version, or when <paramref name="gameFolder" />
	/// holds no App\fgo-launcher.json. The copy is made under a .partial name and renamed when complete,
	/// so a start that stops half way leaves no backup and the next start takes it again.
	/// </summary>
	public static string? TakeBackup(string gameFolder, string version)
	{
		string folder = Path.Combine(gameFolder, "backup", "pre-" + version);
		if (Directory.Exists(folder) || !File.Exists(Path.Combine(gameFolder, "App", "fgo-launcher.json")))
		{
			return null;
		}
		string partial = folder + ".partial";
		if (Directory.Exists(partial))
		{
			Directory.Delete(partial, recursive: true);
		}
		JsonArray files = new JsonArray();
		foreach (string relative in Files(gameFolder, withDatabase: false))
		{
			string copy = Path.Combine(partial, "files", relative);
			Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
			File.Copy(Path.Combine(gameFolder, relative), copy);
			files.Add(new JsonObject
			{
				["path"] = relative,
				["size"] = new FileInfo(copy).Length,
				["sha256"] = Updater.HashFile(copy),
				["restore"] = IsOwnFile(relative) && !relative.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
			});
		}
		JsonObject manifest = new JsonObject
		{
			["version"] = version,
			["takenUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
			["files"] = files
		};
		Directory.CreateDirectory(partial);
		File.WriteAllText(Path.Combine(partial, "manifest.json"), manifest.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}) + Environment.NewLine);
		Directory.Move(partial, folder);
		return folder;
	}
}
