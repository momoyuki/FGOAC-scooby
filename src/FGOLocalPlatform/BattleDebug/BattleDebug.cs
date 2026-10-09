using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FGOLocalPlatform;

/// <summary>
/// The Debug card's switches, kept under "battleDebug" in the Settings file and written by each Play into [battle_debug]
/// of battle-debug.ini, which the stock hook reads about once a second as on only at 1. A write rewrites that section
/// only: every switch as 1 or 0, then the four enemy switches the card does not show as 0. The card also holds three
/// switches of Cloud23333's 1.10 Debug page and his PvP map, kept and written the same way; only his hook reads them.
/// Each write records what the ini then holds: an ini that differs from the record was changed by his launcher or his
/// updater, and its switches and map are taken in place of the saved ones.
/// </summary>
public static class BattleDebug
{
	private const string SettingsKey = "battleDebug";

	/// <summary>The switches the Debug card shows, in file order.</summary>
	public static IReadOnlyList<string> Switches { get; } = new string[6] { "attack_stun", "one_hit", "full_np", "instant_noble", "invincible", "stop_timer" };

	/// <summary>The switches of Cloud23333's 1.10 Debug page the card shows on his build: auto clear, the 233 cost cap and the PvP map pick.</summary>
	public static IReadOnlyList<string> HisCardSwitches { get; } = new string[3] { "auto_clear", "cost_233", "pvp_select" };

	/// <summary>Every switch the Settings file keeps, in file order.</summary>
	private static IEnumerable<string> Saved => Switches.Concat(HisCardSwitches);

	private const string PvpStageKey = "pvp_stage";

	private const string WrittenKey = "written";

	private const string WrittenStageKey = "writtenStage";

	public static string IniPath(string appFolder)
	{
		return Path.Combine(appFolder, "battle-debug.ini");
	}

	/// <summary>
	/// The switches saved in the App folder's Settings file; for a switch it has none saved, whether the ini has it on,
	/// where launchers before it and Cloud23333's launcher keep them. The ini's alone when it changed since this launcher
	/// wrote it.
	/// </summary>
	public static IReadOnlySet<string> Load(string appFolder)
	{
		JsonObject? saved = SavedBlock(appFolder);
		IReadOnlySet<string> ini = Read(IniPath(appFolder));
		if (ChangedOutside(saved, ini, IniStage(IniPath(appFolder))))
		{
			return ini;
		}
		return Saved.Where((string name) => (saved?[name] is JsonValue value && value.TryGetValue(out bool on)) ? on : ini.Contains(name)).ToHashSet();
	}

	/// <summary>The PvP map saved in the Settings file, else, or when the ini changed since this launcher wrote it, the ini's pvp_stage; 0 when there is none.</summary>
	public static int PvpStage(string appFolder)
	{
		string path = IniPath(appFolder);
		JsonObject? saved = SavedBlock(appFolder);
		if (!ChangedOutside(saved, Read(path), IniStage(path)) && saved?[PvpStageKey] is JsonValue value && value.TryGetValue(out int stage) && stage > 0)
		{
			return stage;
		}
		return IniStage(path);
	}

	/// <summary>The ini's last pvp_stage above 0; 0 when there is none.</summary>
	private static int IniStage(string path)
	{
		int found = 0;
		if (File.Exists(path))
		{
			foreach (string line in File.ReadLines(path))
			{
				string[] pair = line.Split('=', 2);
				if (pair.Length == 2 && pair[0].Trim() == PvpStageKey && int.TryParse(pair[1].Trim(), out int number) && number > 0)
				{
					found = number;
				}
			}
		}
		return found;
	}

	/// <summary>True when the ini's switches or map differ from what this launcher recorded at its last write of it.</summary>
	private static bool ChangedOutside(JsonObject? saved, IReadOnlySet<string> ini, int iniStage)
	{
		if (saved?[WrittenKey] is not JsonArray written)
		{
			return false;
		}
		int stage = (saved[WrittenStageKey] is JsonValue value && value.TryGetValue(out int number)) ? number : 0;
		return !ini.SetEquals(written.Select((JsonNode? name) => name?.GetValue<string>() ?? "")) || stage != iniStage;
	}

