using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FGOLocalPlatform;

public static class PhotoNumberInput
{
	public static Slider CreatePositionSlider()
	{
		Slider slider = new Slider
		{
			Minimum = -100.0,
			Maximum = 100.0,
			SmallChange = 0.01,
			LargeChange = 0.1
		};
		slider.ValueChanged += delegate
		{
			if (!slider.IsMouseCaptureWithin)
			{
				ExpandAtEdge();
			}
		};
		slider.AddHandler(Mouse.LostMouseCaptureEvent, (MouseEventHandler)delegate
		{
			ExpandAtEdge();
		}, handledEventsToo: true);
		return slider;
		void ExpandAtEdge()
		{
			double num = Math.Max(Math.Abs(slider.Minimum), Math.Abs(slider.Maximum));
			if (slider.Value <= slider.Minimum)
			{
				slider.Minimum = Math.Max(-3.4028234663852886E+38, slider.Minimum - num);
			}
			else if (slider.Value >= slider.Maximum)
			{
				slider.Maximum = Math.Min(3.4028234663852886E+38, slider.Maximum + num);
			}
		}
	}

	public static void SetExpandedValue(Slider slider, double value)
	{
		if (IsUsable(value))
		{
			slider.Minimum = Math.Min(slider.Minimum, value);
			slider.Maximum = Math.Max(slider.Maximum, value);
			slider.SetCurrentValue(RangeBase.ValueProperty, value);
		}
	}

	public static TextBox Create(Slider slider, double multiplier = 1.0, Action? beforeCommit = null, bool expandRange = false, bool expandMinimum = true)
	{
		TextBox box = new TextBox
		{
			MinWidth = 0.0,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			Margin = new Thickness(4.0, 0.0, 0.0, 0.0),
			Height = 30.0,
			Padding = new Thickness(4.0, 2.0, 4.0, 2.0),
			FontSize = 14.0,
			TextAlignment = TextAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = Brushes.White,
			Background = new SolidColorBrush(Color.FromRgb(23, 18, 27)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(86, 65, 93)),
			BorderThickness = new Thickness(1.0),
			ToolTip = (expandRange ? "Type a value and press Enter, or click elsewhere, to apply it; Esc undoes what you typed. A value outside the slider's range is allowed. Drag sideways to fine-tune." : "Type a value and press Enter, or click elsewhere, to apply it; Esc undoes what you typed. A value outside the range snaps to the nearest one allowed. Drag sideways to fine-tune.")
		};
		slider.ValueChanged += delegate
		{
			if (!box.IsKeyboardFocusWithin)
			{
				Refresh();
			}
		};
		bool pending = false;
		bool dragging = false;
		Point origin = default(Point);
		double initial = 0.0;
		box.PreviewMouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (e.ClickCount == 1)
			{
				pending = true;
				dragging = false;
				origin = e.GetPosition(slider);
				initial = slider.Value;
			}
		};
		box.PreviewMouseMove += delegate(object _, MouseEventArgs e)
		{
			if (pending && e.LeftButton == MouseButtonState.Pressed)
			{
				double num = e.GetPosition(slider).X - origin.X;
				if (dragging || !(Math.Abs(num) < SystemParameters.MinimumHorizontalDragDistance))
				{
					if (!dragging)
					{
						box.CaptureMouse();
						pending = true;
						dragging = true;
						box.Cursor = Cursors.SizeWE;
					}
					double value = DragValue(slider, initial, num, (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.None);
					if (IsUsable(value))
					{
						value = Apply(slider, value, expandRange, expandMinimum);
						beforeCommit?.Invoke();
						slider.SetCurrentValue(RangeBase.ValueProperty, value);
						Refresh();
					}
					e.Handled = true;
				}
			}
		};
		box.PreviewMouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			pending = false;
			if (dragging)
			{
				dragging = false;
				box.ReleaseMouseCapture();
				box.Cursor = null;
				Refresh();
				e.Handled = true;
			}
		};
		box.LostMouseCapture += delegate(object _, MouseEventArgs e)
		{
			if (e.OriginalSource == box && !box.IsMouseCaptured)
			{
				pending = false;
				dragging = false;
				box.Cursor = null;
			}
		};
		box.LostKeyboardFocus += delegate
		{
			Commit();
		};
		box.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				Commit();
				e.Handled = true;
			}
			else if (e.Key == Key.Escape)
			{
				Refresh();
				e.Handled = true;
			}
		};
		Refresh();
		return box;
		void Commit()
		{
			if ((double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var result) || double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) && IsUsable(result / multiplier))
			{
				result = Apply(slider, result / multiplier, expandRange, expandMinimum);
				beforeCommit?.Invoke();
				slider.SetCurrentValue(RangeBase.ValueProperty, result);
			}
			Refresh();
		}
		void Refresh()
		{
			box.Text = (slider.Value * multiplier).ToString("0.######", CultureInfo.CurrentCulture);
		}
	}

	/// <summary>The value a horizontal drag of <paramref name="distance"/> pixels reaches: a tenth of the small change per pixel, or one tick on a snapping slider; a tenth of that with Shift.</summary>
	internal static double DragValue(Slider slider, double initial, double distance, bool fine)
	{
		double step = (slider.IsSnapToTickEnabled ? slider.TickFrequency : (slider.SmallChange / 10.0));
		if (fine)
		{
			step /= 10.0;
		}
		return initial + distance * step;
	}

	/// <summary>Widens the slider to <paramref name="value"/> when the field expands, then returns the value clamped to the slider and snapped to its ticks.</summary>
	internal static double Apply(Slider slider, double value, bool expandRange, bool expandMinimum)
	{
		if (expandRange)
		{
			if (expandMinimum)
			{
				slider.Minimum = Math.Min(slider.Minimum, value);
			}
			slider.Maximum = Math.Max(slider.Maximum, value);
		}
		value = Math.Clamp(value, slider.Minimum, slider.Maximum);
		if (slider.IsSnapToTickEnabled && slider.TickFrequency > 0.0)
		{
			value = slider.Minimum + Math.Round((value - slider.Minimum) / slider.TickFrequency) * slider.TickFrequency;
		}
		return Math.Clamp(value, slider.Minimum, slider.Maximum);
	}

	/// <summary>True for a finite value that fits the hook's single-precision fields.</summary>
	private static bool IsUsable(double value)
	{
		return double.IsFinite(value) && Math.Abs(value) <= 3.4028234663852886E+38;
	}

	public static FrameworkElement TimeRow(Slider slider, Action stop)
	{
		StackPanel obj = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0),
			Children =
			{
				(UIElement)new TextBlock
				{
					Text = "Time (s)",
					VerticalAlignment = VerticalAlignment.Center,
					Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
				}
			}
		};
		TextBox textBox = Create(slider, 1.0 / 60.0, stop);
		textBox.Width = 84.0;
		obj.Children.Add(textBox);
		return obj;
	}
}
