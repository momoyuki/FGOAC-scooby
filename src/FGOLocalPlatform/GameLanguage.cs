using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// The game text language in fgo-launcher.json. gameLanguage is the language Cloud23333's launch scripts read from his
/// 1.10 update on: "en-fgoac" loads this launcher's English set from App\Localization\en-fgoac, a folder his updater
/// never writes; his own launcher writes his "en" at every Play. "ja" is the game's own text. chineseEnabled is
/// the switch game 11.00 reads for the English set in App\zh; his 1.10 scripts read it as Chinese, so it is written
/// only on 11.00.
/// </summary>
internal static class GameLanguage
{
	internal const string English = "en-fgoac";

	internal const string Japanese = "ja";

	/// <summary>True when the saved language is English: ours, his "en" (his launcher writes it for any code it does not list), or before gameLanguage a chineseEnabled that is true.</summary>
	internal static bool IsEnglish(JsonObject settings)
	{
		string? language = (settings["gameLanguage"] as JsonValue)?.ToString();
		if (language != null)
		{
			return language == English || language == "en";
		}
		return settings["chineseEnabled"] is JsonValue value && value.TryGetValue(out bool on) && on;
	}

	/// <summary>The folder under App\Localization of the game text language in App\fgo-launcher.json; "ja" is the game's own text.</summary>
	internal static string Folder(string appFolder)
	{
		try
		{
			JsonObject? settings = JsonNode.Parse(File.ReadAllText(Path.Combine(appFolder, "fgo-launcher.json"))) as JsonObject;
			return (settings?["gameLanguage"] as JsonValue)?.ToString() ?? Japanese;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
		{
			return Japanese;
		}
	}

	/// <summary>The line under the English switch; on Cloud23333's update it adds that a game started from his launcher shows Japanese text.</summary>
	internal static string Help(bool game1100)
	{
		return "Off leaves the game in Japanese. The switch takes effect the next time you press Play." + (game1100 ? "" : " Start the game from FGOAC scooby for this English: started from Cloud23333's launcher, the game's text stays Japanese.");
	}

	/// <summary>Saves the English switch: gameLanguage on every build, chineseEnabled too on game 11.00.</summary>
	internal static void Write(JsonObject settings, bool english, string installRoot)
	{
		if (GameBuild.Is1100(installRoot))
		{
			settings["chineseEnabled"] = english;
		}
		settings["gameLanguage"] = english ? English : Japanese;
	}

	/// <summary>Gives a settings file without gameLanguage the one its chineseEnabled meant; true when it changed.</summary>
	internal static bool Migrate(JsonObject settings)
	{
		if (settings["gameLanguage"] != null)
		{
			return false;
		}
		settings["gameLanguage"] = IsEnglish(settings) ? English : Japanese;
		return true;
	}
}
