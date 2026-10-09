using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hanhua;

namespace FGOLocalPlatform;

public static class ModStorage
{
	public sealed class DialogueIndex
	{
		public string Title { get; set; } = "";

		public string Author { get; set; } = "";

		public string Version { get; set; } = "1.0";

		public string Description { get; set; } = "";

		public bool Enabled { get; set; }

		public string GeneratedFolder { get; set; } = "";

		public string PortraitLibraryPath { get; set; } = "";

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public List<string>? Quests { get; set; }

		public List<string> Events { get; set; } = new List<string>();
	}

	public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private const int BgmSampleRate = 44100;

	public static string Root => Path.Combine(GamePaths.GameRoot, "1_MOD");

	public static List<ModQuest> Banners(string gameRoot, string? language = null)
	{
		string path = Path.Combine(gameRoot, "rom", "single", "quest_top_prop.txt");
		if (!File.Exists(path))
		{
			return new List<ModQuest>();
		}
		if (language == null)
		{
			language = GameLanguage.Folder(GamePaths.GameRoot);
		}
		string path2 = Path.Combine(gameRoot, "Localization", language, "rom", "single", "quest_top_prop.txt");
		Dictionary<int, string> names = (File.Exists(path2) ? Regex.Matches(File.ReadAllText(path2), "(?m)^name(\\d+)=([^\\r\\n]*)").Cast<Match>().ToDictionary((Match m) => int.Parse(m.Groups[1].Value), (Match m) => m.Groups[2].Value.Trim()) : new Dictionary<int, string>());
		Dictionary<int, Dictionary<string, string>> dictionary = new Dictionary<int, Dictionary<string, string>>();
		foreach (Match item in Regex.Matches(File.ReadAllText(path), "(?m)^(sing_id|name)(\\d+)=([^\\r\\n]*)"))
		{
			int key = int.Parse(item.Groups[2].Value);
			if (!dictionary.TryGetValue(key, out var value))
			{
				value = (dictionary[key] = new Dictionary<string, string>());
			}
			value[item.Groups[1].Value] = item.Groups[3].Value.Trim();
		}
		return (from r in dictionary
			where r.Value.ContainsKey("name") && r.Value.ContainsKey("sing_id")
			select new ModQuest(int.Parse(r.Value["sing_id"]), names.GetValueOrDefault(r.Key, r.Value["name"]), r.Value["name"]) into q
			group q by q.Id into g
			select g.First()).ToList();
	}

	public static string TrackPath(string root, string relative)
	{
		if (!Regex.IsMatch(relative, "\\A1_MOD/BGM/[a-f0-9]+\\.ogg\\z"))
		{
			throw new InvalidDataException("Invalid track path or pool.");
		}
		return Path.Combine(root, "BGM", Path.GetFileName(relative));
	}

	public static List<ModQuest> Quests(string gameRoot, string language)
	{
		List<ModQuest> list = new List<ModQuest>();
		foreach (string item in Directory.EnumerateFiles(Path.Combine(gameRoot, "rom", "single"), "quest_prop_*.txt"))
		{
			string input = File.ReadAllText(item);
			if (!int.TryParse(Regex.Match(input, "(?m)^quest_id=(\\d+)").Groups[1].Value, out var result))
			{
				continue;
			}
			string text = Regex.Match(input, "(?m)^quest_name=([^\\r\\n]*)").Groups[1].Value.Trim();
			string text2 = text;
			string path = Path.Combine(gameRoot, "Localization", language, "rom", "single", Path.GetFileName(item));
			if (language != "ja" && File.Exists(path))
			{
				string text3 = Regex.Match(File.ReadAllText(path), "(?m)^quest_name=([^\\r\\n]*)").Groups[1].Value.Trim();
				if (!string.IsNullOrWhiteSpace(text3))
				{
					text2 = text3;
				}
			}
			list.Add(new ModQuest(result, string.IsNullOrWhiteSpace(text2) ? "Unnamed quest" : text2, text)
			{
				BattleWaves = (from w in (from Match m in Regex.Matches(input, "(?m)^battle\\.\\d+\\.wave_id=(\\d+)\\s*$")
						select int.Parse(m.Groups[1].Value)).Distinct()
					orderby w
					select w).ToArray()
			});
		}
		return list.OrderBy((ModQuest q) => q.Id).ToList();
	}

