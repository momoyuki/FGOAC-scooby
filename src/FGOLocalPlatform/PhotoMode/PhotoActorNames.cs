using System;

namespace FGOLocalPlatform;

/// <summary>A character's label in the Face list, as Cloud23333's panel names it: the Servant, its model and its token.</summary>
internal static class PhotoActorNames
{
	/// <summary>The English card name of the Servant id in an SVT_ token, else of the model number.</summary>
	public static string Label(uint model, string token)
	{
		string text = token.ToUpperInvariant();
		uint value = (text.StartsWith("SVT_", StringComparison.Ordinal) && uint.TryParse(text.AsSpan(4), out uint result)) ? result : model;
		string name = CardNames.English(1, (int)value);
		return $"{((name.Length == 0) ? "Unlisted character" : name)} · Model {model} · {token}";
	}
}
