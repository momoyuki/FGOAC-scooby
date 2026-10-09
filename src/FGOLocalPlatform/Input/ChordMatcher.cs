using System;
using System.Collections.Generic;
using System.Linq;

namespace FGOLocalPlatform;

/// <summary>
/// The chord rule: which bindings fire as keys or pad buttons go down and up, so that one press never fires two
/// bindings. A binding fires on the poll its last key goes down, unless a longer binding that contains it is held
/// then. A binding that is part of a longer one waits for its release instead, and fires then only if no longer
/// binding sharing its keys fired meanwhile.
/// </summary>
public sealed class ChordMatcher
{
	private readonly Chord[] chords;

	private readonly bool[] partOfLonger;

	private readonly int[] longestFirst;

	private bool[] held;

	private readonly bool[] waiting;

	public ChordMatcher(IReadOnlyList<Chord> chords)
	{
		this.chords = chords.ToArray();
		held = new bool[this.chords.Length];
		waiting = new bool[this.chords.Length];
		partOfLonger = this.chords.Select((Chord chord) => this.chords.Any((Chord other) => IsLonger(other, chord))).ToArray();
		longestFirst = Enumerable.Range(0, this.chords.Length).OrderByDescending((int index) => this.chords[index].Codes.Count).ToArray();
	}

	/// <summary>The indexes of the chords that fire on this poll.</summary>
	public IReadOnlyList<int> Poll(Func<int, bool> isDown)
	{
		bool[] now = chords.Select((Chord chord) => chord.Codes.All(isDown)).ToArray();
		List<int> fired = new List<int>();
		for (int index = 0; index < chords.Length; index++)
		{
			if (waiting[index] && !now[index])
			{
				waiting[index] = false;
				fired.Add(index);
			}
		}
		foreach (int index in longestFirst)
		{
			if (!now[index] || held[index] || Enumerable.Range(0, chords.Length).Any((int other) => now[other] && IsLonger(chords[other], chords[index])))
			{
				continue;
			}
			if (partOfLonger[index])
			{
				waiting[index] = true;
				continue;
			}
			fired.Add(index);
			for (int other = 0; other < chords.Length; other++)
			{
				if (waiting[other] && chords[other].Codes.Any(chords[index].Codes.Contains))
				{
					waiting[other] = false;
				}
			}
		}
		held = now;
		return fired;
	}

	/// <summary>Forgets every waiting binding and takes what is held now as already pressed, so nothing fires until it is pressed again.</summary>
	public void Reset(Func<int, bool> isDown)
	{
		Array.Clear(waiting);
		held = chords.Select((Chord chord) => chord.Codes.All(isDown)).ToArray();
	}

	private static bool IsLonger(Chord longer, Chord shorter)
	{
		return longer.Codes.Count > shorter.Codes.Count && longer.Contains(shorter);
	}
}
