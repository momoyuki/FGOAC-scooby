using System;
using System.Runtime.InteropServices;

namespace FGOLocalPlatform;

/// <summary>A pad as the Input layer reads it: the XInput buttons with the triggers as bits 16 and 17.</summary>
public readonly record struct PadState(uint Buttons);

/// <summary>What the Input layer reads: the keys, the pad and the window in front.</summary>
public interface IInputDevice
{
	IntPtr ForegroundWindow();

	bool KeyDown(int virtualKey);

	/// <summary>False when no controller answers on the configured number.</summary>
	bool ReadPad(out PadState state);
}

/// <summary>This PC's keyboard and pad, the pad read through the same XInput DLL the stock hook loads.</summary>
internal sealed class SystemInputDevice : IInputDevice
{
	private readonly bool dualSense;

	private readonly uint controllerIndex;

	private readonly Action<string> log;

	private long nextPadTry;

	private bool padBroken;

	public SystemInputDevice(bool dualSense, int controllerIndex, Action<string> log)
	{
		this.dualSense = dualSense;
		this.controllerIndex = (uint)controllerIndex;
		this.log = log;
	}

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int virtualKey);

	public IntPtr ForegroundWindow()
	{
		return GetForegroundWindow();
	}

	public bool KeyDown(int virtualKey)
	{
		return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
	}

	/// <summary>An absent controller is asked again once a second, since XInput is slow to say no.</summary>
	public bool ReadPad(out PadState state)
	{
		state = default(PadState);
		if (padBroken || Environment.TickCount64 < nextPadTry)
		{
			return false;
		}
		try
		{
			if (ControllerInput.GetState(dualSense, controllerIndex, out ControllerInput.State raw) != 0)
			{
				nextPadTry = Environment.TickCount64 + 1000;
				return false;
			}
			ControllerInput.Gamepad pad = raw.Gamepad;
			state = new PadState(pad.Buttons | ((pad.LeftTrigger > 64) ? 0x10000u : 0u) | ((pad.RightTrigger > 64) ? 0x20000u : 0u));
			return true;
		}
		catch (Exception ex) when (ControllerInput.IsLoadError(ex))
		{
			padBroken = true;
			log(ControllerInput.LoadErrorMessage(ex));
			return false;
		}
	}
}
