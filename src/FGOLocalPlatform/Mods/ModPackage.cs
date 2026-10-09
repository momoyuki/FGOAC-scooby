using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace FGOLocalPlatform;

public static class ModPackage
{
	public static void Export(string gameRoot, string root, ModSettings current, string destination, bool audio)
	{
		ModSettings modSettings = JsonSerializer.Deserialize<ModSettings>(JsonSerializer.Serialize(current, ModStorage.Json), ModStorage.Json);
		for (int i = 0; i < modSettings.Dialogues.Count; i++)
		{
			modSettings.Dialogues[i].QuestName = current.Dialogues[i].QuestName;
		}
		if (audio)
		{
			modSettings.DialogueEnabled = false;
			modSettings.DialogueFolder = "";
			modSettings.Dialogues.Clear();
			modSettings.PortraitLibraryPath = "";
		}
		else
		{
			modSettings.BgmEnabled = false;
			modSettings.Tracks.Clear();
		}
		string text = Path.Combine(Path.GetTempPath(), "fgo-mod-" + Guid.NewGuid().ToString("N"));
		string text2 = Path.Combine(text, "App", "1_MOD");
		string text3 = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			Directory.CreateDirectory(text2);
			if (!audio)
			{
				foreach (ModDialogue dialogue in modSettings.Dialogues)
				{
					string text4 = ModContinueImage.PathFor(root, dialogue.Id);
					if (File.Exists(text4))
					{
						Copy(text4, ModContinueImage.PathFor(text2, dialogue.Id));
					}
				}
				ModStorage.AtomicWrite(ModPortraits.CatalogFile(text2), JsonSerializer.Serialize(ModDialogueCompiler.NativePortraits(gameRoot, root), ModStorage.Json));
				HashSet<string> used = (from l in modSettings.Dialogues.SelectMany((ModDialogue d) => d.Lines)
					select l.Portrait).ToHashSet<string>(StringComparer.Ordinal);
				ModStorage.AtomicWrite(ModPortraits.CustomIndex(text2), JsonSerializer.Serialize((from p in ModPortraits.Custom(root, current.PortraitLibraryPath)
					where used.Contains(p.Reference)
					select p).ToArray(), ModStorage.Json));
				foreach (string item in (from l in modSettings.Dialogues.SelectMany((ModDialogue d) => d.Lines)
					select l.Portrait).Distinct())
				{
					string text5 = ModPortraits.PreviewPath(root, item, current.PortraitLibraryPath);
					if (ModPortraits.IsCustom(item) && File.Exists(text5))
					{
						Copy(text5, Path.Combine(ModPortraits.CustomDirectory(text2), item + ".png"));
						continue;
					}
					string text6 = ModPortraits.PreviewPath(root, item);
					if (File.Exists(text6))
					{
						Copy(text6, Path.Combine(text2, "Dialogue", "Catalog", "Portraits", item + ".png"));
					}
				}
				modSettings.PortraitLibraryPath = "";
				ModDialogueCompiler.Build(gameRoot, text2, modSettings);
			}
			else
			{
				foreach (ModTrack track in modSettings.Tracks)
				{
					Copy(ModStorage.TrackPath(root, track.File), ModStorage.TrackPath(text2, track.File));
				}
			}
			ModStorage.SaveComponent(text2, modSettings, audio);
			ModStorage.AtomicWrite(Path.Combine(text, "mod-package.json"), JsonSerializer.Serialize(new
			{
				schemaVersion = 3,
				buildId = Guid.NewGuid().ToString("N"),
				createdUtc = DateTimeOffset.UtcNow,
				component = (audio ? "audio" : "dialogue"),
				gameVersion = "11.51",
				launcherVersion = "1.10",
				Title = modSettings.Title,
				Author = modSettings.Author,
				Version = modSettings.Version,
				Description = modSettings.Description,
				events = modSettings.Dialogues.Count,
				tracks = modSettings.Tracks.Count
			}, ModStorage.Json));
			ModStorage.AtomicWrite(Path.Combine(text, "README.txt"), "Import it on the launcher's Mods page and tick Use. The package is kept in App/1_MOD/Packages and leaves your own edits as they are." + Environment.NewLine + "The ZIP can be deleted once it is imported. Restart the game after changing what is used." + Environment.NewLine + modSettings.Description);
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
			ZipFile.CreateFromDirectory(text, text3, CompressionLevel.Optimal, includeBaseDirectory: false);
			File.Move(text3, destination, overwrite: true);
		}
		finally
		{
			if (File.Exists(text3))
			{
				File.Delete(text3);
			}
			if (Directory.Exists(text))
			{
				Directory.Delete(text, recursive: true);
			}
		}
	}

	private static void Copy(string source, string destination)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(destination));
		File.Copy(source, destination, overwrite: true);
	}
}
