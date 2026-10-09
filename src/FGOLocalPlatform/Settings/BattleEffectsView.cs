using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;

namespace FGOLocalPlatform;

/// <summary>The Battle effects card: two ticks and the opacity slider, read from their files each time the card is shown and written as they change.</summary>
public partial class BattleEffectsView : UserControl, IComponentConnector
{
	private readonly DispatcherTimer opacityTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(400.0)
	};

	private bool showing;

	/// <summary>Shows the switches as the files hold them now.</summary>
	public void Reload()
	{
		showing = true;
		BattleEffects effects = BattleEffects.Read(GamePaths.GameRoot);
		HideNpScreenBox.IsChecked = effects.HideNpScreen;
		HideWhiteFlashBox.IsChecked = effects.HideWhiteFlash;
		OpacitySlider.Value = effects.Opacity;
		OpacityHookNote.Visibility = StockHook.Reads(GamePaths.GameRoot, "opacity") ? Visibility.Collapsed : Visibility.Visible;
		showing = false;
	}

	public BattleEffectsView()
	{
		InitializeComponent();
		base.Loaded += delegate
		{
			Reload();
		};
		opacityTimer.Tick += delegate
		{
			opacityTimer.Stop();
			Save();
		};
		base.Unloaded += delegate
		{
			if (opacityTimer.IsEnabled)
			{
				opacityTimer.Stop();
				Save();
			}
		};
	}

	private void Box_OnChanged(object sender, RoutedEventArgs e)
	{
		if (!showing)
		{
			Save();
		}
	}

	private void Opacity_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (!showing)
		{
			opacityTimer.Stop();
			opacityTimer.Start();
		}
	}

	private void Save()
	{
		try
		{
			new BattleEffects(HideNpScreenBox.IsChecked == true, HideWhiteFlashBox.IsChecked == true, (int)OpacitySlider.Value).Write(GamePaths.GameRoot);
			StatusText.Visibility = Visibility.Collapsed;
		}
		catch (Win32Exception ex)
		{
			StatusText.Text = "Could not save: " + ex.Message;
			StatusText.Visibility = Visibility.Visible;
		}
	}
}
