using System.Text.RegularExpressions;

namespace FGOLocalPlatform;

/// <summary>Removes a card's digits and a recovery key from text before it reaches a log or the Report.</summary>
public static class Redaction
{
	public const string Mark = "<redacted>";

	/// <summary>A run of twenty or more digits, or 32 base32 characters in eight groups of four with or without dashes.</summary>
	private static readonly Regex Secret = new Regex("(?<![0-9])[0-9]{20,}(?![0-9])|(?<![A-Za-z0-9])[A-Za-z2-7]{4}(?:-?[A-Za-z2-7]{4}){7}(?![A-Za-z0-9])", RegexOptions.CultureInvariant | RegexOptions.Compiled);

	public static string Apply(string text)
	{
		return Secret.Replace(text, Mark);
	}
}
