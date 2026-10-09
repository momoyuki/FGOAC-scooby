using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FGOLocalPlatform;

/// <summary>One On-screen button: its name in the bindings file, its label, and its place on the Canvas.</summary>
public sealed record OnScreenButton(string Name, string Label, int X, int Y)
{
	/// <summary>False until a capture gives the button its place; a binding to it then taps nothing.</summary>
	public bool Placed => X >= 0 && Y >= 0;
}

/// <summary>
/// The bindings file, <c>DEVICE\runtime\bindings.ini</c>: the active Profile, the place of each On-screen button,
/// and the key and pad chord that taps it. No binding uses a key or pad button the stock hook reads for its own game
/// actions, or another binding's chord.
/// </summary>
public sealed class Bindings
{
	public const string KeyboardProfile = "keyboard+mouse";

	public const string PadProfile = "pad";

	private static readonly OnScreenButton[] KnownButtons = new OnScreenButton[]
	{
		new OnScreenButton("title", "Title screen: touch to start", 960, 540),
		new OnScreenButton("aimeYes", "Card check: Yes", 1160, 690),
		new OnScreenButton("gpCard", "GP purchase: 300 GP", 360, 585),
		new OnScreenButton("closePopup", "Popup: Close", 960, 850),
		new OnScreenButton("terminalHome", "Terminal: Home", -1, -1),
		new OnScreenButton("grailWar", "Terminal: Grail War", 1462, 520),
		new OnScreenButton("nationwideMatch", "Grail War: Nationwide Match", 660, 510),
		new OnScreenButton("start", "Grail War: Start", 1344, 827),
		new OnScreenButton("confirm", "Confirm", -1, -1),
		new OnScreenButton("back", "Back", -1, -1),
		new OnScreenButton("retry", "Quest: Retry", -1, -1),
		new OnScreenButton("nextQuest", "Quest: Next quest", -1, -1)
	};

	private readonly List<OnScreenButton> buttons = KnownButtons.ToList();

	private readonly Dictionary<string, Chord> keys = new Dictionary<string, Chord>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, Chord> pad = new Dictionary<string, Chord>(StringComparer.OrdinalIgnoreCase);

	public string Profile { get; set; } = KeyboardProfile;

	public IReadOnlyList<OnScreenButton> Buttons => buttons;

	public static string PathIn(string gameRoot)
	{
		return Path.GetFullPath(Path.Combine(gameRoot, "..", "DEVICE", "runtime", "bindings.ini"));
	}

	public Chord? Key(string button)
	{
		return keys.GetValueOrDefault(button);
	}

	public Chord? Pad(string button)
	{
		return pad.GetValueOrDefault(button);
	}

	/// <summary>Ties the chord to the button as its key or pad binding; null when it is bound, else the sentence that says why not.</summary>
	public string? Bind(string button, Chord chord, StockControls stock)
	{
		OnScreenButton? target = buttons.FirstOrDefault((OnScreenButton item) => item.Name.Equals(button, StringComparison.OrdinalIgnoreCase));
		if (target == null)
		{
			return button + " is not a button on the screen.";
		}
		Dictionary<string, Chord> map = chord.IsPad ? pad : keys;
		string? refusal = Refusal(chord, stock, "so it cannot also tap " + target.Label + ".");
		string? other = map.FirstOrDefault((KeyValuePair<string, Chord> item) => item.Value.Equals(chord) && !item.Key.Equals(target.Name, StringComparison.OrdinalIgnoreCase)).Key;
		if (refusal == null && other != null)
		{
			refusal = chord.Display(stock.DualSense) + " already taps " + Label(other) + ".";
		}
		if (refusal == null)
		{
			map[target.Name] = chord;
		}
		return refusal;
	}

	public void Unbind(string button, bool padBinding)
	{
		(padBinding ? pad : keys).Remove(button);
	}

	public static Bindings Load(string path, StockControls stock, Action<string> refused)
	{
		return Parse(File.Exists(path) ? File.ReadAllText(path) : "", stock, refused);
	}

	/// <summary>The bindings file for a check at edit time: no skipped line is reported, and a file that cannot be opened has no bindings.</summary>
	public static Bindings LoadQuietly(string path, StockControls stock)
	{
		try
		{
			return Load(path, stock, delegate
			{
			});
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			return Parse("", stock, delegate
			{
			});
		}
	}

	/// <summary>
	/// Why <paramref name="chord"/> cannot also be bound on another card, because a binding uses one of its keys or pad
	/// buttons, in one sentence that closes with <paramref name="ending"/>; null when none does.
	/// </summary>
	public string? Conflict(Chord chord, string ending, bool dualSense = false)
	{
		string name = chord.Display(dualSense);
		OnScreenButton? tapped = buttons.FirstOrDefault((OnScreenButton button) => (chord.IsPad ? Pad(button.Name) : Key(button.Name))?.Codes.Any((int code) => chord.Codes.Any((int other) => StockControls.SameKey(code, other))) == true);
		return (tapped == null) ? null : (name + " taps " + tapped.Label + " in Bindings, " + ending);
	}

