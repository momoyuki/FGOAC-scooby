using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// Keeps the English set for the new build, App\Localization\en-fgoac, in step with the game files it was made from.
/// Its fgoac-overrides.json maps each override, by its path under the set (rom\...), to the SHA-256 of the Japanese file
/// under App\ it was built from. An override whose base file has changed, as after an update of Cloud23333's, is turned
/// off by renaming it to name.fgoac-off, so that file shows the game's own text and nothing else breaks; one whose base
/// matches again is turned back on. A base file is hashed only when its size or time differs from the last check, which
/// App\fgoac-state.json keeps with the exe header and his update markers seen then.
/// </summary>
internal static class EnglishSet
{
	internal const string Folder = "Localization\\en-fgoac";

	internal const string Overrides = "fgoac-overrides.json";

	internal const string State = "fgoac-state.json";

	internal const string Off = ".fgoac-off";

	/// <summary>UpdateDetected: the exe header or an update marker differs from the last check, which existed.</summary>
	internal sealed record Result(bool UpdateDetected, IReadOnlyList<string> TurnedOff, IReadOnlyList<string> TurnedOn);

	/// <summary>Checks the set for the App folder <paramref name="gameRoot"/>; writes only its own files.</summary>
	internal static Result Verify(string gameRoot)
	{
		string installRoot = Path.GetFullPath(Path.Combine(gameRoot, ".."));
		string statePath = Path.Combine(gameRoot, State);
		JsonObject before = (File.Exists(statePath) && TryParse(File.ReadAllText(statePath)) is JsonObject saved) ? saved : new JsonObject();
		string game = GameFingerprint(gameRoot, installRoot);
		// On 11.00 the game reads App\zh, not this set, whose 11.51 bases differ by design: nothing is turned off or
		// reported, and an override turned off before is put back.
		bool game1100 = GameBuild.Is1100(installRoot);
		bool updateDetected = !game1100 && before["game"] is JsonValue previous && previous.ToString() != game;
		JsonObject files = (before["files"] as JsonObject) ?? new JsonObject();
		JsonObject after = new JsonObject { ["game"] = game };
		JsonObject checkedFiles = new JsonObject();
		List<string> turnedOff = new List<string>();
		List<string> turnedOn = new List<string>();
		string setRoot = Path.Combine(gameRoot, Folder);
		string listPath = Path.Combine(setRoot, Overrides);
		if (File.Exists(listPath) && TryParse(File.ReadAllText(listPath)) is JsonObject list && list["files"] is JsonObject overrides)
		{
			foreach (KeyValuePair<string, JsonNode?> entry in overrides)
			{
				string relative = entry.Key.Replace('/', '\\');
				string expected = entry.Value?.ToString() ?? "";
				string basePath = Path.Combine(gameRoot, relative);
				string onPath = Path.Combine(setRoot, relative);
				string offPath = onPath + Off;
				if (game1100)
				{
					if (!File.Exists(onPath) && File.Exists(offPath))
					{
						File.Move(offPath, onPath);
					}
					continue;
				}
				string stamp = File.Exists(basePath) ? (new FileInfo(basePath).Length + ":" + File.GetLastWriteTimeUtc(basePath).Ticks) : "missing";
				bool matches;
				if (files[entry.Key] is JsonObject known && known["stamp"]?.ToString() == stamp && known["expected"]?.ToString() == expected && known["matches"] is JsonValue verdict)
				{
					matches = verdict.GetValue<bool>();
				}
				else
				{
					matches = stamp != "missing" && string.Equals(Updater.HashFile(basePath), expected, StringComparison.OrdinalIgnoreCase);
				}
				checkedFiles[entry.Key] = new JsonObject { ["stamp"] = stamp, ["expected"] = expected, ["matches"] = matches };
				if (!matches && File.Exists(onPath))
				{
					File.Move(onPath, offPath, overwrite: true);
					turnedOff.Add(relative);
				}
				else if (matches && !File.Exists(onPath) && File.Exists(offPath))
				{
					File.Move(offPath, onPath);
					turnedOn.Add(relative);
				}
			}
		}
		after["files"] = checkedFiles;
		string text = after.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
		if (!File.Exists(statePath) || File.ReadAllText(statePath) != text)
		{
			AtomicFile.WriteAllText(statePath, text);
		}
		return new Result(updateDetected, turnedOff, turnedOn);
	}

	/// <summary>The exe header and every update-*.json at the install root, by name, size and time.</summary>
	private static string GameFingerprint(string gameRoot, string installRoot)
	{
		StringBuilder text = new StringBuilder(GameBuild.Header(Path.Combine(gameRoot, "ago.exe")));
		foreach (string marker in Directory.EnumerateFiles(installRoot, "update-*.json").OrderBy((string path) => path, StringComparer.OrdinalIgnoreCase))
		{
			text.Append('|').Append(Path.GetFileName(marker)).Append(':').Append(new FileInfo(marker).Length).Append(':').Append(File.GetLastWriteTimeUtc(marker).Ticks);
		}
		return text.ToString();
	}

	private static JsonNode? TryParse(string text)
	{
		try
		{
			return JsonNode.Parse(text);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
