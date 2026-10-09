using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// The scene settings of Cloud23333's 1.10 update, ported from his launcher's Display page: volumetric fog, reflections,
/// ultra-low quality, draw distance and the size and opacity of the treasure chests in battle. They live in the graphics
/// object of fgo-launcher.json under his key names, which his launch script turns into the game's environment at every
/// start; the draw distance also in [effects] view_distance of graphics-effects.ini, which his hook reads. Only his
/// hook reads any of them.
/// </summary>
public sealed record SceneGraphics(bool VolumeFog, bool Reflections, bool UltraLow, int ViewDistance, double TreasureScale, double TreasureOpacity)
{
	/// <summary>The draw distances his launcher offers, in percent of the game's own.</summary>
	public static readonly int[] DrawDistances = new int[4] { 100, 200, 400, 1000 };

	/// <summary>True on any build but 11.00: the game folder has his update, whose hook reads these settings.</summary>
	public static bool Offered(string installRoot)
	{
		return !GameBuild.Is1100(installRoot);
	}

	/// <summary>The settings in <paramref name="graphics"/> as his launcher reads them: fog and reflections on unless false, the rest at their defaults when missing or out of range.</summary>
	public static SceneGraphics Read(JsonObject? graphics)
	{
		int distance = Number(graphics?["viewDistance"]) is double saved && DrawDistances.Contains((int)saved) && saved == Math.Floor(saved) ? (int)saved : 100;
		return new SceneGraphics(!IsFalse(graphics?["volumeFog"]), !IsFalse(graphics?["reflections"]), IsTrue(graphics?["ultraLowQuality"]), distance, Math.Clamp(Number(graphics?["treasureScale"]) ?? 100.0, 0.0, 200.0), Math.Clamp(Number(graphics?["treasureOpacity"]) ?? 100.0, 0.0, 100.0));
	}

	/// <summary>Sets his keys in <paramref name="graphics"/>; every other key stays.</summary>
	public void Write(JsonObject graphics)
	{
		graphics["volumeFog"] = VolumeFog;
		graphics["reflections"] = Reflections;
		graphics["ultraLowQuality"] = UltraLow;
		graphics["viewDistance"] = DrawDistances.Contains(ViewDistance) ? ViewDistance : 100;
		graphics["treasureScale"] = Math.Round(Math.Clamp(TreasureScale, 0.0, 200.0), 1);
		graphics["treasureOpacity"] = Math.Round(Math.Clamp(TreasureOpacity, 0.0, 100.0), 1);
	}

	/// <summary>Sets view_distance in [effects] of graphics-effects.ini in the App folder, every other line kept.</summary>
	public void WriteDrawDistance(string appFolder)
	{
		string path = Path.Combine(appFolder, "graphics-effects.ini");
		string text = File.Exists(path) ? File.ReadAllText(path) : "";
		string value = (DrawDistances.Contains(ViewDistance) ? ViewDistance : 100).ToString(CultureInfo.InvariantCulture);
		AtomicFile.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes((text.Trim().Length == 0) ? ("[effects]\r\nview_distance=" + value + "\r\n") : IniText.Set(text, "effects", "view_distance", value)));
	}

	private static double? Number(JsonNode? node)
	{
		return (node is JsonValue value && value.TryGetValue(out double number)) ? number : null;
	}

	private static bool IsTrue(JsonNode? node)
	{
		return node is JsonValue value && value.TryGetValue(out bool on) && on;
	}

	private static bool IsFalse(JsonNode? node)
	{
		return node is JsonValue value && value.TryGetValue(out bool on) && !on;
	}
}
