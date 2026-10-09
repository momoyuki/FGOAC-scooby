using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// The script Play runs. On game 11.00 it is FGOAC_Launcher.ps1, which writes this launcher's runtime ini blocks itself.
/// On any other build it is Cloud23333's own App\FGO_Launcher.ps1, read afresh at every Play so each launch rule he
/// adds is kept: the add-on, when installed, goes in through a generated copy that differs from his script only by
/// lines inserted before the one that adds ago.exe to the launch, and this launcher's blocks go into App\segatools.ini
/// key by key. When that line is not there exactly once, his script runs as it is and the add-on is off for that start.
/// With the HUD at the screen edges, the inserted lines also put back the whole-surface touch canvas his script resets
/// to 16:9 after it copies segatools.ini; his file is never edited.
/// </summary>
internal static class LaunchScript
{
	internal const string Own = "FGOAC_Launcher.ps1";

	internal const string His = "FGO_Launcher.ps1";

	internal const string Generated = "FGOAC_Launcher.generated.ps1";

	internal const string AddOn = "fgoacaddon.dll";

	/// <summary>The line of his script that adds ago.exe to the launch arguments; every -k before it is a hook.</summary>
	internal const string Anchor = "$launchArguments.Add($agoPath)";

	/// <summary>Names in his script the touch lines use: his key-merge writer, the runtime ini and the game's resolution.</summary>
	private static readonly string[] TouchNames = new string[4] { "function Set-IniValue\\b", "\\$runtimeIni\\b", "\\$effectiveResolutionWidth\\b", "\\$effectiveResolutionHeight\\b" };

	/// <summary>The script to run, whether it takes -CommunityServer, and a sentence for the player when the add-on is off.</summary>
	internal sealed record Choice(string Script, bool Own, string? Note);

