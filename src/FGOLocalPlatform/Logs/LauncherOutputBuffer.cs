using System;
using System.Collections.Generic;
using System.Text;

namespace FGOLocalPlatform;

/// <summary>
/// The launch script's output waiting for the Game log, ported from Cloud23333's 1.10 launcher: stdout and stderr each
/// in a queue of its own, capped at 192 KB and 64 KB of text, so a flood on stdout never drops an error line. The
/// oldest lines go first and are counted; Drain hands the rest back in the order written, at most 64 KB at a time.
/// </summary>
internal sealed class LauncherOutputBuffer
{
	private const int NormalLimit = 196608;

	private const int ImportantLimit = 65536;

	private const int EntryLimit = 32768;

	private const int DrainLimit = 65536;

	private const string Truncated = "[launcher] ...single line of output truncated...";

	private readonly object gate = new object();

	private readonly Queue<(long Sequence, string Text)> normal = new Queue<(long, string)>();

	private readonly Queue<(long Sequence, string Text)> important = new Queue<(long, string)>();

	private int normalCharacters;

	private int importantCharacters;

	private long sequence;

	private long normalDropped;

	private long importantDropped;

	private bool completed;

	internal void Reset()
	{
		lock (gate)
		{
			normal.Clear();
			important.Clear();
			normalCharacters = (importantCharacters = 0);
			sequence = (normalDropped = (importantDropped = 0L));
			completed = false;
		}
	}

	internal void Enqueue(string line, bool standardError)
	{
		string prefix = (standardError ? "[stderr] " : "");
		int room = EntryLimit - prefix.Length - Environment.NewLine.Length;
		string marker = Truncated + Environment.NewLine;
		string text = ((line.Length <= room) ? (prefix + line + Environment.NewLine) : (prefix + marker + line.Substring(line.Length - (room - marker.Length)) + Environment.NewLine));
		lock (gate)
		{
			Queue<(long, string)> queue = (standardError ? important : normal);
			ref int characters = ref standardError ? ref importantCharacters : ref normalCharacters;
			ref long dropped = ref standardError ? ref importantDropped : ref normalDropped;
			queue.Enqueue((sequence++, text));
			characters += text.Length;
			int limit = (standardError ? ImportantLimit : NormalLimit);
			while (characters > limit)
			{
				characters -= queue.Dequeue().Item2.Length;
				dropped++;
			}
		}
	}

	internal void Complete()
	{
		lock (gate)
		{
			completed = true;
		}
	}

	/// <summary>The next text for the Game log, and whether the output is complete and nothing is left.</summary>
	internal (string Text, bool Finished) Drain()
	{
		lock (gate)
		{
			StringBuilder text = new StringBuilder();
			if (normalDropped != 0L || importantDropped != 0L)
			{
				text.Append("[launcher] Output too fast - the panel dropped the oldest ").Append(normalDropped).Append(" stdout and ").Append(importantDropped).Append(" stderr lines; the log file still has everything the script wrote.")
					.AppendLine();
				normalDropped = (importantDropped = 0L);
			}
			while (normal.Count != 0 || important.Count != 0)
			{
				bool error = normal.Count == 0 || (important.Count != 0 && important.Peek().Sequence < normal.Peek().Sequence);
				Queue<(long, string)> queue = (error ? important : normal);
				if (text.Length + queue.Peek().Item2.Length > DrainLimit)
				{
					break;
				}
				string entry = queue.Dequeue().Item2;
				if (error)
				{
					importantCharacters -= entry.Length;
				}
				else
				{
					normalCharacters -= entry.Length;
				}
				text.Append(entry);
			}
			return (Text: text.ToString(), Finished: completed && normal.Count == 0 && important.Count == 0);
		}
	}
}
