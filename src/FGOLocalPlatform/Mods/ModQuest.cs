using System;

namespace FGOLocalPlatform;

public sealed class ModQuest
{
	public int Id { get; }

	public string Name { get; }

	public string OriginalName { get; }

	public int[] BattleWaves { get; init; } = Array.Empty<int>();

	public bool Modified { get; set; }

	public ModQuest(int id, string name, string originalName)
	{
		Id = id;
		Name = name;
		OriginalName = originalName;
	}

	public override string ToString()
	{
		return (Modified ? "● " : "") + $"{Id} · {Name}";
	}
}
