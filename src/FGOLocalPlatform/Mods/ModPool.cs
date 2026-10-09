namespace FGOLocalPlatform;

public sealed record ModPool(string Key, string Label, string Usage = "")
{
	public override string ToString()
	{
		return Label;
	}
}
