using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FGOLocalPlatform;

/// <summary>
/// The keys, pad buttons and controller the stock hook reads for its own game actions, as the Input card
/// saves them in the ini; a key the ini leaves out has the stock hook's default (hook MAP section 3).
/// </summary>
public sealed class StockControls
{
	private static readonly (string Key, string Label, int Default)[] KeyActions = new(string, string, int)[]
	{
		("attack", "Attack", 0x20), ("dash", "Dash", 0xA0), ("target", "Switch Lock-on", 0x4A), ("camera", "Center Camera", 0x4B),
		("np", "Noble Phantasm", 0x4C), ("up", "Move Up", 0x57), ("down", "Move Down", 0x53), ("left", "Move Left", 0x41),
		("right", "Move Right", 0x44), ("skill1", "Servant Skill 1", 0x31), ("skill2", "Servant Skill 2", 0x32),
		("skill3", "Servant Skill 3", 0x33), ("skillTarget1", "Skill Target 1", 0x31), ("skillTarget2", "Skill Target 2", 0x32),
		("skillTarget3", "Skill Target 3", 0x33), ("skillTargetCancel", "Skill Target Cancel", 0x1B)
	};

	private static readonly (string Key, string Label, int Default)[] PadActions = new(string, string, int)[]
	{
		("attack", "Attack", 0x3000), ("dash", "Dash", 0x10000), ("target", "Switch Lock-on", 0x100), ("np", "Noble Phantasm", 0xC000),
		("camera", "Center Camera", 0x40), ("skill1", "Servant Skill 1", 0), ("skill2", "Servant Skill 2", 0),
		("skill3", "Servant Skill 3", 0), ("skillTarget1", "Skill Target 1", 0), ("skillTarget2", "Skill Target 2", 0),
		("skillTarget3", "Skill Target 3", 0), ("skillTargetCancel", "Skill Target Cancel", 0x2000)
	};

	/// <summary>Card selection on a pad in Cloud23333's 1.10 hook, on while [xinput] cardNavigation is not 0.</summary>
	private static readonly (string Key, string Label, int Default)[] CardNavigation = new(string, string, int)[]
	{
		("cardLeft", "Card Left", 0x100), ("cardRight", "Card Right", 0x200), ("cardConfirm", "Card Confirm", 0x1000), ("cardUndo", "Card Undo", 0x2000)
	};

	public const int LeftMouseButton = 0x01;

	private void AddPad(uint mask, string label)
	{
		if (mask != 0)
		{
			pad.Add((mask, label));
		}
	}

	private readonly List<(int Key, string Label)> keys = new List<(int, string)>();

	private readonly List<(uint Mask, string Label)> pad = new List<(uint, string)>();

	/// <summary>True when Cloud23333's keyboard free camera is on: it drives the battle camera and reads the mouse buttons as battle actions.</summary>
	public bool KeyboardFreeCamera { get; private set; }

	/// <summary>Why the bindings tap nothing this session, in one sentence; null when they can tap.</summary>
	public string? TapsOff => KeyboardFreeCamera ? "Cloud23333's keyboard battle mode is on and reads the mouse buttons as battle actions, so the bindings tap nothing." : null;

	/// <summary>What moves the character on a pad: 0 the left stick, 1 the right stick, 2 the D-pad.</summary>
	public int Movement { get; private set; }

	public int ControllerIndex { get; private set; }

	/// <summary>True when the stock hook reads the pad through the DualSense runtime in the App folder.</summary>
	public bool DualSense { get; private set; }

	/// <summary>The key the stock hook turns into touch, [touch] clickKey: 0 to 254, else the left mouse button.</summary>
	public int TouchKey { get; private set; }

	/// <summary>The ini at iniPath as the hook of this game folder reads it: on any build but 11.00 his 1.10 hook.</summary>
	public static StockControls Read(string iniPath)
	{
		return Parse(File.Exists(iniPath) ? File.ReadAllText(iniPath) : "", !GameBuild.Is1100(Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."))));
	}

	public static StockControls Parse(string iniText)
	{
		return Parse(iniText, hisHook: false);
	}

	/// <summary>The ini as a hook reads it; <paramref name="hisHook"/>, Cloud23333's 1.10 hook, takes a missing [xinput] cardNavigation as on.</summary>
	public static StockControls Parse(string iniText, bool hisHook)
	{
		Dictionary<string, Dictionary<string, string>> ini = IniText.Parse(iniText);
		int touch = ini.Int("touch", "clickKey", LeftMouseButton);
		StockControls stock = new StockControls
		{
			Movement = ini.Int("xinput", "movement", 0),
			ControllerIndex = System.Math.Clamp(ini.Int("xinput", "controllerIndex", 0), 0, 3),
			DualSense = ini.Int("dualsense", "enabled", 0) == 1,
			TouchKey = (touch >= 0 && touch <= 0xFE) ? touch : LeftMouseButton
		};
		foreach (var action in KeyActions)
		{
			int key = ini.Int("keyboard", action.Key, action.Default);
			if (key > 0 && key <= 0xFF)
			{
				stock.keys.Add((key, action.Label));
			}
		}
		stock.keys.Add((stock.TouchKey, "Touch"));
		foreach (var action in PadActions)
		{
			uint mask = (uint)ini.Int("xinput", action.Key, action.Default);
			if (mask != 0)
			{
				stock.pad.Add((mask, action.Label));
			}
		}
		if (stock.Movement == 2)
		{
			stock.pad.Add((0xF, "Move"));
		}
		// Cloud23333's 1.10 hook reads these pad keys; his update adds them to the ini, and older hooks ignore them.
		if (ini.Int("xinput", "cardNavigation", hisHook ? 1 : 0) != 0)
		{
			foreach (var action in CardNavigation)
			{
				stock.AddPad((uint)ini.Int("xinput", action.Key, action.Default), action.Label);
			}
		}
		if (ini.Int("battleKeyboard", "enabled", 0) != 0)
		{
			stock.KeyboardFreeCamera = true;
		}
		if (ini.Section("xinput").ContainsKey("mouseToggle"))
		{
			stock.AddPad((uint)ini.Int("xinput", "mouseToggle", 0), "Pad Mouse");
		}
		if (ini.Int("xinput", "modernCamera", 0) != 0)
		{
			stock.AddPad((uint)ini.Int("xinput", "cameraLock", 0x80), "Camera Lock");
		}
		return stock;
	}

	/// <summary>The game action the stock hook fires for this key, or null; Shift, Ctrl and Alt match either side.</summary>
	public string? KeyAction(int key)
	{
		return keys.FirstOrDefault(((int Key, string Label) stock) => SameKey(stock.Key, key)).Label;
	}

	/// <summary>True when the stock hook reads the two key codes as one key: Shift, Ctrl and Alt match either side.</summary>
	public static bool SameKey(int a, int b)
	{
		return a == b || Generic(a) == b || a == Generic(b);
	}

	/// <summary>The game action the stock hook fires for this pad button (0x100 plus its bit), or null.</summary>
	public string? PadAction(int code)
	{
		uint bit = 1u << (code - Chord.PadCode);
		return pad.FirstOrDefault(((uint Mask, string Label) stock) => (stock.Mask & bit) != 0).Label;
	}

	private static int Generic(int key)
	{
		return key switch
		{
			0xA0 or 0xA1 => 0x10,
			0xA2 or 0xA3 => 0x11,
			0xA4 or 0xA5 => 0x12,
			_ => key,
		};
	}
}
