using System;
using System.Collections.Generic;
using System.Linq;

namespace FGOLocalPlatform;

/// <summary>
/// Keys or pad buttons held together as one binding. A key's code is its virtual key; a pad button's code is
/// <see cref="PadCode"/> plus its bit in the XInput button mask, where bits 16 and 17 are the left and right
/// triggers as the stock hook reads them.
/// </summary>
public sealed class Chord : IEquatable<Chord>
{
	public const int PadCode = 0x100;

	private static readonly (string Name, int Key)[] KeyNames = new(string, int)[]
	{
		("Shift", 0x10), ("Ctrl", 0x11), ("Alt", 0x12), ("LShift", 0xA0), ("RShift", 0xA1), ("LCtrl", 0xA2),
		("RCtrl", 0xA3), ("LAlt", 0xA4), ("RAlt", 0xA5), ("LWin", 0x5B), ("RWin", 0x5C), ("MiddleMouse", 0x04),
		("Mouse4", 0x05), ("Mouse5", 0x06), ("Backspace", 0x08), ("Tab", 0x09), ("Enter", 0x0D), ("Esc", 0x1B),
		("Space", 0x20), ("PageUp", 0x21), ("PageDown", 0x22), ("End", 0x23), ("Home", 0x24), ("Left", 0x25),
		("Up", 0x26), ("Right", 0x27), ("Down", 0x28), ("Insert", 0x2D), ("Delete", 0x2E)
	};

	private static readonly (string Name, string Display, string DualSense, int Bit)[] PadButtons = new(string, string, string, int)[]
	{
		("Up", "D-Pad Up", "D-Pad Up", 0), ("Down", "D-Pad Down", "D-Pad Down", 1), ("Left", "D-Pad Left", "D-Pad Left", 2),
		("Right", "D-Pad Right", "D-Pad Right", 3), ("Start", "Start", "Options", 4), ("Back", "Back", "Create", 5),
		("L3", "Left Stick Click", "L3", 6), ("R3", "Right Stick Click", "R3", 7), ("LB", "LB", "L1", 8), ("RB", "RB", "R1", 9),
		("A", "A", "×", 12), ("B", "B", "○", 13), ("X", "X", "□", 14), ("Y", "Y", "△", 15), ("LT", "LT", "L2", 16), ("RT", "RT", "R2", 17)
	};

	private readonly int[] codes;

	private Chord(IEnumerable<int> codes)
	{
		this.codes = codes.OrderBy((int code) => IsModifier(code) ? code - 0x1000 : code).ToArray();
	}

	public IReadOnlyList<int> Codes => codes;

	public bool IsPad => codes[0] >= PadCode;

	public uint PadMask => codes.Where((int code) => code >= PadCode).Aggregate(0u, (uint mask, int code) => mask | (1u << (code - PadCode)));

	public static Chord? FromPadMask(uint mask)
	{
		List<int> bits = Enumerable.Range(0, 32).Where((int bit) => (mask & (1u << bit)) != 0).ToList();
		if (bits.Count == 0 || bits.Any((int bit) => !PadButtons.Any((pad) => pad.Bit == bit)))
		{
			return null;
		}
		return new Chord(bits.Select((int bit) => PadCode + bit));
	}

	public static Chord? FromKeys(IEnumerable<int> keys)
	{
		List<int> list = keys.Distinct().ToList();
		return (list.Count == 0 || list.Any((int key) => key < 1 || key > 0xFE)) ? null : new Chord(list);
	}

