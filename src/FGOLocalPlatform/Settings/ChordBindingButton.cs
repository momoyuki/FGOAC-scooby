using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FGOLocalPlatform;

/// <summary>
/// A box that binds a key or a chord of keys by pressing: Ctrl, Shift, Alt and Windows held first join the key
/// pressed next, and a modifier released on its own binds by itself. Esc cancels. The owner decides whether
/// <see cref="Pressed"/>'s chord is bound and sets <see cref="Value"/>.
/// </summary>
public sealed class ChordBindingButton : Button
{
	private readonly List<int> held = new List<int>();

	private Chord? value;

	private bool listening;

	public event EventHandler<Chord>? Pressed;

	public Chord? Value
	{
		get
		{
			return value;
		}
		set
		{
			this.value = value;
			listening = false;
			held.Clear();
			base.Content = value?.Display() ?? "Not bound";
		}
	}

	public ChordBindingButton()
	{
		SetResourceReference(FrameworkElement.StyleProperty, typeof(Button));
		base.HorizontalContentAlignment = HorizontalAlignment.Left;
		base.ToolTip = "Click, then press a key, or hold Ctrl, Shift or Alt and press a key. Esc cancels.";
		base.Content = "Not bound";
		base.LostKeyboardFocus += delegate
		{
			if (listening)
			{
				Value = value;
			}
		};
	}

	protected override void OnClick()
	{
		base.OnClick();
		Focus();
		listening = true;
		held.Clear();
		base.Content = "Press a key or a chord... (Esc cancels)";
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		if (!listening)
		{
			base.OnPreviewKeyDown(e);
			return;
		}
		e.Handled = true;
		int key = VirtualKey(e);
		if (key == 0x1B && held.Count == 0)
		{
			Value = value;
		}
		else if (IsModifier(key))
		{
			if (!held.Contains(key))
			{
				held.Add(key);
			}
		}
		else if (key > 0 && key <= 0xFE)
		{
			Commit(key);
		}
	}

	protected override void OnPreviewKeyUp(KeyEventArgs e)
	{
		if (!listening)
		{
			base.OnPreviewKeyUp(e);
			return;
		}
		e.Handled = true;
		if (held.Contains(VirtualKey(e)))
		{
			Commit(0);
		}
	}

	protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
	{
		if (!listening)
		{
			base.OnPreviewMouseDown(e);
			return;
		}
		e.Handled = true;
		int key = e.ChangedButton switch
		{
			MouseButton.Middle => 0x04,
			MouseButton.XButton1 => 0x05,
			MouseButton.XButton2 => 0x06,
			_ => 0,
		};
		if (key != 0)
		{
			Commit(key);
		}
	}

	private void Commit(int key)
	{
		List<int> keys = new List<int>(held);
		if (key != 0)
		{
			keys.Add(key);
		}
		Chord? chord = Chord.FromKeys(keys);
		Value = value;
		if (chord != null)
		{
			this.Pressed?.Invoke(this, chord);
		}
	}

	private static int VirtualKey(KeyEventArgs e)
	{
		Key key = (e.Key == Key.System) ? e.SystemKey : ((e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key);
		return KeyInterop.VirtualKeyFromKey(key);
	}

	private static bool IsModifier(int key)
	{
		return key is (>= 0xA0 and <= 0xA5) or 0x5B or 0x5C;
	}
}
