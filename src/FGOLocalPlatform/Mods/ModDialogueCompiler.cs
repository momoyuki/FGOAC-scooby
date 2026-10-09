using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hanhua;

namespace FGOLocalPlatform;

public static class ModDialogueCompiler
{
	public static string[] Portraits(string gameRoot, string? root = null, string? library = null)
	{
		if (root == null)
		{
			root = Path.Combine(gameRoot, "1_MOD");
		}
		return NativePortraits(gameRoot, root).Concat(from p in ModPortraits.Custom(root, library)
			select p.Reference).ToArray();
	}

	public static string[] NativePortraits(string gameRoot, string? root = null, string? library = null)
	{
		if (root == null)
		{
			root = Path.Combine(gameRoot, "1_MOD");
		}
		string path = ModPortraits.CatalogFile(root);
		if (!File.Exists(path))
		{
			path = Path.Combine(root, "Dialogue", "portraits.json");
		}
		if (!File.Exists(path))
		{
			path = Path.Combine(root, "portraits.json");
		}
		if (!File.Exists(path))
		{
			return Array.Empty<string>();
		}
		return JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? Array.Empty<string>();
	}

	private static byte[] ReadEntry(string file, out Archive archive, out ArchiveEntry entry)
	{
		archive = new Archive(File.ReadAllBytes(file));
		entry = archive.Entries.Single();
		return archive.Read(entry);
	}

	public static void Build(string gameRoot, string root, ModSettings settings)
	{
		if (settings.Dialogues.Any((ModDialogue modDialogue) => !Regex.IsMatch(modDialogue.Id, "\\A[a-f0-9]{32}\\z")) || settings.Dialogues.Select((ModDialogue modDialogue) => modDialogue.Id).Distinct<string>(StringComparer.Ordinal).Count() != settings.Dialogues.Count)
		{
			throw new InvalidDataException("Invalid event ID.");
		}
		ModPortraits.MigrateLegacy(gameRoot, root, settings);
		HashSet<string> hashSet = Portraits(gameRoot, root, settings.PortraitLibraryPath).ToHashSet<string>(StringComparer.Ordinal);
		if ((from modDialogue in settings.Dialogues
			group modDialogue by (TargetKind: modDialogue.TargetKind, Quest: modDialogue.Quest, Trigger: modDialogue.Trigger)).Any((IGrouping<(int TargetKind, int Quest, int Trigger), ModDialogue> g) => g.Count() > 1))
		{
			throw new InvalidDataException("A target cannot have duplicate triggers.");
		}
		foreach (ModDialogue d in settings.Dialogues)
		{
			if (d.ContinueScreen && d.ContinueCustomImage && !File.Exists(ModContinueImage.PathFor(root, d.Id)))
			{
				throw new InvalidDataException("Import an image for each trigger that has custom images enabled first.");
			}
			string path = Path.Combine(gameRoot, "rom", "single", $"quest_prop_{d.Quest:000000}.txt");
			if (d.TargetKind == 0 && !File.Exists(path))
			{
				throw new InvalidDataException($"Quest {d.Quest} is not in the game.");
			}
			if (d.TargetKind == 1 && !ModStorage.Banners(gameRoot).Any((ModQuest q) => q.Id == d.Quest))
			{
				throw new InvalidDataException("The event banner is not in the game.");
			}
			int targetKind = d.TargetKind;
			bool flag = ((targetKind < 0 || targetKind > 1) ? true : false);
			if (flag || d.Trigger < 0 || d.Trigger > 4 || (d.TargetKind == 1 && d.Trigger != 0) || (d.TargetKind == 0 && d.Trigger > 0 && d.Trigger < 4 && !Regex.IsMatch(File.ReadAllText(path), "(?m)^battle\\." + d.Trigger + "\\.wave_id=")))
			{
				throw new InvalidDataException($"Quest {d.Quest} has no such wave.");
			}
			if (d.Lines.Count == 0)
			{
				throw new InvalidDataException($"{d} has no lines yet.");
			}
			foreach (ModLine line in d.Lines)
			{
				if ((line.Portrait.Length > 0 && !hashSet.Contains(line.Portrait)) || string.IsNullOrWhiteSpace(line.Text) || string.IsNullOrWhiteSpace(line.Name))
				{
					throw new InvalidDataException($"A line of {d} has no valid portrait, name or text.");
				}
			}
		}
		string text = "Dialogue/Events/shared-" + Guid.NewGuid().ToString("N");
		string text2 = Path.Combine(root, text);
		Directory.CreateDirectory(text2);
		BuildVariant(gameRoot, gameRoot, text2, settings);
		foreach (ModDialogue item in settings.Dialogues.Where((ModDialogue modDialogue) => modDialogue.ContinueScreen && modDialogue.ContinueCustomImage))
		{
			File.Copy(ModContinueImage.PathFor(root, item.Id), Path.Combine(root, EventResource(text, item.Id), "continue.bmp"), overwrite: true);
		}
		ModPortraits.Build(gameRoot, root, text2, settings.Dialogues, settings.PortraitLibraryPath);
		settings.DialogueFolder = text;
	}

