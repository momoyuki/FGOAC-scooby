using System;
using System.IO;
using System.Text;

namespace FGOLocalPlatform;

/// <summary>
/// The stock hook, App\fgohook.dll, and the ini keys it names: an older build ignores the settings whose keys it does
/// not name, so those rows show a note.
/// </summary>
public static class StockHook
{
	/// <summary>
	/// True when the hook in appFolder holds key as a whole string, ASCII or UTF-16: followed by a NUL and not preceded
	/// by a letter, digit or _. True as well when there is no hook to read, so no note shows by mistake.
	/// </summary>
	public static bool Reads(string appFolder, string key)
	{
		byte[] image;
		try
		{
			image = File.ReadAllBytes(Path.Combine(appFolder, "fgohook.dll"));
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			return true;
		}
		return Holds(image, Encoding.ASCII.GetBytes(key + "\0"), 1) || Holds(image, Encoding.Unicode.GetBytes(key + "\0"), 2);
	}

	/// <summary>True when needle occurs in image after a character of width bytes that cannot be part of a key, or at its start.</summary>
	private static bool Holds(byte[] image, byte[] needle, int width)
	{
		int start = 0;
		while (true)
		{
			int found = image.AsSpan(start).IndexOf(needle);
			if (found < 0)
			{
				return false;
			}
			int at = start + found;
			if (at < width || !IsKeyCharacter(image, at - width, width))
			{
				return true;
			}
			start = at + 1;
		}
	}

	private static bool IsKeyCharacter(byte[] image, int at, int width)
	{
		char character = (char)image[at];
		return (width == 1 || image[at + 1] == 0) && character < 128 && (char.IsLetterOrDigit(character) || character == '_');
	}
}