	/// <summary>A chord in the bindings file's words, such as <c>Ctrl+Space</c> or <c>0x70</c>; null for anything else.</summary>
	public static Chord? ParseKeys(string text)
	{
		List<int> keys = new List<int>();
		foreach (string part in text.Split('+'))
		{
			string name = part.Trim();
			int key = KeyNames.FirstOrDefault(((string Name, int Key) known) => known.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
			if (key == 0)
			{
				key = KeyCode(name) ?? 0;
			}
			if (key < 1 || key > 0xFE || keys.Contains(key))
			{
				return null;
			}
			keys.Add(key);
		}
		return new Chord(keys);
	}

	/// <summary>A pad chord in the bindings file's words, such as <c>LB+A</c> or the mask <c>0x3000</c>; null for anything else.</summary>
	public static Chord? ParsePad(string text)
	{
		uint mask = 0;
		foreach (string part in text.Split('+'))
		{
			string name = part.Trim();
			int bit = Array.FindIndex(PadButtons, ((string Name, string Display, string DualSense, int Bit) pad) => pad.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
			uint add = (bit >= 0) ? (1u << PadButtons[bit].Bit) : (uint)(IniText.Number(name) ?? 0);
			if (add == 0 || (mask & add) != 0)
			{
				return null;
			}
			mask |= add;
		}
		return FromPadMask(mask);
	}

	/// <summary>The chord in the bindings file's words, which <see cref="ParseKeys"/> and <see cref="ParsePad"/> read back.</summary>
	public string Format()
	{
		return string.Join("+", codes.Select((int code) => (code >= PadCode) ? PadButton(code).Name : (KeyNames.FirstOrDefault(((string Name, int Key) known) => known.Key == code).Name ?? KeyText(code))));
	}

	/// <summary>The chord as the Input card names its keys and buttons.</summary>
	public string Display(bool dualSense = false)
	{
		return string.Join(" + ", codes.Select((int code) => Name(code, dualSense)));
	}

	public static string Name(int code, bool dualSense = false)
	{
		if (code < PadCode)
		{
			return KeyBindingButton.KeyName(code);
		}
		var pad = PadButton(code);
		return dualSense ? pad.DualSense : pad.Display;
	}

	public bool Contains(Chord other)
	{
		return other.codes.All(codes.Contains);
	}

	public bool Equals(Chord? other)
	{
		return other != null && codes.SequenceEqual(other.codes);
	}

	public override bool Equals(object? obj)
	{
		return Equals(obj as Chord);
	}

	public override int GetHashCode()
	{
		return codes.Aggregate(17, (int hash, int code) => hash * 31 + code);
	}

	public override string ToString()
	{
		return Format();
	}

	private static bool IsModifier(int code)
	{
		return code is 0x10 or 0x11 or 0x12 or (>= 0xA0 and <= 0xA5) or 0x5B or 0x5C;
	}

	private static (string Name, string Display, string DualSense, int Bit) PadButton(int code)
	{
		return PadButtons.First(((string Name, string Display, string DualSense, int Bit) pad) => pad.Bit == code - PadCode);
	}

	private static int? KeyCode(string name)
	{
		if (name.Length == 1 && ((name[0] >= '0' && name[0] <= '9') || (char.ToUpperInvariant(name[0]) >= 'A' && char.ToUpperInvariant(name[0]) <= 'Z')))
		{
			return char.ToUpperInvariant(name[0]);
		}
		if (name.Length > 1 && (name[0] == 'F' || name[0] == 'f') && int.TryParse(name[1..], out int function) && function >= 1 && function <= 24)
		{
			return 0x6F + function;
		}
		if (name.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase) && name.Length == 7 && name[6] >= '0' && name[6] <= '9')
		{
			return 0x60 + (name[6] - '0');
		}
		return name.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? IniText.Number(name) : null;
	}

	private static string KeyText(int key)
	{
		if ((key >= 0x30 && key <= 0x39) || (key >= 0x41 && key <= 0x5A))
		{
			return ((char)key).ToString();
		}
		if (key >= 0x70 && key <= 0x87)
		{
			return $"F{key - 0x6F}";
		}
		if (key >= 0x60 && key <= 0x69)
		{
			return $"Numpad{key - 0x60}";
		}
		return $"0x{key:X2}";
	}
}
