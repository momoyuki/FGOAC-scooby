using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace FGOLocalPlatform;

/// <summary>
/// The Input layer: one thread per Session that, while the game's window is in front, taps the On-screen buttons
/// whose bindings are pressed. A tap is posted to the game window as the touch button at the client point of the
/// Canvas point, which the stock hook's window subclass turns into touch; the real mouse pointer never moves.
/// </summary>
public sealed class InputLayer : IDisposable
{
	private struct Rect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private sealed record Posted(long Due, IntPtr Window, uint Message, IntPtr WParam, IntPtr LParam);

	/// <summary>The messages that press and release a mouse button, and their wParams.</summary>
	private sealed record TouchButton(uint Down, uint Up, int DownWParam, int UpWParam);

	private const uint MouseMove = 0x200;

	private const int PollMilliseconds = 8;

	private const int HoldMilliseconds = 60;

	private const int GapMilliseconds = 40;

	private readonly ManualResetEventSlim stopping = new ManualResetEventSlim();

	private readonly Thread thread;

	private InputLayer(Action<InputLayer> body)
	{
		thread = new Thread(() => body(this))
		{
			IsBackground = true,
			Name = "Input layer"
		};
		thread.Start();
	}

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

	[DllImport("user32.dll")]
	private static extern bool GetClientRect(IntPtr window, out Rect rect);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

	/// <summary>Runs the layer against the game process with these bindings until <see cref="Dispose"/>; taps press touchKey.</summary>
	public static InputLayer Start(Bindings bindings, TouchGate gate, int touchKey, int processId, IInputDevice device, Action<string> log)
	{
		return new InputLayer((InputLayer layer) => layer.Guarded(log, () => layer.Run(bindings, gate, touchKey, processId, device, log)));
	}

	/// <summary>
	/// Waits for the Session's game, reads the Runtime ini the launch script wrote for it and the bindings file,
	/// then runs the layer until <see cref="Dispose"/>.
	/// </summary>
	public static InputLayer StartForSession(string gameRoot, Action<string> log)
	{
		return new InputLayer((InputLayer layer) => layer.Guarded(log, delegate
		{
			int processId;
			while ((processId = GameProcessId(gameRoot)) == 0)
			{
				if (layer.stopping.Wait(500))
				{
					return;
				}
			}
			string runtimeIni = Path.GetFullPath(Path.Combine(gameRoot, "..", "DEVICE", "runtime", "segatools.runtime.ini"));
			StockControls stock = StockControls.Read(runtimeIni);
			if (stock.TapsOff != null)
			{
				log(stock.TapsOff);
				return;
			}
			Bindings bindings = Bindings.Load(Bindings.PathIn(gameRoot), stock, log);
			bool pad = bindings.Profile == Bindings.PadProfile;
			int count = bindings.Buttons.Count((OnScreenButton button) => (pad ? bindings.Pad(button.Name) : bindings.Key(button.Name)) != null);
			if (count == 0)
			{
				log("No bindings are set for the " + bindings.Profile + " profile, so nothing is tapped this session.");
				return;
			}
			log($"{count} binding(s) on the {bindings.Profile} profile work while the game window is in front.");
			layer.Run(bindings, TouchGate.Read(runtimeIni), stock.TouchKey, processId, new SystemInputDevice(stock.DualSense, stock.ControllerIndex, log), log);
		}));
	}

	public void Dispose()
	{
		stopping.Set();
		if (Thread.CurrentThread != thread)
		{
			thread.Join();
		}
	}

	private void Guarded(Action<string> log, Action body)
	{
		try
		{
			body();
		}
		catch (Exception ex)
		{
			// The layer must never take the launcher down with it; the Session goes on without bindings.
			log("The bindings stopped working for this session: " + ex.Message);
		}
	}

