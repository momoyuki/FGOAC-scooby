using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FGOLocalPlatform;

/// <summary>
/// The PvP maps of Cloud23333's map pick, listed as his SceneCatalog lists them: the stage ids 2001 to 2030 in the
/// master's stage_asset table whose stage file is in App\rom\stage, in stage order. Each is named in English from this
/// launcher's English set, App\Localization\en-fgoac\server-display-text.json, by the stage's Japanese name; a map it
/// does not name is named by its number.
/// </summary>
internal static class PvpStages
{
	internal sealed record Stage(int StageId, string Code, string Name, bool Named)
	{
		public string Display => Named ? $"{Name} · {StageId}" : Name;

		public override string ToString()
		{
			return Display;
		}
	}

	/// <summary>His stage files that are not scenes of their own.</summary>
	private static readonly string[] Skipped = new string[3] { "STG001_VB_01", "STG007", "STG017_QF" };

	internal static IReadOnlyList<Stage> Read(string appFolder)
	{
		string master = Path.GetFullPath(Path.Combine(appFolder, "..", "Server", "data", "fgo-master-11.51", "stage_asset", "arms_mst_stage_asset.bin"));
		string stageFolder = Path.Combine(appFolder, "rom", "stage");
		if (!File.Exists(master) || !Directory.Exists(stageFolder))
		{
			return Array.Empty<Stage>();
		}
		Dictionary<string, string> table = File.ReadLines(master).Select((string line) => line.Split('=', 2)).Where((string[] pair) => pair.Length == 2).GroupBy((string[] pair) => pair[0].Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary((IGrouping<string, string[]> group) => group.Key, (IGrouping<string, string[]> group) => group.Last()[1].Trim(), StringComparer.OrdinalIgnoreCase);
		int count = int.TryParse(table.GetValueOrDefault("data_list.length"), out int length) ? length : 0;
		HashSet<string> files = Directory.EnumerateFiles(stageFolder, "*.info").Select((string file) => Path.GetFileNameWithoutExtension(file).ToUpperInvariant()).Where((string code) => !Skipped.Contains(code)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> english = EnglishNames(appFolder);
		List<Stage> stages = new List<Stage>();
		for (int i = 0; i < count; i++)
		{
			string prefix = $"stage_asset.{i}.";
			string code = table.GetValueOrDefault(prefix + "stage_name", "");
			if (code.Length == 0 || !files.Contains(code) || !int.TryParse(table.GetValueOrDefault(prefix + "stage_id"), out int id) || id < 2001 || id > 2030)
			{
				continue;
			}
			string? name = english.GetValueOrDefault(table.GetValueOrDefault(prefix + "disp_name", ""));
			stages.Add(new Stage(id, code, name ?? $"Stage {id}", name != null));
		}
		return stages.OrderBy((Stage stage) => stage.StageId).ToList();
	}

	/// <summary>The English set's names of the game's own texts, by their Japanese; none when the set is missing or unreadable.</summary>
	private static Dictionary<string, string> EnglishNames(string appFolder)
	{
		Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
		string path = Path.Combine(appFolder, EnglishSet.Folder, "server-display-text.json");
		try
		{
			if (!File.Exists(path))
			{
				return names;
			}
			using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
			if (!document.RootElement.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
			{
				return names;
			}
			foreach (JsonElement entry in entries.EnumerateArray())
			{
				if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("scope", out JsonElement scope) && scope.ValueEquals("server.name") && entry.TryGetProperty("source", out JsonElement source) && entry.TryGetProperty("translation", out JsonElement translation) && source.ValueKind == JsonValueKind.String && translation.ValueKind == JsonValueKind.String)
				{
					names.TryAdd(source.GetString()!, translation.GetString()!);
				}
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
		{
		}
		return names;
	}
}
