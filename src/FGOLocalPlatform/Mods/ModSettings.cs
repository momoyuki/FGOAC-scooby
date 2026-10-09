using System.Collections.ObjectModel;

namespace FGOLocalPlatform;

public sealed class ModSettings
{
	internal bool PortraitMigrationHandled { get; set; }

	public string Title { get; set; } = "";

	public string Author { get; set; } = "";

	public string Version { get; set; } = "1.0";

	public string Description { get; set; } = "";

	public bool BgmEnabled { get; set; }

	public bool DialogueEnabled { get; set; }

	public string DialogueFolder { get; set; } = "";

	public string PortraitLibraryPath { get; set; } = "";

	public ObservableCollection<ModTrack> Tracks { get; set; } = new ObservableCollection<ModTrack>();

	public ObservableCollection<ModDialogue> Dialogues { get; set; } = new ObservableCollection<ModDialogue>();
}
