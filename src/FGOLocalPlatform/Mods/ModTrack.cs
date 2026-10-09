namespace FGOLocalPlatform;

public sealed class ModTrack
{
	public string Pool { get; set; } = "battle_all";

	public string Title { get; set; } = "";

	public string File { get; set; } = "";

	public double? LoopBegin { get; set; }

	public double? LoopEnd { get; set; }

	public bool AutoLoudness { get; set; }

	public double GainDb { get; set; }

	public string LoopAnalysis { get; set; } = "";
}
