namespace FGOLocalPlatform;

public sealed record MasterSprite(string Bundle, string Name, int Width, int Height, string Label)
{
	public override string ToString()
	{
		return Label + $" · {Width}×{Height}";
	}
}
