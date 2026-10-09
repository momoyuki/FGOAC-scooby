using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// The frame rate and sync switches of Cloud23333's 1.10 update, ported from his launcher's Display page: high frame
/// rate timing, VSync and the AMD anti-tearing mode. They live in the graphics object of fgo-launcher.json under his
/// keys highFpsV2, vsync and amdPresentSync, which his launch script passes to his hook as FGO_HIGH_FPS_V2, FGO_VSYNC
/// and FGO_AMD_PRESENT_SYNC; without high frame rate timing it holds the game at 60 FPS. Only his hook reads them.
/// </summary>
public sealed record FrameSync(bool HighFpsV2, bool VSync, bool AmdPresentSync)
{
	/// <summary>The switches in <paramref name="graphics"/> as his launcher reads them: each off unless true.</summary>
	public static FrameSync Read(JsonObject? graphics)
	{
		return new FrameSync(IsTrue(graphics?["highFpsV2"]), IsTrue(graphics?["vsync"]), IsTrue(graphics?["amdPresentSync"]));
	}

	/// <summary>Sets his keys in <paramref name="graphics"/>; every other key stays.</summary>
	public void Write(JsonObject graphics)
	{
		graphics["highFpsV2"] = HighFpsV2;
		graphics["vsync"] = VSync;
		graphics["amdPresentSync"] = AmdPresentSync;
	}

	private static bool IsTrue(JsonNode? node)
	{
		return node is JsonValue value && value.TryGetValue(out bool on) && on;
	}
}
