namespace Hanhua;

public sealed record Sprite(int Page, string Name, int X, int Y, int W, int H, bool Rotated)
{
	public int Width
	{
		get
		{
			if (!Rotated)
			{
				return W;
			}
			return H;
		}
	}

	public int Height
	{
		get
		{
			if (!Rotated)
			{
				return H;
			}
			return W;
		}
	}

	public override string ToString()
	{
		return $"{Name}  ·  {Width}×{Height}";
	}
}