	/// <summary>Reads the bindings file; each line it cannot use is skipped and told to <paramref name="refused"/> in one sentence.</summary>
	public static Bindings Parse(string text, StockControls stock, Action<string> refused)
	{
		Dictionary<string, Dictionary<string, string>> ini = IniText.Parse(text, (string line) => refused("The line '" + line + "' in the bindings file is not a setting and was skipped."));
		Bindings bindings = new Bindings();
		if (ini.Section("profile").TryGetValue("name", out string? profile))
		{
			if (profile == KeyboardProfile || profile == PadProfile)
			{
				bindings.Profile = profile;
			}
			else
			{
				refused("The profile '" + profile + "' is neither keyboard+mouse nor pad, so keyboard+mouse is used.");
			}
		}
		foreach (KeyValuePair<string, string> item in ini.Section("buttons"))
		{
			bindings.Place(item.Key, item.Value, refused);
		}
		bindings.ReadChords(ini.Section("keys"), stock, Chord.ParseKeys, "a key or a chord of keys", refused);
		bindings.ReadChords(ini.Section("pad"), stock, Chord.ParsePad, "a pad button or a chord of pad buttons", refused);
		return bindings;
	}

	/// <summary>The file's text: every button with its place or <c>unset</c>, then the bound chords in the buttons' order.</summary>
	public string Format()
	{
		StringBuilder text = new StringBuilder();
		text.Append("; The launcher's bindings. A place is a point on the game's 1920 x 1080 screen.\r\n");
		text.Append("[profile]\r\nname=").Append(Profile).Append("\r\n\r\n[buttons]\r\n");
		foreach (OnScreenButton button in buttons)
		{
			text.Append(button.Name).Append('=').Append(button.Placed ? $"{button.X},{button.Y}" : "unset").Append("\r\n");
		}
		foreach ((string section, Dictionary<string, Chord> map) in new (string, Dictionary<string, Chord>)[2] { ("keys", keys), ("pad", pad) })
		{
			text.Append("\r\n[").Append(section).Append("]\r\n");
			foreach (OnScreenButton button in buttons.Where((OnScreenButton item) => map.ContainsKey(item.Name)))
			{
				text.Append(button.Name).Append('=').Append(map[button.Name].Format()).Append("\r\n");
			}
		}
		return text.ToString();
	}

	public void Save(string path)
	{
		AtomicFile.WriteAllText(path, Format());
	}

	private string Label(string name)
	{
		return buttons.First((OnScreenButton item) => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Label;
	}

	private void Place(string name, string value, Action<string> refused)
	{
		int index = buttons.FindIndex((OnScreenButton item) => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
		OnScreenButton button = (index >= 0) ? buttons[index] : new OnScreenButton(name, name, -1, -1);
		string[] parts = value.Split(',');
		int? x = (parts.Length == 2) ? IniText.Number(parts[0]) : null;
		int? y = (parts.Length == 2) ? IniText.Number(parts[1]) : null;
		if (x >= 0 && x < 1920 && y >= 0 && y < 1080)
		{
			button = button with { X = x.Value, Y = y.Value };
		}
		else if (!value.Equals("unset", StringComparison.OrdinalIgnoreCase))
		{
			refused("'" + value + "' for " + button.Label + " is not a point on the 1920 x 1080 screen, so " + (button.Placed ? "its usual place is used." : "it has no place yet."));
		}
		if (index >= 0)
		{
			buttons[index] = button;
		}
		else
		{
			buttons.Add(button);
		}
	}

	private void ReadChords(IReadOnlyDictionary<string, string> section, StockControls stock, Func<string, Chord?> parse, string what, Action<string> refused)
	{
		foreach (KeyValuePair<string, string> item in section)
		{
			if (!buttons.Any((OnScreenButton button) => button.Name.Equals(item.Key, StringComparison.OrdinalIgnoreCase)))
			{
				refused(item.Key + " is not a button on the screen, so its binding was skipped.");
				continue;
			}
			if (item.Value.Length == 0)
			{
				continue;
			}
			Chord? chord = parse(item.Value);
			string? refusal = (chord == null) ? ("'" + item.Value + "' for " + Label(item.Key) + " is not " + what + " and was skipped.") : Bind(item.Key, chord, stock);
			if (refusal != null)
			{
				refused(refusal);
			}
		}
	}

	private static string? Refusal(Chord chord, StockControls stock, string ending)
	{
		foreach (int code in chord.Codes)
		{
			string? action = chord.IsPad ? stock.PadAction(code) : stock.KeyAction(code);
			if (action != null)
			{
				return Chord.Name(code, stock.DualSense) + " is " + action + " in Controls, " + ending;
			}
		}
		return null;
	}
}