	/// <summary>Prepares the start for the App folder <paramref name="gameRoot"/>; never throws for a file it cannot write, it says so in Note.</summary>
	internal static Choice Prepare(string gameRoot)
	{
		string installRoot = Path.GetFullPath(Path.Combine(gameRoot, ".."));
		if (GameBuild.Is1100(installRoot))
		{
			return new Choice(Path.Combine(gameRoot, Own), Own: true, null);
		}
		string his = Path.Combine(gameRoot, His);
		List<string> notes = new List<string>();
		try
		{
			BattleEffects.ApplyNpScreen(gameRoot);
		}
		catch (Win32Exception ex)
		{
			notes.Add("Hide NP screen could not be written to App\\graphics-effects.ini: " + ex.Message.TrimEnd('.') + ".");
		}
		try
		{
			WriteBlocks(gameRoot);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException)
		{
			notes.Add("The cursor lock, widescreen and PC play settings could not be written to App\\segatools.ini: " + ex.Message.TrimEnd('.') + ".");
		}
		string addOn = Path.Combine(gameRoot, AddOn);
		if (!File.Exists(his) || !File.Exists(addOn))
		{
			return new Choice(his, Own: false, Join(notes));
		}
		string text = File.ReadAllText(his);
		string[] lines = text.Split('\n');
		int[] anchors = Enumerable.Range(0, lines.Length).Where((int index) => lines[index].Trim() == Anchor).ToArray();
		if (anchors.Length != 1)
		{
			notes.Add("The add-on is off for this start: Cloud23333's launch script has changed, so it ran as he shipped it.");
			return new Choice(his, Own: false, Join(notes));
		}
		string line = lines[anchors[0]];
		string indent = line.Substring(0, line.Length - line.TrimStart().Length);
		string end = line.EndsWith('\r') ? "\r" : "";
		string quoted = "'" + addOn.Replace("'", "''") + "'";
		string touch = "";
		if (HudAtEdges(gameRoot))
		{
			if (TouchNames.All((string name) => Regex.IsMatch(text, name)))
			{
				touch = indent + "Set-IniValue -Path $runtimeIni -Section \"touch\" -Key \"inputWidth\" -Value ([string]$effectiveResolutionWidth)" + end + "\n" + indent + "Set-IniValue -Path $runtimeIni -Section \"touch\" -Key \"inputHeight\" -Value ([string]$effectiveResolutionHeight)" + end + "\n";
			}
			else
			{
				notes.Add("Touch over the whole screen for the HUD at the screen edges is off for this start: Cloud23333's launch script has changed.");
			}
		}
		lines[anchors[0]] = touch + indent + "$launchArguments.Add(\"-k\")" + end + "\n" + indent + "$launchArguments.Add(" + quoted + ")" + end + "\n" + line;
		string generated = Path.Combine(gameRoot, Generated);
		try
		{
			// Windows PowerShell reads a script without a byte order mark as the system code page.
			File.WriteAllText(generated, string.Join('\n', lines), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			notes.Add("The add-on is off for this start: App\\" + Generated + " could not be written: " + ex.Message.TrimEnd('.') + ".");
			return new Choice(his, Own: false, Join(notes));
		}
		return new Choice(generated, Own: false, Join(notes));
	}

	/// <summary>The saved HUD and menus at the screen edges switch, as FGOAC_Launcher.ps1 reads it; off when unreadable.</summary>
	private static bool HudAtEdges(string gameRoot)
	{
		try
		{
			string settingsPath = Path.Combine(gameRoot, "fgo-launcher.json");
			return File.Exists(settingsPath) && JsonNode.Parse(File.ReadAllText(settingsPath)) is JsonObject settings && Truthy((settings["widescreen"] as JsonObject)?["hudAnchor"], fallback: false);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException)
		{
			return false;
		}
	}

	private static string? Join(List<string> notes)
	{
		return (notes.Count == 0) ? null : string.Join(" ", notes);
	}

	/// <summary>
	/// The [pcport], [widescreen] and [cursor] blocks FGOAC_Launcher.ps1 writes into the runtime ini, with the same
	/// values, written into App\segatools.ini instead, which his script copies to the runtime ini; and the widescreen
	/// anchors template beside the runtime ini when it is missing.
	/// </summary>
	private static void WriteBlocks(string gameRoot)
	{
		string settingsPath = Path.Combine(gameRoot, "fgo-launcher.json");
		JsonObject settings = (File.Exists(settingsPath) && JsonNode.Parse(File.ReadAllText(settingsPath)) is JsonObject saved) ? saved : new JsonObject();
		List<(string Section, string Key, string Value)> keys = new List<(string, string, string)>();
		JsonObject? pcport = settings["pcport"] as JsonObject;
		foreach ((string name, bool on) in new (string, bool)[5] { ("noContinueTimer", true), ("noPopupTimers", true), ("noGpPurchase", false), ("noStoryTimer", false), ("noMenuTimer", false) })
		{
			keys.Add(("pcport", name, Truthy(pcport?[name], on) ? "1" : "0"));
		}
		JsonObject? widescreen = settings["widescreen"] as JsonObject;
		foreach ((string name, bool on) in new (string, bool)[3] { ("enable", true), ("vertPlus", true), ("hudAnchor", false) })
		{
			keys.Add(("widescreen", name, Truthy(widescreen?[name], on) ? "1" : "0"));
		}
		string anchors = Text(widescreen?["anchors"]) is { Length: > 0 } named ? named.Replace("\r", "").Replace("\n", "") : "widescreen-anchors.txt";
		keys.Add(("widescreen", "anchors", anchors));
		keys.Add(("widescreen", "anchorFix", (Text(widescreen?["anchorFix"]) ?? "").Replace("\r", "").Replace("\n", "")));
		keys.Add(("cursor", "confine", ((settings["cursor"] as JsonObject)?["confine"] is JsonValue confine && confine.TryGetValue(out bool locked) && locked) ? "1" : "0"));
		string iniPath = Path.Combine(gameRoot, "segatools.ini");
		byte[] original = File.Exists(iniPath) ? File.ReadAllBytes(iniPath) : Array.Empty<byte>();
		bool bom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
		string ini = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(original, bom ? 3 : 0, original.Length - (bom ? 3 : 0));
		string updated = ini;
		foreach ((string section, string key, string value) in keys)
		{
			updated = IniText.Set(updated, section, key, value);
		}
		if (updated != ini)
		{
			AtomicFile.WriteAllBytes(iniPath, (bom ? new byte[3] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(updated)).ToArray());
		}
		string runtime = Path.GetFullPath(Path.Combine(gameRoot, "..", "DEVICE", "runtime"));
		string template = Path.Combine(runtime, anchors);
		if (Path.GetFileName(anchors) == anchors && !File.Exists(template))
		{
			Directory.CreateDirectory(runtime);
			AtomicFile.WriteAllText(template, AnchorsTemplate);
		}
	}

	/// <summary>As FGOAC_Launcher.ps1 reads a switch: a JSON true or false, else the default.</summary>
	private static bool Truthy(JsonNode? node, bool fallback)
	{
		if (node is JsonValue value)
		{
			if (value.TryGetValue(out bool on))
			{
				return on;
			}
			if (value.TryGetValue(out double number))
			{
				return number != 0.0;
			}
			if (value.TryGetValue(out string? text))
			{
				return text.Length > 0;
			}
		}
		return fallback;
	}

	private static string? Text(JsonNode? node)
	{
		return (node is JsonValue value) ? value.ToString() : null;
	}

	private const string AnchorsTemplate = "# Widescreen HUD and menu anchors, read by the hook when [widescreen] hudAnchor=1 in the runtime ini.\r\n# Lines add to the hook's built-in table; a line here replaces the built-in line of the same name in the\r\n# same section. Sections: [battle] (also lines before any section), [menu] for every menu screen, and one\r\n# per screen: [logo] [title] [main_selector] [terminal] [formation] [quest_list] [summon_menu] [summon]\r\n# [shop] [my_room] [card_list] [online_lobby] [game_main] [result] [game_over] [loading] [card_login].\r\n# One line per layer or per point of a layer, as the game names them:\r\n#   SCENE/LAYER = SIDE GROUP [overhang=N]\r\n#   SCENE/LAYER/POINT = SIDE GROUP [overhang=N]\r\n# LAYER may end in * to match every layer starting with it. SCENE may be * on a SCENE/LAYER line: with\r\n# any side in a screen section, only with side F under [battle]. A point line overrides its layer's side.\r\n# SIDE: L, R, T or B moves toward that edge of the screen; TL, TR, BL or BR toward that corner; C holds\r\n# still; F stretches the 16:9 box over the whole screen; W stretches it across only (a bar at the box width).\r\n# GROUP names the panels that move together (up to 15 characters); every side but C needs one.\r\n# overhang=N (1 to 1920): the art already reaches N design units past the 16:9 edge, so it moves only\r\n# until that end meets the screen edge.\r\n# Example: gam_qst_main/info_quest_ui = TR quest\r\n# Lines starting with # or ; are ignored. The launcher writes this file only when it is missing.\r\n";
}
