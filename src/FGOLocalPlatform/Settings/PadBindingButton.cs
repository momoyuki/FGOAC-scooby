using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FGOLocalPlatform;

/// <summary>
/// A box that binds a pad button or a chord of pad buttons by pressing: with every button released, press the
/// buttons together and release them. It reads the controller the Input card names and gives up after ten
/// seconds. The owner decides whether <see cref="Pressed"/>'s chord is bound and sets <see cref="Value"/>.
/// </summary>
public sealed class PadBindingButton : Button
{
	private const uint PadButtons = 0x3F3FF;

	private readonly DispatcherTimer timer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(33.0)
	};

	private Chord? value;

	private bool sawNeutral;

	private uint pressed;

	private DateTime deadline;

	public event EventHandler<Chord>? Pressed;

	/// <summary>Why listening stopped without a chord, in one sentence.</summary>
	public event EventHandler<string>? Problem;

	public bool DualSense { get; set; }

	public int ControllerIndex { get; set; }

	public Chord? Value
	{
		get
		{
			return value;
		}
		set
		{
			this.value = value;
			timer.Stop();
			base.Content = value?.Display(DualSense) ?? "Not bound";
		}
	}

	public PadBindingButton()
	{
		SetResourceReference(FrameworkElement.StyleProperty, typeof(Button));
		base.HorizontalContentAlignment = HorizontalAlignment.Left;
		base.ToolTip = "Click, release every controller button, then press the button or buttons you want and let go.";
		base.Content = "Not bound";
		timer.Tick += Timer_OnTick;
		base.Unloaded += delegate
		{
			timer.Stop();
		};
	}

	protected override void OnClick()
	{
		base.OnClick();
		sawNeutral = false;
		pressed = 0;
		deadline = DateTime.UtcNow.AddSeconds(10.0);
		base.Content = "Release, then press...";
		timer.Start();
	}

	private void Timer_OnTick(object? sender, EventArgs e)
	{
		if (DateTime.UtcNow >= deadline)
		{
			Stop("Nothing was pressed on the controller for 10 seconds, so nothing changed.");
			return;
		}
		uint result;
		ControllerInput.State state;
		try
		{
			result = ControllerInput.GetState(DualSense, (uint)ControllerIndex, out state);
		}
		catch (Exception ex) when (ControllerInput.IsLoadError(ex))
		{
			Stop(ControllerInput.LoadErrorMessage(ex));
			return;
		}
		if (result != 0)
		{
			Stop((result == 1167) ? $"No controller answers as number {ControllerIndex + 1}. Check its connection and the controller number in Controls." : $"The controller check failed with error {result}.");
			return;
		}
		uint buttons = (state.Gamepad.Buttons | ((state.Gamepad.LeftTrigger > 64) ? 0x10000u : 0u) | ((state.Gamepad.RightTrigger > 64) ? 0x20000u : 0u)) & PadButtons;
		if (buttons != 0)
		{
			pressed |= sawNeutral ? buttons : 0;
			return;
		}
		sawNeutral = true;
		if (pressed != 0)
		{
			Chord? chord = Chord.FromPadMask(pressed);
			Value = value;
			if (chord != null)
			{
				this.Pressed?.Invoke(this, chord);
			}
		}
	}

	private void Stop(string problem)
	{
		Value = value;
		this.Problem?.Invoke(this, problem);
	}
}
