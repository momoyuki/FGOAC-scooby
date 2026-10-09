using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace FGOLocalPlatform;

/// <summary>
/// The Bindings card: the Profile, and a key box and a pad box for every On-screen button. Every change is checked
/// against the stock controls the Input card saved and written to the bindings file at once.
/// </summary>
public partial class BindingsSettingsView : UserControl, IComponentConnector
{
	private readonly string path = Bindings.PathIn(GamePaths.GameRoot);

	private readonly string controlsIni = Path.Combine(GamePaths.GameRoot, "segatools.ini");

	private readonly Bindings bindings;

	private readonly bool ready;

	public BindingsSettingsView()
	{
		InitializeComponent();
		List<string> problems = new List<string>();
		StockControls stock = StockControls.Read(controlsIni);
		try
		{
			bindings = Bindings.Load(path, stock, problems.Add);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			bindings = Bindings.Parse("", stock, problems.Add);
			problems.Add("The bindings file could not be read: " + ex.Message);
		}
		if (stock.TapsOff != null)
		{
			problems.Add(stock.TapsOff);
		}
		ProfileSelector.SelectedIndex = (bindings.Profile == Bindings.PadProfile) ? 1 : 0;
		// A button without a place on the screen has nothing to tap, so it gets no row.
		foreach (OnScreenButton button in bindings.Buttons.Where((OnScreenButton b) => b.Placed))
		{
			AddRow(button, stock);
		}
		ready = true;
		if (problems.Count > 0)
		{
			ShowProblem(this, string.Join(" ", problems));
		}
	}

	private void AddRow(OnScreenButton button, StockControls stock)
	{
		int row = BindingRows.RowDefinitions.Count;
		BindingRows.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(42.0)
		});
		TextBlock label = new TextBlock
		{
			Text = button.Label,
			Foreground = (Brush)Application.Current.Resources["TextBrush"],
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		Place(label, row, 0);
		ChordBindingButton keyBox = new ChordBindingButton
		{
			Value = bindings.Key(button.Name)
		};
		PadBindingButton padBox = new PadBindingButton
		{
			DualSense = stock.DualSense,
			ControllerIndex = stock.ControllerIndex
		};
		padBox.Value = bindings.Pad(button.Name);
		keyBox.Pressed += delegate(object? sender, Chord chord)
		{
			StockControls current = StockControls.Read(controlsIni);
			Apply(bindings.Bind(button.Name, chord, current), delegate
			{
				keyBox.Value = chord;
			});
		};
		padBox.Pressed += delegate(object? sender, Chord chord)
		{
			StockControls current = StockControls.Read(controlsIni);
			Apply(bindings.Bind(button.Name, chord, current), delegate
			{
				padBox.Value = chord;
			});
		};
		padBox.Problem += ShowProblem;
		AddBox(keyBox, button, "keyboard", row, 1, delegate
		{
			bindings.Unbind(button.Name, padBinding: false);
			keyBox.Value = null;
		});
		AddBox(padBox, button, "controller", row, 3, delegate
		{
			bindings.Unbind(button.Name, padBinding: true);
			padBox.Value = null;
		});
	}

	private void AddBox(Button box, OnScreenButton button, string device, int row, int column, Action clear)
	{
		box.Height = 34.0;
		box.Margin = new Thickness(0.0, 0.0, 8.0, 0.0);
		AutomationProperties.SetName(box, button.Label + ", " + device);
		Place(box, row, column);
		Button clearButton = new Button
		{
			Content = "Clear",
			Height = 34.0,
			MinHeight = 34.0
		};
		clearButton.SetResourceReference(FrameworkElement.StyleProperty, "QuietButtonStyle");
		AutomationProperties.SetName(clearButton, "Clear " + button.Label + ", " + device);
		clearButton.Click += delegate
		{
			Apply(null, clear);
		};
		Place(clearButton, row, column + 1);
	}

	private void Place(UIElement element, int row, int column)
	{
		Grid.SetRow(element, row);
		Grid.SetColumn(element, column);
		BindingRows.Children.Add(element);
	}

	/// <summary>Shows the refusal, or takes the change and saves the file.</summary>
	private void Apply(string? refusal, Action accepted)
	{
		if (refusal != null)
		{
			ShowProblem(this, refusal);
			return;
		}
		accepted();
		Save();
	}

	private void Profile_OnChanged(object sender, SelectionChangedEventArgs e)
	{
		if (ready)
		{
			bindings.Profile = (ProfileSelector.SelectedIndex == 1) ? Bindings.PadProfile : Bindings.KeyboardProfile;
			Save();
		}
	}

	private void Save()
	{
		try
		{
			bindings.Save(path);
			StatusText.Foreground = (Brush)Application.Current.Resources["IceBrush"];
			StatusText.Text = "Saved. The bindings apply the next time you press Play.";
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			ShowProblem(this, "Could not save the bindings: " + ex.Message);
		}
	}

	private void ShowProblem(object? sender, string problem)
	{
		StatusText.Foreground = (Brush)Application.Current.Resources["WarnBrush"];
		StatusText.Text = problem;
	}
}
