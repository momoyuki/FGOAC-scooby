using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// The PC play switches, kept under "pcport" in the Settings file. The launcher patches the running game for all
/// but the story timer, which is the hook's, read from the runtime ini.
/// </summary>
public sealed record PcPortSwitches(bool NoContinueTimer, bool NoPopupTimers, bool NoGpPurchase, bool NoStoryTimer, bool NoMenuTimer)
{
	public const string NoStoryTimerKey = "noStoryTimer";

	public static PcPortSwitches Defaults { get; } = new PcPortSwitches(NoContinueTimer: true, NoPopupTimers: true, NoGpPurchase: false, NoStoryTimer: false, NoMenuTimer: false);

	/// <summary>Whether the switch of a [pcport] key is on; false for any other key.</summary>
	public bool IsOn(string key)
	{
		return key switch
		{
			CabinetPatches.NoContinueTimer => NoContinueTimer,
			CabinetPatches.NoPopupTimers => NoPopupTimers,
			CabinetPatches.NoGpPurchase => NoGpPurchase,
			NoStoryTimerKey => NoStoryTimer,
			CabinetPatches.NoMenuTimer => NoMenuTimer,
			_ => false
		};
	}

	/// <summary>The switches saved in the Settings file's object; a switch not saved has its default.</summary>
	public static PcPortSwitches Read(JsonObject settings)
	{
		JsonObject? saved = settings["pcport"] as JsonObject;
		return new PcPortSwitches(saved?[CabinetPatches.NoContinueTimer]?.GetValue<bool>() ?? Defaults.NoContinueTimer, saved?[CabinetPatches.NoPopupTimers]?.GetValue<bool>() ?? Defaults.NoPopupTimers, saved?[CabinetPatches.NoGpPurchase]?.GetValue<bool>() ?? Defaults.NoGpPurchase, saved?[NoStoryTimerKey]?.GetValue<bool>() ?? Defaults.NoStoryTimer, saved?[CabinetPatches.NoMenuTimer]?.GetValue<bool>() ?? Defaults.NoMenuTimer);
	}

	public void Write(JsonObject settings)
	{
		settings["pcport"] = new JsonObject
		{
			[CabinetPatches.NoContinueTimer] = NoContinueTimer,
			[CabinetPatches.NoPopupTimers] = NoPopupTimers,
			[CabinetPatches.NoGpPurchase] = NoGpPurchase,
			[NoStoryTimerKey] = NoStoryTimer,
			[CabinetPatches.NoMenuTimer] = NoMenuTimer
		};
	}

	/// <summary>The switches of the Settings file at path; the defaults when it is missing or cannot be read.</summary>
	public static PcPortSwitches Load(string path)
	{
		try
		{
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject settings)
			{
				return Read(settings);
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is InvalidOperationException || ex is FormatException)
		{
		}
		return Defaults;
	}
}
