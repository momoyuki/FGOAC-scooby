namespace FgoArcadeStudio.Core;

public sealed class FgoTexture
{
	public string Name { get; init; } = "";

	public int Width { get; init; }

	public int Height { get; init; }

	public int FormatId { get; init; }

	public byte[] BgraPixels { get; init; } = System.Array.Empty<byte>();

	public bool IsCubeMap { get; init; }

	public override string ToString()
	{
		return $"{Name}  {Width}×{Height}  FMT {FormatId}";
	}
}
