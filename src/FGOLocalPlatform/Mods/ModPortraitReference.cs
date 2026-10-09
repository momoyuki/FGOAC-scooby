namespace FGOLocalPlatform;

public sealed record ModPortraitReference(string Reference, string Name, string? LegacySlot = null)
{
	public override string ToString()
	{
		if (Reference.Length != 0)
		{
			return "[" + (ModPortraits.IsCustom(Reference) ? "Your portrait" : "Game portrait") + "] " + Name;
		}
		return Name;
	}
}
