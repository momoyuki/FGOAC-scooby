using System.Collections.Generic;

namespace FGOLocalPlatform;

public sealed class ModMasterSettings
{
	public bool Enabled { get; set; }

	public string Title { get; set; } = "";

	public string Author { get; set; } = "";

	public string Version { get; set; } = "1.0";

	public string Description { get; set; } = "";

	public string Folder { get; set; } = "";

	public Dictionary<string, string> Images { get; set; } = new Dictionary<string, string>();
}
