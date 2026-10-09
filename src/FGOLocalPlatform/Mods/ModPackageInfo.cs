namespace FGOLocalPlatform;

public sealed class ModPackageInfo
{
	public int SchemaVersion { get; set; }

	public string Component { get; set; } = "";

	public string GameVersion { get; set; } = "";

	public string Title { get; set; } = "";

	public string Author { get; set; } = "";

	public string Version { get; set; } = "";

	public string BuildId { get; set; } = "";
}