	public static ModSettings Load(string root)
	{
		string path = Path.Combine(root, "Dialogue", "settings.json");
		string path2 = Path.Combine(root, "BGM", "settings.json");
		if (File.Exists(path) || File.Exists(path2))
		{
			string path3 = Path.Combine(root, "settings.json");
			ModSettings modSettings = (((!File.Exists(path) || !File.Exists(path2)) && File.Exists(path3)) ? (JsonSerializer.Deserialize<ModSettings>(File.ReadAllText(path3), Json) ?? throw new InvalidDataException("The mod settings are empty.")) : new ModSettings());
			ModSettings modSettings2 = (File.Exists(path2) ? (JsonSerializer.Deserialize<ModSettings>(File.ReadAllText(path2), Json) ?? throw new InvalidDataException("The music settings are empty.")) : modSettings);
			if (!File.Exists(path))
			{
				modSettings2.DialogueEnabled = modSettings.DialogueEnabled;
				modSettings2.DialogueFolder = modSettings.DialogueFolder;
				modSettings2.Dialogues = modSettings.Dialogues;
				modSettings2.PortraitLibraryPath = modSettings.PortraitLibraryPath;
				return modSettings2;
			}
			DialogueIndex dialogueIndex = JsonSerializer.Deserialize<DialogueIndex>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("The dialogue list is empty.");
			modSettings2.Dialogues.Clear();
			modSettings2.DialogueEnabled = dialogueIndex.Enabled;
			modSettings2.DialogueFolder = dialogueIndex.GeneratedFolder;
			modSettings2.Title = dialogueIndex.Title;
			modSettings2.Author = dialogueIndex.Author;
			modSettings2.Version = dialogueIndex.Version;
			modSettings2.Description = dialogueIndex.Description;
			modSettings2.PortraitLibraryPath = dialogueIndex.PortraitLibraryPath;
			{
				foreach (string item in dialogueIndex.Events.Concat(dialogueIndex.Quests ?? new List<string>()))
				{
					string json = File.ReadAllText(Path.Combine(root, "Dialogue", item));
					using JsonDocument jsonDocument = JsonDocument.Parse(json);
					if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array)
					{
						foreach (ModDialogue item2 in JsonSerializer.Deserialize<List<ModDialogue>>(json, Json) ?? throw new InvalidDataException("The dialogue of a trigger is empty."))
						{
							modSettings2.Dialogues.Add(item2);
						}
					}
					else
					{
						modSettings2.Dialogues.Add(JsonSerializer.Deserialize<ModDialogue>(json, Json) ?? throw new InvalidDataException("The dialogue of a trigger is empty."));
					}
				}
				return modSettings2;
			}
		}
		string path4 = Path.Combine(root, "settings.json");
		if (!File.Exists(path4))
		{
			return new ModSettings();
		}
		return JsonSerializer.Deserialize<ModSettings>(File.ReadAllText(path4), Json) ?? throw new InvalidDataException("The mod settings are empty.");
	}

	public static string QuestDirectory(int id, string name)
	{
		return $"{id:000000}_" + string.Concat(name.Select((char c) => (!Path.GetInvalidFileNameChars().Contains(c)) ? c : '_')).Trim().TrimEnd('.');
	}

	public static void AtomicWrite(string path, string text)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string text2 = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		File.WriteAllText(text2, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.Move(text2, path, overwrite: true);
	}

	public static void Save(string root, ModSettings settings)
	{
		Save(root, settings, audio: true, dialogue: true);
	}

	public static void SaveComponent(string root, ModSettings settings, bool audio)
	{
		Save(root, settings, audio, !audio);
	}

	private static void Save(string root, ModSettings settings, bool audio, bool dialogue)
	{
		string text = (audio ? AudioRuntime(settings) : "");
		string runtime = (dialogue ? DialogueRuntime(settings) : "");
		if (audio)
		{
			AtomicWrite(Path.Combine(root, "BGM", "settings.json"), JsonSerializer.Serialize(new { settings.Title, settings.Author, settings.Version, settings.Description, settings.BgmEnabled, settings.Tracks }, Json));
			AtomicWrite(Path.Combine(root, "BGM", "runtime.ini"), text);
		}
		if (dialogue)
		{
			SaveDialogue(root, settings, runtime);
		}
		if (audio && dialogue)
		{
			ModLibrary.Compose(root);
		}
		if (audio && dialogue)
		{
			CleanBgm(root, settings).GetAwaiter().GetResult();
		}
		if (audio && dialogue)
		{
			string text2 = Path.Combine(root, "settings.json");
			if (File.Exists(text2))
			{
				Directory.CreateDirectory(Path.Combine(root, "Dialogue", "legacy"));
				File.Move(text2, Path.Combine(root, "Dialogue", "legacy", "settings-" + Guid.NewGuid().ToString("N") + ".json"));
			}
		}
	}

	private static string AudioRuntime(ModSettings settings)
	{
		if (settings.Tracks.Count > 1024)
		{
			throw new InvalidDataException("Up to 1024 tracks are supported.");
		}
		StringBuilder stringBuilder = new StringBuilder("[bgm]\r\nenabled=" + (settings.BgmEnabled ? "1" : "0") + "\r\ncount=" + settings.Tracks.Count + "\r\n");
		for (int i = 0; i < settings.Tracks.Count; i++)
		{
			ModTrack modTrack = settings.Tracks[i];
			if (!Regex.IsMatch(modTrack.Pool, "\\A[a-zA-Z0-9_.-]+\\z") || !Regex.IsMatch(modTrack.File, "\\A1_MOD/BGM/[a-f0-9]+\\.ogg\\z"))
			{
				throw new InvalidDataException("Invalid track path or pool.");
			}
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(23, 3, stringBuilder2);
			handler.AppendLiteral("[track");
			handler.AppendFormatted(i);
			handler.AppendLiteral("]\r\npool=");
			handler.AppendFormatted(modTrack.Pool);
			handler.AppendLiteral("\r\nfile=");
			handler.AppendFormatted(modTrack.File);
			handler.AppendLiteral("\r\n");
			stringBuilder2.Append(ref handler);
		}
		return stringBuilder.ToString();
	}

	private static string DialogueRuntime(ModSettings settings)
	{
		if (settings.Dialogues.Count > 512)
		{
			throw new InvalidDataException("Up to 512 dialogue triggers are supported.");
		}
		if (settings.Dialogues.Select((ModDialogue d) => d.Id).Distinct<string>(StringComparer.Ordinal).Count() != settings.Dialogues.Count)
		{
			throw new InvalidDataException("Invalid event ID.");
		}
		if (settings.DialogueEnabled && !Regex.IsMatch(settings.DialogueFolder, "\\ADialogue/(generated/dialogue-|Events/shared-)[a-f0-9]{32}\\z"))
		{
			throw new InvalidDataException("Save and build the dialogue first.");
		}
		StringBuilder stringBuilder = new StringBuilder("[dialogue]\r\nenabled=" + (settings.DialogueEnabled ? "1" : "0") + "\r\n");
		stringBuilder.Append("folder=" + settings.DialogueFolder + "\r\n");
		stringBuilder.Append("count=" + settings.Dialogues.Count + "\r\n");
		for (int num = 0; num < settings.Dialogues.Count; num++)
		{
			ModDialogue modDialogue = settings.Dialogues[num];
			if (!Regex.IsMatch(modDialogue.Id, "\\A[a-f0-9]{32}\\z"))
			{
				throw new InvalidDataException("Invalid event ID.");
			}
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(50, 6, stringBuilder2);
			handler.AppendLiteral("[dialogue");
			handler.AppendFormatted(num);
			handler.AppendLiteral("]\r\nquest=");
			handler.AppendFormatted(modDialogue.Quest);
			handler.AppendLiteral("\r\nsequence=");
			handler.AppendFormatted(modDialogue.Trigger);
			handler.AppendLiteral("\r\ntype=");
			handler.AppendFormatted(modDialogue.TargetKind);
			handler.AppendLiteral("\r\nonce=");
			handler.AppendFormatted(modDialogue.Once ? 1 : 0);
			handler.AppendLiteral("\r\nid=");
			handler.AppendFormatted(modDialogue.Id);
			handler.AppendLiteral("\r\n");
			stringBuilder3.Append(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(35, 2, stringBuilder2);
			handler.AppendLiteral("continue_screen=");
			handler.AppendFormatted(modDialogue.ContinueScreen ? 1 : 0);
			handler.AppendLiteral("\r\ncontinue_image=");
			handler.AppendFormatted(modDialogue.ContinueCustomImage ? 1 : 0);
			handler.AppendLiteral("\r\n");
			stringBuilder4.Append(ref handler);
			if (settings.DialogueFolder.StartsWith("Dialogue/Events/shared-", StringComparison.Ordinal))
			{
				stringBuilder.Append("resource=" + ModDialogueCompiler.EventResource(settings.DialogueFolder, modDialogue.Id) + "\r\n");
			}
		}
		return stringBuilder.ToString();
	}

	private static void SaveDialogue(string root, ModSettings settings, string runtime)
	{
		DialogueIndex dialogueIndex = new DialogueIndex
		{
			Enabled = settings.DialogueEnabled,
			GeneratedFolder = settings.DialogueFolder,
			Title = settings.Title,
			Author = settings.Author,
			Version = settings.Version,
			Description = settings.Description,
			PortraitLibraryPath = settings.PortraitLibraryPath
		};
		foreach (ModDialogue dialogue in settings.Dialogues)
		{
			string text = Path.Combine("Events", "event-" + dialogue.Id, "event.json");
			AtomicWrite(Path.Combine(root, "Dialogue", text), JsonSerializer.Serialize(dialogue, Json));
			dialogueIndex.Events.Add(text);
		}
		AtomicWrite(Path.Combine(root, "Dialogue", "settings.json"), JsonSerializer.Serialize(dialogueIndex, Json));
		AtomicWrite(Path.Combine(root, "Dialogue", "runtime.ini"), runtime);
		ModPortraits.RetireLegacyImports(root, settings);
	}

	public static void Organize(string root)
	{
		ModPortraits.OrganizeCatalog(root);
		string text = Path.Combine(root, "Dialogue", "generated");
		string text2 = Path.Combine(root, "Dialogue", "Legacy", "generated");
		if (Directory.Exists(text) && !Directory.Exists(text2) && Load(root).DialogueFolder.StartsWith("Dialogue/Events/shared-", StringComparison.Ordinal))
		{
			Directory.CreateDirectory(Path.GetDirectoryName(text2));
			Directory.Move(text, text2);
		}
		string path = Path.Combine(root, "Dialogue", "Events");
		if (Directory.Exists(path))
		{
			foreach (string item in from d in Directory.EnumerateDirectories(path)
				where Regex.IsMatch(Path.GetFileName(d), "^\\d{6}$") && File.Exists(Path.Combine(d, "events.json"))
				select d)
			{
				string text3 = Path.Combine(root, "Dialogue", "Legacy", "Events", Path.GetFileName(item));
				if (!Directory.Exists(text3))
				{
					Directory.CreateDirectory(Path.GetDirectoryName(text3));
					Directory.Move(item, text3);
				}
			}
		}
		string text4 = Path.Combine(root, "Dialogue");
		if (!Directory.Exists(text4))
		{
			return;
		}
		string[] directories = Directory.GetDirectories(text4);
		foreach (string text5 in directories)
		{
			if (Regex.IsMatch(Path.GetFileName(text5), "\\A\\d{6}_") && File.Exists(Path.Combine(text5, "dialogue.json")))
			{
				string text6 = Path.Combine(text4, "Legacy", Path.GetFileName(text5));
				if (!Directory.Exists(text6))
				{
					Directory.CreateDirectory(Path.GetDirectoryName(text6));
					Directory.Move(text5, text6);
				}
			}
		}
	}

	public static async Task SaveBgmAndClean(string root, ModSettings settings)
	{
		SaveComponent(root, settings, audio: true);
		ModLibrary.Compose(root);
		await CleanBgm(root, settings).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static async Task CleanBgm(string root, ModSettings settings)
	{
		HashSet<string> used = settings.Tracks.Select((ModTrack t) => Path.GetFileName(t.File)).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		string[] array = Directory.EnumerateFiles(Path.Combine(root, "BGM"), "*.ogg").ToArray();
		foreach (string file in array)
		{
			string name = Path.GetFileName(file);
			if (!Regex.IsMatch(name, "\\A[a-f0-9]{32}\\.ogg\\z") || used.Contains(name))
			{
				continue;
			}
			int retry = 0;
			while (true)
			{
				try
				{
					File.Delete(file);
				}
				catch (IOException ex) when ((ex.HResult & 0xFFFF) == 32 && retry < 20)
				{
					await Task.Delay(250).ConfigureAwait(continueOnCapturedContext: false);
					if (!(JsonSerializer.Deserialize<ModSettings>(File.ReadAllText(Path.Combine(root, "BGM", "settings.json")), Json) ?? throw new InvalidDataException("The music settings are empty.")).Tracks.Any((ModTrack t) => string.Equals(Path.GetFileName(t.File), name, StringComparison.OrdinalIgnoreCase)))
					{
						goto IL_0217;
					}
				}
				break;
				IL_0217:
				retry++;
			}
		}
	}

	public static List<ModPool> Pools(string gameRoot)
	{
		List<ModPool> list = new List<ModPool>
		{
			new ModPool("battle_all", "All battles · continuous across waves / Fatal / Grand"),
			new ModPool("menu", "Home · separate pool"),
			new ModPool("room", "My Room · separate pool"),
			new ModPool("menu_room_shared", "Home + My Room · shared continuous pool")
		};
		Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		string path = Path.Combine(gameRoot, "rom", "single");
		if (Directory.Exists(path))
		{
			foreach (string item in Directory.EnumerateFiles(path, "quest_prop_*.txt"))
			{
				string input = File.ReadAllText(item);
				string text = Regex.Match(input, "(?m)^quest_name=(.*)").Groups[1].Value.Trim();
				foreach (Match item2 in Regex.Matches(input, "(?m)^battle\\.(\\d+)\\.battle_bgm=(\\S+)"))
				{
					string key = item2.Groups[2].Value + ".ogg";
					if (!dictionary.TryGetValue(key, out var value))
					{
						value = (dictionary[key] = new List<string>());
					}
					value.Add(text + " / " + "Wave" + item2.Groups[1].Value);
				}
				Match match2 = Regex.Match(input, "(?m)^result_bgm_name=(\\S+)");
				if (match2.Success)
				{
					string key2 = match2.Groups[1].Value + ".ogg";
					if (!dictionary.TryGetValue(key2, out var value2))
					{
						value2 = (dictionary[key2] = new List<string>());
					}
					value2.Add(text + " / " + "Solo quest victory result");
				}
			}
		}
		string path2 = Path.Combine(gameRoot, "rom", "mst_data", "arms_mst_stage_asset.farc");
		if (File.Exists(path2))
		{
			Archive archive = new Archive(File.ReadAllBytes(path2));
			string input2 = Encoding.UTF8.GetString(archive.Read(archive.Entries.Single((ArchiveEntry e) => e.Name == "arms_mst_stage_asset.bin")));
			Dictionary<string, Dictionary<string, string>> dictionary2 = new Dictionary<string, Dictionary<string, string>>();
			foreach (Match item3 in Regex.Matches(input2, "(?m)^stage_asset\\.(\\d+)\\.([^=\\r\\n]+)=([^\\r\\n]*)"))
			{
				string value3 = item3.Groups[1].Value;
				if (!dictionary2.TryGetValue(value3, out var value4))
				{
					value4 = (dictionary2[value3] = new Dictionary<string, string>());
				}
				value4[item3.Groups[2].Value] = item3.Groups[3].Value;
			}
			Dictionary<string, string> dictionary4 = new Dictionary<string, string>
			{
				["normal_bgm"] = "Normal battle",
				["fatal_bgm"] = "Fatal battle",
				["result_bgm_win"] = "Victory",
				["result_bgm_lose"] = "Defeat",
				["result_bgm_draw"] = "Draw"
			};
			foreach (Dictionary<string, string> value9 in dictionary2.Values)
			{
				foreach (KeyValuePair<string, string> item4 in dictionary4)
				{
					if (!value9.TryGetValue(item4.Key, out var value5) || value5 == "none" || string.IsNullOrEmpty(value5))
					{
						continue;
					}
					if (value5 == "default")
					{
						string text2;
						if (item4.Key == "result_bgm_win")
						{
							text2 = "arms_bgm15";
						}
						else
						{
							string key3 = item4.Key;
							bool flag = ((key3 == "result_bgm_lose" || key3 == "result_bgm_draw") ? true : false);
							text2 = (flag ? "arms_bgm16" : "");
						}
						value5 = text2;
						if (value5.Length == 0)
						{
							continue;
						}
					}
					string key4 = value5 + ".ogg";
					if (!dictionary.TryGetValue(key4, out var value6))
					{
						value6 = (dictionary[key4] = new List<string>());
					}
					value6.Add(value9.GetValueOrDefault("disp_name", value9.GetValueOrDefault("[key]", "")) + " / " + item4.Value);
				}
			}
		}
		Dictionary<string, string> dictionary5 = new Dictionary<string, string>
		{
			["arms_bgm04"] = "Home",
			["arms_bgm05"] = "My Room",
			["arms_bgm06"] = "Summon",
			["arms_bgm12"] = "Login",
			["arms_bgm15"] = "Ranked / Servant battle · default victory",
			["arms_bgm23"] = "Solo quest · default victory",
			["arms_bgm16"] = "Default defeat / battle draw"
		};
		string text3 = Path.Combine(gameRoot, "rom");
		if (Directory.Exists(text3))
		{
			foreach (string item5 in Directory.EnumerateFiles(text3, "*.txt", SearchOption.AllDirectories))
			{
				foreach (string item6 in File.ReadLines(item5))
				{
					foreach (Match item7 in Regex.Matches(item6, "\\barms_bgm[a-zA-Z0-9_]*"))
					{
						string key5 = item7.Value + ".ogg";
						if (!dictionary.TryGetValue(key5, out var value7))
						{
							value7 = (dictionary[key5] = new List<string>());
						}
						value7.Add(Path.GetRelativePath(text3, item5).Replace('\\', '/') + " : " + item6.Trim());
					}
				}
			}
		}
		string path3 = Path.Combine(gameRoot, "rom", "sound", "bgm");
		if (Directory.Exists(path3))
		{
			foreach (string item8 in Directory.EnumerateFiles(path3, "*.ogg").OrderBy(Path.GetFileName))
			{
				string fileName = Path.GetFileName(item8);
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(item8);
				string[] array = dictionary.GetValueOrDefault(fileName, new List<string>()).Distinct().ToArray();
				string value8;
				string text4 = (dictionary5.TryGetValue(fileNameWithoutExtension, out value8) ? value8 : ((array.Length != 0) ? string.Join("; ", array.Take(2)) : "Use not known"));
				list.Add(new ModPool(fileName, text4 + " · " + fileNameWithoutExtension, (array.Length != 0) ? string.Join("\n", array) : "The game's files do not say where this track plays."));
			}
		}
		string[] array2 = new string[3] { "arms_bgm23.ogg", "arms_bgm16.ogg", "arms_bgm15.ogg" };
		foreach (string key6 in array2)
		{
			ModPool modPool = list.FirstOrDefault((ModPool p) => p.Key == key6);
			if (modPool != null)
			{
				list.Remove(modPool);
				list.Insert(4, modPool);
			}
		}
		return list;
	}

	public static double TrackDuration(string file)
	{
		return (double)OggSamples(file) / 44100.0;
	}

	private static long OggSamples(string file)
	{
		using FileStream fileStream = File.OpenRead(file);
		using BinaryReader binaryReader = new BinaryReader(fileStream);
		long num = 0L;
		while (fileStream.Position < fileStream.Length)
		{
			byte[] array = binaryReader.ReadBytes(27);
			if (array.Length != 27 || Encoding.ASCII.GetString(array, 0, 4) != "OggS")
			{
				throw new InvalidDataException("The music file is damaged.");
			}
			long num2 = BitConverter.ToInt64(array, 6);
			if (num2 > num)
			{
				num = num2;
			}
			byte[] source = binaryReader.ReadBytes(array[26]);
			fileStream.Seek(source.Sum((byte x) => x), SeekOrigin.Current);
		}
		return num;
	}

	public static async Task<ModTrack> Import(string root, string source, string pool, double? begin, double? end, bool autoLoop = false, bool autoLoudness = false)
	{
		if (begin.HasValue != end.HasValue || (begin.HasValue && (!double.IsFinite(begin.Value) || !double.IsFinite(end.Value) || begin < 0.0 || end <= begin)))
		{
			throw new InvalidDataException("Loop end must exceed loop start, in seconds.");
		}
		string encoder = Path.Combine(root, "Tools", "ffmpeg.exe");
		if (!File.Exists(encoder))
		{
			throw new FileNotFoundException("Adding music needs App\\1_MOD\\Tools\\ffmpeg.exe, which Cloud23333's latest update installs.", encoder);
		}
		string loopAnalysis = "";
		if (autoLoop && !begin.HasValue)
		{
			ModLoopAnalysis modLoopAnalysis = await ModAudioAnalysis.Loop(encoder, source);
			begin = modLoopAnalysis.Begin;
			end = modLoopAnalysis.End;
			loopAnalysis = modLoopAnalysis.Method;
		}
		ModBgmLoudnessReference modBgmLoudnessReference = ((!autoLoudness) ? null : (await ModBgmReference.ForPool(root, pool)));
		ModBgmLoudnessReference modBgmLoudnessReference2 = modBgmLoudnessReference;
		ModLoudnessAnalysis modLoudnessAnalysis = ((!(modBgmLoudnessReference2 != null)) ? null : (await ModAudioAnalysis.Loudness(encoder, source, modBgmLoudnessReference2.Integrated, modBgmLoudnessReference2.TruePeak, modBgmLoudnessReference2.Range)));
		ModLoudnessAnalysis modLoudnessAnalysis2 = modLoudnessAnalysis;
		double gain = modLoudnessAnalysis2?.GainDb ?? 0.0;
		Directory.CreateDirectory(Path.Combine(root, "BGM"));
		string id = Guid.NewGuid().ToString("N");
		string output = Path.Combine(root, "BGM", id + ".ogg");
		ProcessStartInfo processStartInfo = new ProcessStartInfo(encoder)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true
		};
		string[] array = new string[18]
		{
			"-nostdin",
			"-v",
			"error",
			"-i",
			source,
			"-vn",
			"-map",
			"0:a:0",
			"-map_metadata",
			"-1",
			"-ar",
			44100.ToString(CultureInfo.InvariantCulture),
			"-ac",
			"2",
			"-c:a",
			"libvorbis",
			"-q:a",
			"6"
		};
		foreach (string item in array)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		if (modLoudnessAnalysis2 != null)
		{
			processStartInfo.ArgumentList.Add("-af");
			processStartInfo.ArgumentList.Add(modLoudnessAnalysis2.Filter);
		}
		if (begin.HasValue)
		{
			array = checked(new string[4]
			{
				"-metadata",
				"LOOP_BEGIN=" + ((long)(begin.Value * 44100.0)).ToString(CultureInfo.InvariantCulture),
				"-metadata",
				"LOOP_END=" + ((long)(end.Value * 44100.0)).ToString(CultureInfo.InvariantCulture)
			});
			foreach (string item2 in array)
			{
				processStartInfo.ArgumentList.Add(item2);
			}
		}
		processStartInfo.ArgumentList.Add(output);
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("Could not start the audio converter.");
		string errors = await process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();
		if (process.ExitCode != 0)
		{
			if (File.Exists(output))
			{
				File.Delete(output);
			}
			throw new InvalidDataException("Audio conversion failed: " + errors);
		}
		long num2;
		try
		{
			long num = OggSamples(output);
			if (end.HasValue && end.Value * 44100.0 > (double)num)
			{
				throw new InvalidDataException($"The loop end is past the end of the music ({(double)num / 44100.0:F3} s).");
			}
			num2 = num;
		}
		catch
		{
			File.Delete(output);
			throw;
		}
		if (!begin.HasValue)
		{
			string tagged = output + ".tagged.ogg";
			ProcessStartInfo processStartInfo2 = new ProcessStartInfo(encoder)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true
			};
			array = new string[12]
			{
				"-nostdin",
				"-v",
				"error",
				"-i",
				output,
				"-c:a",
				"copy",
				"-metadata",
				"LOOP_BEGIN=0",
				"-metadata",
				"LOOP_END=" + num2.ToString(CultureInfo.InvariantCulture),
				tagged
			};
			foreach (string item3 in array)
			{
				processStartInfo2.ArgumentList.Add(item3);
			}
			using Process tagging = Process.Start(processStartInfo2) ?? throw new IOException("Could not write the loop tags of the music file.");
			string error = await tagging.StandardError.ReadToEndAsync();
			await tagging.WaitForExitAsync();
			if (tagging.ExitCode != 0)
			{
				File.Delete(output);
				if (File.Exists(tagged))
				{
					File.Delete(tagged);
				}
				throw new IOException(error);
			}
			File.Move(tagged, output, overwrite: true);
		}
		return new ModTrack
		{
			Pool = pool,
			Title = Path.GetFileNameWithoutExtension(source),
			File = "1_MOD/BGM/" + id + ".ogg",
			LoopBegin = begin,
			LoopEnd = end,
			AutoLoudness = autoLoudness,
			GainDb = gain,
			LoopAnalysis = loopAnalysis
		};
	}
}
