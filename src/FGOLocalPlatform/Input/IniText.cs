using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FGOLocalPlatform;

/// <summary>
/// Reads the Runtime ini and the bindings file the way GetPrivateProfileString does: sections and keys
/// ignore case, a repeated section adds to the first, and the first value of a key wins.
/// </summary>
internal static class IniText
{
	public static Dictionary<string, Dictionary<string, string>> Parse(string text, Action<string>? junk = null)
	{
		Dictionary<string, Dictionary<string, string>> sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string>? current = null;
		foreach (string raw in text.Split('\n'))
		{
			string line = raw.Trim();
			if (line.Length == 0 || line[0] == ';' || line[0] == '#')
			{
				continue;
			}
			if (line[0] == '[' && line[^1] == ']')
			{
				string name = line[1..^1].Trim();
				if (!sections.TryGetValue(name, out current))
				{
					current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					sections[name] = current;
				}
				continue;
			}
			int equals = line.IndexOf('=');
			if (current == null || equals <= 0)
			{
				junk?.Invoke(line);
				continue;
			}
			current.TryAdd(line[..equals].Trim(), line[(equals + 1)..].Trim());
		}
		return sections;
	}

	/// <summary>
	/// The text with <paramref name="key"/> in <paramref name="section"/> set to <paramref name="value"/>, as the launch
	/// script's Set-IniValue does it: the first such key line gets the new value and keeps its line end, a missing key
	/// goes first in the section, a missing section goes last; every other line stays as it was.
	/// </summary>
	public static string Set(string text, string section, string key, string value)
	{
		Match header = Regex.Match(text, "(?m)^\\[" + Regex.Escape(section) + "\\]\\s*$");
		if (!header.Success)
		{
			return text.TrimEnd() + "\r\n\r\n[" + section + "]\r\n" + key + "=" + value + "\r\n";
		}
		int start = header.Index + header.Length;
		Match next = new Regex("(?m)^\\[[^\\]]+\\]\\s*$").Match(text, start);
		int end = next.Success ? next.Index : text.Length;
		string body = text.Substring(start, end - start);
		Match line = Regex.Match(body, "(?m)^([ \\t]*" + Regex.Escape(key) + "[ \\t]*=)[^\\r\\n]*");
		body = line.Success ? (body.Substring(0, line.Index) + line.Groups[1].Value + value + body.Substring(line.Index + line.Length)) : ("\r\n" + key + "=" + value + body);
		return text.Substring(0, start) + body + text.Substring(end);
	}

	public static IReadOnlyDictionary<string, string> Section(this Dictionary<string, Dictionary<string, string>> ini, string name)
	{
		return ini.TryGetValue(name, out Dictionary<string, string>? section) ? section : new Dictionary<string, string>();
	}

	/// <summary>The key's number, decimal or 0x hex, or <paramref name="fallback"/> when it is missing or not a number.</summary>
	public static int Int(this Dictionary<string, Dictionary<string, string>> ini, string section, string key, int fallback)
	{
		return ini.Section(section).TryGetValue(key, out string? text) ? (Number(text) ?? fallback) : fallback;
	}

	public static int? Number(string text)
	{
		text = text.Trim();
		if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return int.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int hex) ? hex : null;
		}
		return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : null;
	}
}
