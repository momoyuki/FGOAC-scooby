using System.Collections.Generic;

namespace FGOLocalPlatform;

public sealed class ModLoadingSettings
{
	public List<ModLoadingEntry> Entries { get; set; } = new List<ModLoadingEntry>();

	public string Folder { get; set; } = "";
}
