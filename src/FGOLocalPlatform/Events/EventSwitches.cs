using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FGOLocalPlatform;

/// <summary>
/// The event switches of Cloud23333's 1.10 local server, ported from his launcher: the events in his catalogue
/// Server\artemis\titles\fgo\activity_display_catalog.json, switched off in hidden_ids and ordered in ordered_ids of
/// Server\state\activity-display.json, the file his server reads at every sortie. Ids and keys this launcher does not
/// know are kept, so both launchers share the file.
/// </summary>
internal static class EventSwitches
{
	internal sealed class Entry
	{
		[JsonPropertyName("id")]
		public int Id { get; set; }

		[JsonPropertyName("name")]
		public string Name { get; set; } = "";

		[JsonPropertyName("edition")]
		public string Edition { get; set; } = "";

		[JsonPropertyName("category")]
		public string Category { get; set; } = "";

		[JsonPropertyName("release_order")]
		public int ReleaseOrder { get; set; }

		[JsonPropertyName("phase")]
		public string Phase { get; set; } = "";
	}

	/// <summary>A heading of the list: an edition, or a kind inside one (Category set), and how many events it holds.</summary>
	internal sealed record Group(string Edition, string? Category, string Title, int Count);

	internal static readonly string[] Editions = new string[3] { "original", "revival", "permanent" };

	internal static readonly string[] Categories = new string[7] { "story", "coop", "time_trial", "box", "collection", "campaign", "pvp" };

	private static readonly string[] EditionTitles = new string[3] { "Original events", "Reruns", "Permanent extra episodes" };

	private static readonly string[] CategoryTitles = new string[7] { "Story and quests", "Co-op and raids", "Time trials", "Box events", "Collection and exchange", "Campaigns", "Versus and tournaments" };

