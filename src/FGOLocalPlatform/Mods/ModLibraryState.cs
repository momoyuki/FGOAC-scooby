using System.Collections.Generic;

namespace FGOLocalPlatform;

public sealed class ModLibraryState
{
	public int SchemaVersion { get; set; } = 1;

	public Dictionary<string, string> Selected { get; set; } = new Dictionary<string, string>();
}
