using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace FGOLocalPlatform;

public sealed class ModDialogue
{
	public int Quest { get; set; } = 1;

	[JsonIgnore]
	public string QuestName { get; set; } = "";

	public int Trigger { get; set; }

	public int TargetKind { get; set; }

	public bool Once { get; set; }

	public bool ContinueScreen { get; set; }

	public bool ContinueCustomImage { get; set; }

	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public ObservableCollection<ModLine> Lines { get; set; } = new ObservableCollection<ModLine>();

	public override string ToString()
	{
		return $"{Quest} · {QuestName}\n" + ((TargetKind == 1) ? "Event banner" : ((Trigger == 0) ? "Quest start" : ((Trigger == 4) ? "After battle" : $"Wave {Trigger}"))) + " · " + (Once ? "First time per account" : "Every time");
	}
}