	/// <summary>English titles by event id, from the launcher's English text; an event without one keeps its Japanese title.</summary>
	private static readonly Lazy<Dictionary<int, string>> EnglishNames = new Lazy<Dictionary<int, string>>(delegate
	{
		using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FGOLocalPlatform.EventNames.json");
		if (stream == null)
		{
			return new Dictionary<int, string>();
		}
		return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)?.ToDictionary((KeyValuePair<string, string> pair) => int.Parse(pair.Key), (KeyValuePair<string, string> pair) => pair.Value) ?? new Dictionary<int, string>();
	});

	internal static string CatalogPath(string serverRoot)
	{
		return Path.Combine(serverRoot, "artemis", "titles", "fgo", "activity_display_catalog.json");
	}

	internal static string SettingsPath(string serverRoot)
	{
		return Path.Combine(serverRoot, "state", "activity-display.json");
	}

	/// <summary>True where his server has the switches: his catalogue is installed.</summary>
	internal static bool Offered(string serverRoot)
	{
		return File.Exists(CatalogPath(serverRoot));
	}

	/// <summary>His catalogue in release order; throws InvalidDataException for another schema or a broken entry.</summary>
	internal static Entry[] ReadCatalog(string path)
	{
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
		if (document.RootElement.GetProperty("schema_version").GetInt32() != 1)
		{
			throw new InvalidDataException("Unsupported event catalogue version");
		}
		Entry[] entries = JsonSerializer.Deserialize<Entry[]>(document.RootElement.GetProperty("events").GetRawText()) ?? Array.Empty<Entry>();
		if (entries.Length == 0 || entries.Select((Entry entry) => entry.Id).Distinct().Count() != entries.Length || entries.Any((Entry entry) => entry.Id < 0 || !Editions.Contains(entry.Edition) || !Categories.Contains(entry.Category) || string.IsNullOrWhiteSpace(entry.Name)))
		{
			throw new InvalidDataException("Invalid event catalogue");
		}
		return entries.OrderBy((Entry entry) => entry.ReleaseOrder).ThenBy((Entry entry) => entry.Id).ToArray();
	}

	/// <summary>The settings file; every event on when it is missing; throws InvalidDataException for one his server would refuse.</summary>
	internal static JsonObject Read(string path)
	{
		if (!File.Exists(path))
		{
			return new JsonObject
			{
				["schema_version"] = 1,
				["hidden_ids"] = new JsonArray()
			};
		}
		JsonObject document = (JsonNode.Parse(File.ReadAllText(path)) as JsonObject) ?? throw new InvalidDataException("Invalid event settings");
		if (document["schema_version"]?.GetValue<int>() != 1 || !(document["hidden_ids"] is JsonArray hidden) || hidden.Any((JsonNode? id) => id == null || id.GetValue<int>() < 0))
		{
			throw new InvalidDataException("Invalid event settings");
		}
		if (document.ContainsKey("ordered_ids") && (!(document["ordered_ids"] is JsonArray order) || order.Any((JsonNode? id) => id == null || id.GetValue<int>() < 0) || order.Select((JsonNode? id) => id!.GetValue<int>()).Distinct().Count() != order.Count))
		{
			throw new InvalidDataException("Invalid event order");
		}
		return document;
	}

	internal static HashSet<int> Hidden(JsonObject document)
	{
		return document["hidden_ids"]!.AsArray().Select((JsonNode? id) => id!.GetValue<int>()).ToHashSet();
	}

	internal static int[] Order(JsonObject document)
	{
		return (document["ordered_ids"] is JsonArray order) ? order.Select((JsonNode? id) => id!.GetValue<int>()).ToArray() : Array.Empty<int>();
	}

	/// <summary>The entries in the saved order, then in release order.</summary>
	internal static Entry[] Sort(IEnumerable<Entry> entries, IEnumerable<int> order)
	{
		Dictionary<int, int> ranks = order.Select((int id, int index) => (id, index)).ToDictionary(((int id, int index) pair) => pair.id, ((int id, int index) pair) => pair.index);
		return entries.OrderBy((Entry entry) => ranks.TryGetValue(entry.Id, out int rank) ? rank : ranks.Count).ThenBy((Entry entry) => entry.ReleaseOrder).ThenBy((Entry entry) => entry.Id).ToArray();
	}

	/// <summary>The entries grouped as the list shows them: by edition, then by kind, each keeping the order it has.</summary>
	internal static Entry[] Grouped(IEnumerable<Entry> entries)
	{
		return entries.OrderBy((Entry entry) => Array.IndexOf(Editions, entry.Edition)).ThenBy((Entry entry) => Array.IndexOf(Categories, entry.Category)).ToArray();
	}

	/// <summary>The headings of the list in order, each edition before its kinds.</summary>
	internal static Group[] Groups(IReadOnlyList<Entry> entries)
	{
		List<Group> groups = new List<Group>();
		foreach (string edition in Editions)
		{
			int inEdition = entries.Count((Entry entry) => entry.Edition == edition);
			if (inEdition == 0)
			{
				continue;
			}
			groups.Add(new Group(edition, null, EditionTitles[Array.IndexOf(Editions, edition)], inEdition));
			foreach (string category in Categories)
			{
				int inCategory = entries.Count((Entry entry) => entry.Edition == edition && entry.Category == category);
				if (inCategory > 0)
				{
					groups.Add(new Group(edition, category, CategoryTitles[Array.IndexOf(Categories, category)], inCategory));
				}
			}
		}
		return groups.ToArray();
	}

	/// <summary>The event's title, in English where known, with Part 1 or Part 2 of a split event and Run N of a title that returns.</summary>
	internal static string Label(Entry entry, IReadOnlyList<Entry> entries)
	{
		string label = EnglishNames.Value.TryGetValue(entry.Id, out string? english) ? english : entry.Name;
		if (entry.Phase.Length > 0)
		{
			return label + ((entry.Phase == "first") ? " - Part 1" : " - Part 2");
		}
		if (entries.Count((Entry other) => other.Name == entry.Name) > 1)
		{
			return label + " - Run " + entries.Count((Entry other) => other.Name == entry.Name && other.ReleaseOrder <= entry.ReleaseOrder);
		}
		return label;
	}

	/// <summary>The entries with one moved to the top, up, down or to the bottom of its own edition and kind.</summary>
	internal static Entry[] Move(IReadOnlyList<Entry> entries, int id, string where)
	{
		Entry[] grouped = Grouped(entries);
		int from = Array.FindIndex(grouped, (Entry entry) => entry.Id == id);
		if (from < 0)
		{
			return grouped;
		}
		int[] span = Enumerable.Range(0, grouped.Length).Where((int index) => grouped[index].Edition == grouped[from].Edition && grouped[index].Category == grouped[from].Category).ToArray();
		int to = where switch
		{
			"top" => span[0],
			"up" => Math.Max(span[0], from - 1),
			"down" => Math.Min(span[^1], from + 1),
			"bottom" => span[^1],
			_ => from,
		};
		List<Entry> moved = grouped.ToList();
		Entry item = moved[from];
		moved.RemoveAt(from);
		moved.Insert(to, item);
		return moved.ToArray();
	}

	/// <summary>
	/// Writes the switches: the catalogue's events not in <paramref name="visible"/> into hidden_ids beside the hidden ids it
	/// does not know, and <paramref name="order"/> with the ordered ids it does not know after it into ordered_ids, or no
	/// ordered_ids for release order. Every other key stays.
	/// </summary>
	internal static void Save(string path, IEnumerable<Entry> entries, ISet<int> visible, IEnumerable<int>? order)
	{
		JsonObject document = Read(path);
		HashSet<int> hidden = Hidden(document);
		Entry[] known = entries.ToArray();
		foreach (Entry entry in known)
		{
			if (visible.Contains(entry.Id))
			{
				hidden.Remove(entry.Id);
			}
			else
			{
				hidden.Add(entry.Id);
			}
		}
		document["hidden_ids"] = new JsonArray(hidden.OrderBy((int id) => id).Select((int id) => (JsonNode)JsonValue.Create(id)).ToArray());
		HashSet<int> ids = known.Select((Entry entry) => entry.Id).ToHashSet();
		if (order == null)
		{
			document.Remove("ordered_ids");
		}
		else
		{
			document["ordered_ids"] = new JsonArray(order.Concat(Order(document).Where((int id) => !ids.Contains(id))).Distinct().Select((int id) => (JsonNode)JsonValue.Create(id)).ToArray());
		}
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		AtomicFile.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}) + "\n");
	}
}