	public static string EventResource(string generation, string id)
	{
		return "Dialogue/Events/event-" + id + "/build-" + generation.Substring(generation.Length - 32);
	}

	public static string ScriptFile(string root, ModSettings settings, ModDialogue dialogue)
	{
		return Path.Combine(root, EventResource(settings.DialogueFolder, dialogue.Id), "scripts", $"Story_MOD_{dialogue.TargetKind}_{dialogue.Quest:000000}_{dialogue.Trigger}.json");
	}

	private static void BuildVariant(string gameRoot, string sourceRoot, string output, ModSettings settings)
	{
		string path = Path.Combine("rom", "mst_data", "arms_mst_story_info_setting.farc");
		string text = Path.Combine(sourceRoot, path);
		if (!File.Exists(text))
		{
			text = Path.Combine(gameRoot, path);
		}
		Archive archive;
		ArchiveEntry entry;
		string input = Encoding.UTF8.GetString(ReadEntry(text, out archive, out entry));
		SortedDictionary<int, Dictionary<string, string>> sortedDictionary = new SortedDictionary<int, Dictionary<string, string>>();
		foreach (Match item in Regex.Matches(input, "(?m)^story_info_setting\\.(\\d+)\\.([^=\\r\\n]+)=([^\\r\\n]*)"))
		{
			int key = int.Parse(item.Groups[1].Value);
			if (!sortedDictionary.TryGetValue(key, out var value))
			{
				value = (sortedDictionary[key] = new Dictionary<string, string>());
			}
			value[item.Groups[2].Value] = item.Groups[3].Value;
		}
		int num = ((sortedDictionary.Count != 0) ? (sortedDictionary.Keys.Max() + 1) : 0);
		foreach (ModDialogue d in settings.Dialogues)
		{
			int sequence = d.Trigger;
			string text2 = $"Story_0_{d.Quest:0000}_{sequence}";
			sortedDictionary.FirstOrDefault((KeyValuePair<int, Dictionary<string, string>> k) => k.Value.GetValueOrDefault("quest_id") == d.Quest.ToString() && k.Value.GetValueOrDefault("quest_type") == "0" && k.Value.GetValueOrDefault("disp_seq") == sequence.ToString()).Value?.GetValueOrDefault("talk_scene_name");
			text2 = $"Story_MOD_{d.TargetKind}_{d.Quest:000000}_{sequence}";
			int key2 = num++;
			sortedDictionary[key2] = new Dictionary<string, string>
			{
				["uniq_id"] = key2.ToString(),
				["quest_id"] = d.Quest.ToString(),
				["quest_type"] = (100 + d.TargetKind).ToString(),
				["disp_seq"] = sequence.ToString(),
				["talk_scene_name"] = text2,
				["is_next_boss_appearance"] = "0"
			};
			Archive archive2;
			ArchiveEntry entry2;
			string[] source = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(ReadEntry(Path.Combine(gameRoot, "rom", "talk_list", "Story_0_0457_0.farc"), out archive2, out entry2))[0].Keys.ToArray();
			List<Dictionary<string, string>> list = new List<Dictionary<string, string>>();
			int num2 = 9;
			Dictionary<string, string> dictionary2 = null;
			foreach (ModLine line in d.Lines)
			{
				Dictionary<string, string> dictionary3 = source.ToDictionary((string x) => x, (string x) => "");
				dictionary3["frame"] = num2.ToString();
				dictionary3["text_win_chara"] = line.Portrait;
				dictionary3["chara_name"] = line.Name;
				dictionary3["txt"] = line.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
				dictionary3["win_type"] = "2";
				int trigger = d.Trigger;
				dictionary3["is_use_screen"] = ((trigger >= 1 && trigger <= 3) ? "0" : "1");
				dictionary3["disp_frame"] = "2";
				dictionary3["is_skip_in_effect"] = ((list.Count == 0) ? "" : "TRUE");
				list.Add(dictionary3);
				num2 += 600;
			}
			Dictionary<string, string> dictionary4 = dictionary2 ?? source.ToDictionary((string x) => x, (string x) => "");
			dictionary4["frame"] = num2.ToString();
			dictionary4["is_end"] = "TRUE";
			if (dictionary2 == null)
			{
				int trigger = d.Trigger;
				dictionary4["is_use_screen"] = ((trigger >= 1 && trigger <= 3) ? "0" : "1");
			}
			list.Add(dictionary4);
			byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(list, new JsonSerializerOptions
			{
				Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
			}));
			string path2 = Path.Combine(Path.GetDirectoryName(output), "event-" + d.Id, "build-" + Path.GetFileName(output).Substring(7));
			Directory.CreateDirectory(Path.Combine(path2, "scripts"));
			File.WriteAllBytes(Path.Combine(path2, "scripts", text2 + ".json"), bytes);
		}
		StringBuilder stringBuilder = new StringBuilder(Regex.Replace(Regex.Replace(input, "(?m)^story_info_setting\\.[^\\r\\n]*\\r?\\n?", ""), "(?m)^data_list.length=[^\\r\\n]*", "data_list.length=" + num).TrimEnd() + "\r\n");
		foreach (KeyValuePair<int, Dictionary<string, string>> item2 in sortedDictionary)
		{
			foreach (KeyValuePair<string, string> item3 in item2.Value)
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(23, 3, stringBuilder2);
				handler.AppendLiteral("story_info_setting.");
				handler.AppendFormatted(item2.Key);
				handler.AppendLiteral(".");
				handler.AppendFormatted(item3.Key);
				handler.AppendLiteral("=");
				handler.AppendFormatted(item3.Value);
				handler.AppendLiteral("\r\n");
				stringBuilder2.Append(ref handler);
			}
		}
		WriteArchive(Path.Combine(output, path), archive.Repack(new Dictionary<string, byte[]> { 
		{
			entry.Name,
			Encoding.UTF8.GetBytes(stringBuilder.ToString())
		} }));
	}

	private static void WriteArchive(string path, byte[] bytes)
	{
		Archive archive = new Archive(bytes);
		foreach (ArchiveEntry entry in archive.Entries)
		{
			archive.Read(entry);
		}
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		File.WriteAllBytes(path, bytes);
	}

	public static byte[] SingleArchive(string name, byte[] content)
	{
		return MultiArchive(new Dictionary<string, byte[]> { { name, content } });
	}

	private static byte[] MultiArchive(Dictionary<string, byte[]> members)
	{
		(byte[] Name, int Size, byte[] Data)[] array = members.Select<KeyValuePair<string, byte[]>, (byte[] Name, int Size, byte[] Data)>(delegate(KeyValuePair<string, byte[]> kv)
		{
			using MemoryStream memoryStream = new MemoryStream();
			using (GZipStream gZipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
			{
				gZipStream.Write(kv.Value);
			}
			return (Name: Encoding.UTF8.GetBytes(kv.Key), Size: kv.Value.Length, Data: memoryStream.ToArray());
		}).ToArray();
		int num = 32 + array.Sum(((byte[] Name, int Size, byte[] Data) e) => e.Name.Length + 17);
		int num2 = (num + 15) & -16;
		byte[] array2 = new byte[num2 + array.Sum(((byte[] Name, int Size, byte[] Data) e) => (e.Data.Length + 15) & -16)];
		Encoding.ASCII.GetBytes("FARc").CopyTo(array2, 0);
		Archive.Put(array2, 4, num - 8);
		Archive.Put(array2, 8, 2);
		Archive.Put(array2, 16, 16);
		Archive.Put(array2, 20, 1);
		Archive.Put(array2, 24, array.Length);
		Archive.Put(array2, 28, 16);
		int num3 = 32;
		(byte[], int, byte[])[] array3 = array;
		for (int num4 = 0; num4 < array3.Length; num4++)
		{
			(byte[], int, byte[]) tuple = array3[num4];
			tuple.Item1.CopyTo(array2, num3);
			num3 += tuple.Item1.Length + 1;
			Archive.Put(array2, num3, num2);
			Archive.Put(array2, num3 + 4, tuple.Item3.Length);
			Archive.Put(array2, num3 + 8, tuple.Item2);
			Archive.Put(array2, num3 + 12, 2);
			tuple.Item3.CopyTo(array2, num2);
			num2 += (tuple.Item3.Length + 15) & -16;
			num3 += 16;
		}
		return array2;
	}
}