	private void Run(Bindings bindings, TouchGate gate, int touchKey, int processId, IInputDevice device, Action<string> log)
	{
		TouchButton? touch = TouchButtonFor(touchKey);
		if (touch == null)
		{
			log(Chord.Name(touchKey) + " is the touch button, and a binding can only tap with a mouse button, so nothing is tapped this session.");
			return;
		}
		bool pad = bindings.Profile == Bindings.PadProfile;
		List<(OnScreenButton Button, Chord Chord)> bound = bindings.Buttons
			.Select((OnScreenButton button) => (button, pad ? bindings.Pad(button.Name) : bindings.Key(button.Name)))
			.Where(((OnScreenButton, Chord?) item) => item.Item2 != null)
			.Select(((OnScreenButton, Chord?) item) => (item.Item1, item.Item2!))
			.ToList();
		ChordMatcher matcher = new ChordMatcher(bound.Select(((OnScreenButton Button, Chord Chord) item) => item.Chord).ToList());
		int[] keys = bound.SelectMany(((OnScreenButton Button, Chord Chord) item) => item.Chord.Codes).Distinct().ToArray();
		List<Posted> posted = new List<Posted>();
		HashSet<string> told = new HashSet<string>();
		HashSet<int> down = new HashSet<int>();
		Stopwatch clock = Stopwatch.StartNew();
		long tapFree = 0;
		void Tell(string line)
		{
			if (told.Add(line))
			{
				log(line);
			}
		}
		void Post(long due, IntPtr window, uint message, int wParam, int x, int y)
		{
			Posted item = new Posted(due, window, message, (IntPtr)wParam, (IntPtr)((y << 16) | (x & 0xFFFF)));
			int index = posted.FindIndex((Posted other) => other.Due > due);
			posted.Insert((index < 0) ? posted.Count : index, item);
		}
		while (!stopping.IsSet)
		{
			long now = clock.ElapsedMilliseconds;
			IntPtr window = device.ForegroundWindow();
			bool front = window != IntPtr.Zero && GetWindowThreadProcessId(window, out int owner) != 0 && owner == processId;
			down.Clear();
			PadState state = default(PadState);
			bool padUp = pad && device.ReadPad(out state);
			if (padUp)
			{
				for (int bit = 0; bit < 18; bit++)
				{
					if ((state.Buttons & (1u << bit)) != 0)
					{
						down.Add(Chord.PadCode + bit);
					}
				}
			}
			else if (!pad)
			{
				foreach (int key in keys.Where(device.KeyDown))
				{
					down.Add(key);
				}
			}
			if (!front)
			{
				matcher.Reset(down.Contains);
			}
			else
			{
				foreach (int index in matcher.Poll(down.Contains))
				{
					OnScreenButton button = bound[index].Button;
					ClientPoint? point = button.Placed && GetClientRect(window, out Rect rect) ? gate.ToClient(button.X, button.Y, rect.Right, rect.Bottom) : null;
					if (point == null)
					{
						Tell(button.Placed ? (button.Label + " is outside the game's 16:9 area at this window size, so its binding taps nothing.") : (button.Label + " has no place on the screen yet, so its binding taps nothing."));
						continue;
					}
					long start = Math.Max(now, tapFree);
					Post(start, window, MouseMove, 0, point.Value.X, point.Value.Y);
					Post(start, window, touch.Down, touch.DownWParam, point.Value.X, point.Value.Y);
					Post(start + HoldMilliseconds, window, touch.Up, touch.UpWParam, point.Value.X, point.Value.Y);
					tapFree = start + HoldMilliseconds + GapMilliseconds;
				}
			}
			PostDue(posted, now, Tell);
			stopping.Wait(PollMilliseconds);
		}
		PostDue(posted, long.MaxValue, Tell);
	}

	/// <summary>The messages for a mouse button the stock hook's subclass matches against clickKey; null for any other key.</summary>
	private static TouchButton? TouchButtonFor(int touchKey)
	{
		return touchKey switch
		{
			0x01 => new TouchButton(0x201, 0x202, 0x01, 0),
			0x02 => new TouchButton(0x204, 0x205, 0x02, 0),
			0x04 => new TouchButton(0x207, 0x208, 0x10, 0),
			0x05 => new TouchButton(0x20B, 0x20C, 0x10020, 0x10000),
			0x06 => new TouchButton(0x20B, 0x20C, 0x20040, 0x20000),
			_ => null,
		};
	}

	private static void PostDue(List<Posted> posted, long now, Action<string> tell)
	{
		while (posted.Count > 0 && posted[0].Due <= now)
		{
			Posted message = posted[0];
			posted.RemoveAt(0);
			if (!PostMessageW(message.Window, message.Message, message.WParam, message.LParam))
			{
				tell("The game window did not take a tap (" + new Win32Exception(Marshal.GetLastWin32Error()).Message + "); start the launcher as administrator.");
			}
		}
	}

	private static int GameProcessId(string gameRoot)
	{
		string path = Path.Combine(gameRoot, "ago.exe");
		foreach (Process process in Process.GetProcessesByName("ago"))
		{
			using (process)
			{
				try
				{
					if (string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
					{
						return process.Id;
					}
				}
				catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
				{
					// The module list is not readable while the game is still starting; the next look finds it.
				}
			}
		}
		return 0;
	}
}