	private static JsonObject? SavedBlock(string appFolder)
	{
		string settingsPath = Path.Combine(appFolder, "fgo-launcher.json");
		return (File.Exists(settingsPath) && JsonNode.Parse(File.ReadAllText(settingsPath)) is JsonObject settings) ? settings[SettingsKey] as JsonObject : null;
	}

	/// <summary>
	/// Keeps every switch as true or false, and the PvP map when there is one, under "battleDebug" in the App folder's
	/// Settings file, beside its other keys.
	/// </summary>
	public static void Save(string appFolder, IReadOnlySet<string> on, int pvpStage = 0)
	{
		string settingsPath = Path.Combine(appFolder, "fgo-launcher.json");
		JsonObject settings = (File.Exists(settingsPath) && JsonNode.Parse(File.ReadAllText(settingsPath)) is JsonObject existing) ? existing : new JsonObject();
		JsonObject? before = settings[SettingsKey] as JsonObject;
		JsonObject saved = new JsonObject();
		foreach (string name in Saved)
		{
			saved[name] = on.Contains(name);
		}
		if (pvpStage > 0)
		{
			saved[PvpStageKey] = pvpStage;
		}
		foreach (string key in new string[2] { WrittenKey, WrittenStageKey })
		{
			if (before?[key] is JsonNode kept)
			{
				saved[key] = JsonNode.Parse(kept.ToJsonString());
			}
		}
		settings[SettingsKey] = saved;
		AtomicFile.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}) + Environment.NewLine);
	}

	/// <summary>
	/// The Debug card's save: the Settings file, then the ini, unless an Online Play runs, whose ini keeps every switch 0.
	/// <paramref name="pvpStages"/> is the list of PvP maps his hook picks from, as his launcher writes it.
	/// </summary>
	public static void SaveFromCard(string appFolder, IReadOnlySet<string> on, bool onlineSession, int pvpStage = 0, string pvpStages = "")
	{
		Save(appFolder, on, pvpStage);
		if (!onlineSession)
		{
			Write(IniPath(appFolder), on, online: false, pvpStage, pvpStages);
			RecordWritten(appFolder);
		}
	}

	/// <summary>The ini for a Play: every switch 0 for Online, the saved switches and PvP map for Local; they are saved first, so zeros never replace them.</summary>
	public static void WriteForSession(string appFolder, bool online)
	{
		IReadOnlySet<string> on = Load(appFolder);
		int pvpStage = PvpStage(appFolder);
		Save(appFolder, on, pvpStage);
		Write(IniPath(appFolder), online ? new HashSet<string>() : on, online, online ? 0 : pvpStage);
		RecordWritten(appFolder);
	}

	/// <summary>Records under "battleDebug" the switches and the map the ini holds now, just written by this launcher.</summary>
	private static void RecordWritten(string appFolder)
	{
		string settingsPath = Path.Combine(appFolder, "fgo-launcher.json");
		if (!File.Exists(settingsPath) || JsonNode.Parse(File.ReadAllText(settingsPath)) is not JsonObject settings || settings[SettingsKey] is not JsonObject saved)
		{
			return;
		}
		string path = IniPath(appFolder);
		IReadOnlySet<string> on = Read(path);
		saved[WrittenKey] = new JsonArray(Saved.Where(on.Contains).Select((string name) => (JsonNode)JsonValue.Create(name)!).ToArray());
		saved[WrittenStageKey] = IniStage(path);
		AtomicFile.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}) + Environment.NewLine);
	}

	/// <summary>The switches the file sets to 1; none when it is missing. The last line for a switch wins.</summary>
	public static IReadOnlySet<string> Read(string path)
	{
		HashSet<string> on = new HashSet<string>();
		if (!File.Exists(path))
		{
			return on;
		}
		foreach (string line in File.ReadLines(path))
		{
			string[] pair = line.Split('=', 2);
			if (pair.Length == 2 && Saved.Contains(pair[0].Trim()))
			{
				if (pair[1].Trim() == "1")
				{
					on.Add(pair[0].Trim());
				}
				else
				{
					on.Remove(pair[0].Trim());
				}
			}
		}
		return on;
	}

	/// <summary>The four enemy switches the card does not show; always written 0.</summary>
	private static readonly string[] EnemySwitches = new string[4] { "freeze_enemy", "enemy_evade", "enemy_flee", "enemy_guard" };

	/// <summary>Cloud23333's free exploration switch in the same section, which the card does not show; 0 for an Online Play.</summary>
	private const string Roaming = "roam_enabled";

	/// <summary>
	/// Rewrites the [battle_debug] section, or adds it at the end: the card's switches, the enemy switches 0, his switches
	/// on the card, then for a Local Play with a PvP map pvp_stage, pvp_random 0 and pvp_stages when given, then every
	/// other line the section held, such as his free exploration and its values; <paramref name="online"/> writes free
	/// exploration 0 as well. Every line outside the section stays byte for byte.
	/// </summary>
	public static void Write(string path, IReadOnlySet<string> on, bool online = false, int pvpStage = 0, string pvpStages = "")
	{
		StringBuilder section = new StringBuilder("[battle_debug]\n");
		foreach (string name in Switches)
		{
			section.Append(name).Append('=').Append(on.Contains(name) ? '1' : '0').Append('\n');
		}
		foreach (string name in EnemySwitches)
		{
			section.Append(name).Append("=0\n");
		}
		foreach (string name in HisCardSwitches)
		{
			section.Append(name).Append('=').Append((!online && on.Contains(name)) ? '1' : '0').Append('\n');
		}
		List<string> rewritten = Saved.Concat(EnemySwitches).ToList();
		if (!online && pvpStage > 0)
		{
			section.Append(PvpStageKey).Append('=').Append(pvpStage).Append("\npvp_random=0\n");
			rewritten.Add(PvpStageKey);
			rewritten.Add("pvp_random");
			if (pvpStages.Length > 0)
			{
				section.Append("pvp_stages=").Append(pvpStages).Append('\n');
				rewritten.Add("pvp_stages");
			}
		}
		if (online)
		{
			section.Append(Roaming).Append("=0\n");
			rewritten.Add(Roaming);
		}
		HashSet<string> replaced = new HashSet<string>(rewritten, StringComparer.OrdinalIgnoreCase);
		// Latin-1 maps each byte to one character, so lines outside the section are written back unchanged.
		string old = File.Exists(path) ? Encoding.Latin1.GetString(File.ReadAllBytes(path)) : "";
		StringBuilder kept = new StringBuilder();
		StringBuilder text = new StringBuilder();
		bool inSection = false;
		int sectionAt = -1;
		foreach (Match line in Regex.Matches(old, "[^\n]*\n|[^\n]+\\z"))
		{
			string trimmed = line.Value.Trim();
			if (trimmed.StartsWith('['))
			{
				// As Windows reads a header: the name runs to the first ] or the line's end, trimmed.
				int close = trimmed.IndexOf(']');
				string name = (close < 0 ? trimmed.Substring(1) : trimmed.Substring(1, close - 1)).Trim();
				inSection = string.Equals(name, "battle_debug", StringComparison.OrdinalIgnoreCase);
				if (inSection)
				{
					if (sectionAt < 0)
					{
						sectionAt = text.Length;
					}
					continue;
				}
			}
			if (!inSection)
			{
				text.Append(line.Value);
			}
			else if (trimmed.Length > 0 && !replaced.Contains(trimmed.Split('=', 2)[0].Trim()))
			{
				kept.Append(line.Value.EndsWith('\n') ? line.Value : (line.Value + "\n"));
			}
		}
		section.Append(kept);
		bool written = sectionAt >= 0;
		if (written)
		{
			text.Insert(sectionAt, section.ToString());
		}
		if (!written)
		{
			if (text.Length > 0 && text[text.Length - 1] != '\n')
			{
				text.Append('\n');
			}
			text.Append(section);
		}
		AtomicFile.WriteAllBytes(path, Encoding.Latin1.GetBytes(text.ToString()));
	}
}
