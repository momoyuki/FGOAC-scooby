using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FGOLocalPlatform;

public static class ModInstall
{
	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(ModStorage.Json)
	{
		PropertyNameCaseInsensitive = true
	};

	public static string DirectoryName(string component)
	{
		return component switch
		{
			"audio" => "BGM", 
			"dialogue" => "Dialogue", 
			"master" => "Master", 
			_ => throw new InvalidDataException("The package holds a kind of content this launcher does not know."), 
		};
	}

	public static ModPackageInfo Read(string file)
	{
		using ZipArchive zipArchive = ZipFile.OpenRead(file);
		using Stream utf8Json = (zipArchive.GetEntry("mod-package.json") ?? throw new InvalidDataException("This file is not a mod package.")).Open();
		ModPackageInfo modPackageInfo = JsonSerializer.Deserialize<ModPackageInfo>(utf8Json, Json) ?? throw new InvalidDataException("This file is not a mod package.");
		if (modPackageInfo.SchemaVersion != 3 || modPackageInfo.GameVersion != "11.51")
		{
			throw new InvalidDataException("This package is made for another game version.");
		}
		DirectoryName(modPackageInfo.Component);
		return modPackageInfo;
	}

	public static ModPackageInfo? Current(string root, string component)
	{
		string path = Path.Combine(root, DirectoryName(component), "installed-package.json");
		if (!File.Exists(path))
		{
			return null;
		}
		return JsonSerializer.Deserialize<ModPackageInfo>(File.ReadAllText(path), Json);
	}

	public static bool Enabled(string root, string component)
	{
		string path = Path.Combine(root, DirectoryName(component), "runtime.ini");
		string text = ((component == "audio") ? "bgm" : component);
		if (!File.Exists(path))
		{
			return false;
		}
		return Regex.IsMatch(Regex.Match(File.ReadAllText(path), "(?ms)^\\[" + text + "\\]\\r?\\n(.*?)(?=^\\[|\\z)").Groups[1].Value, "(?m)^enabled=1\\r?$");
	}

	public static string Install(string root, string zipPath, bool load)
	{
		ModPackageInfo modPackageInfo = Read(zipPath);
		string text = DirectoryName(modPackageInfo.Component);
		string text2 = "App/1_MOD/" + text + "/";
		string text3 = Guid.NewGuid().ToString("N");
		string text4 = Path.Combine(root, "InstallTemp", text3);
		string text5 = ModLibrary.PackageRoot(root, text3);
		string text6 = Path.Combine(text4, text);
		bool flag = false;
		try
		{
			Directory.CreateDirectory(text6);
			using ZipArchive zipArchive = ZipFile.OpenRead(zipPath);
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ZipArchiveEntry entry in zipArchive.Entries)
			{
				string fullName = entry.FullName;
				if ((fullName == "mod-package.json" || fullName == "README.txt") ? true : false)
				{
					continue;
				}
				if (!fullName.StartsWith(text2, StringComparison.Ordinal) || fullName.Contains('\\'))
				{
					throw new InvalidDataException("The package holds files of another kind, or a file name that is not allowed.");
				}
				string text7 = fullName.Substring(text2.Length);
				if (text7.Length != 0 || !fullName.EndsWith('/'))
				{
					if (text7.Split('/').Any((string s) => ((s == "." || s == "..") ? true : false) || s.Contains(':')))
					{
						throw new InvalidDataException("The package holds files of another kind, or a file name that is not allowed.");
					}
					string fullPath = Path.GetFullPath(Path.Combine(text6, text7));
					if (!fullPath.StartsWith(Path.GetFullPath(text6) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !hashSet.Add(fullPath))
					{
						throw new InvalidDataException("The package holds files of another kind, or a file name that is not allowed.");
					}
					if (fullName.EndsWith('/'))
					{
						Directory.CreateDirectory(fullPath);
						continue;
					}
					Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
					entry.ExtractToFile(fullPath);
				}
			}
			if (!File.Exists(Path.Combine(text6, "settings.json")) || !File.Exists(Path.Combine(text6, "runtime.ini")))
			{
				throw new InvalidDataException("The package is incomplete: its settings are missing.");
			}
			ModStorage.AtomicWrite(Path.Combine(text6, "installed-package.json"), JsonSerializer.Serialize(modPackageInfo, ModStorage.Json));
			if (modPackageInfo.Component == "master")
			{
				ModMasterSettings settings = ModMaster.Load(text4);
				ModMaster.Build(Path.GetFullPath(Path.Combine(root, "..")), text4, settings);
				ModMaster.Validate(text4);
			}
			else
			{
				if (modPackageInfo.Component == "dialogue")
				{
					ModStorage.DialogueIndex dialogueIndex = JsonSerializer.Deserialize<ModStorage.DialogueIndex>(File.ReadAllText(Path.Combine(text6, "settings.json")), ModStorage.Json) ?? throw new InvalidDataException("The package's dialogue list is empty.");
					foreach (string item in dialogueIndex.Events.Concat(dialogueIndex.Quests ?? new List<string>()))
					{
						string fullPath2 = Path.GetFullPath(Path.Combine(text6, item));
						if (!fullPath2.StartsWith(Path.GetFullPath(text6) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath2))
						{
							throw new InvalidDataException("The package's dialogue names a file the package does not hold.");
						}
					}
				}
				ModSettings modSettings = ModStorage.Load(text4);
				foreach (ModDialogue dialogue in modSettings.Dialogues)
				{
					if (dialogue.ContinueScreen && dialogue.ContinueCustomImage && !File.Exists(ModContinueImage.PathFor(text4, dialogue.Id)))
					{
						throw new InvalidDataException("The package is missing a tap-to-continue image.");
					}
				}
				foreach (ModTrack track in modSettings.Tracks)
				{
					if (!File.Exists(ModStorage.TrackPath(text4, track.File)))
					{
						throw new InvalidDataException("The package is missing a music file.");
					}
				}
				if (modPackageInfo.Component == "dialogue" && (!Regex.IsMatch(modSettings.DialogueFolder, "\\ADialogue/(Events/shared-|generated/dialogue-)[a-f0-9]{32}\\z") || !Directory.Exists(Path.Combine(text4, modSettings.DialogueFolder))))
				{
					throw new InvalidDataException("The package is missing its built dialogue files.");
				}
				foreach (ModDialogue dialogue2 in modSettings.Dialogues)
				{
					if (dialogue2.ContinueScreen && dialogue2.ContinueCustomImage && !File.Exists(Path.Combine(text4, ModDialogueCompiler.EventResource(modSettings.DialogueFolder, dialogue2.Id), "continue.bmp")))
					{
						throw new InvalidDataException("The package is missing a built tap-to-continue image.");
					}
				}
			}
			Directory.CreateDirectory(Path.GetDirectoryName(text5));
			Directory.Move(text4, text5);
			if (load)
			{
				ModLibrary.Select(root, modPackageInfo.Component, text3);
			}
			flag = true;
			return text3;
		}
		catch
		{
			if (!flag && Directory.Exists(text5))
			{
				Directory.Delete(text5, recursive: true);
			}
			throw;
		}
		finally
		{
			if (Directory.Exists(text4))
			{
				Directory.Delete(text4, recursive: true);
			}
		}
	}
}
