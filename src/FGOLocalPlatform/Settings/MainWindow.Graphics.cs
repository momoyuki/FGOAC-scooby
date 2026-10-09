using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private int hideUiVirtualKey = 121;

	private bool bindingHideUiKey;

	private void UpdateHideUiKeyLabel()
	{
		HideUiKeyButton.Content = KeyInterop.KeyFromVirtualKey(hideUiVirtualKey).ToString();
	}

	private void HideUiKeyButton_OnClick(object sender, RoutedEventArgs e)
	{
		bindingHideUiKey = true;
		HideUiKeyButton.Content = "Press a key (Esc to cancel)";
		HideUiKeyButton.Focus();
	}

	private void HideUiKeyButton_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		bindingHideUiKey = false;
		UpdateHideUiKeyLabel();
	}

	private void HideUiKeyButton_OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (!bindingHideUiKey)
		{
			return;
		}
		e.Handled = true;
		Key val = ((e.Key == Key.System) ? e.SystemKey : e.Key);
		if (val != Key.LWin && val != Key.RWin && (val < Key.LeftShift || val > Key.RightAlt))
		{
			if (val != Key.Escape)
			{
				hideUiVirtualKey = KeyInterop.VirtualKeyFromKey(val);
			}
			bindingHideUiKey = false;
			UpdateHideUiKeyLabel();
		}
	}

	private void ShaderCacheButton_OnClick(object sender, RoutedEventArgs e)
	{
		bool running = false;
		foreach (string name in new string[2] { "ago", "amdaemon" })
		{
			Process[] processes = Process.GetProcessesByName(name);
			running |= processes.Length != 0;
			foreach (Process process in processes)
			{
				process.Dispose();
			}
		}
		try
		{
			HoldStatus((ShaderCache.Clear(GamePaths.GameRoot, running).Count == 0) ? "There was no shader cache to clear." : "Shader cache cleared: the next start builds it again, so it takes a little longer.");
		}
		catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)
		{
			HoldStatus(ex.Message, problem: true);
		}
	}

	private void SaveGraphicsButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (!SaveLauncherSettings(out string _, out int _, out int _, out string _, out int _))
		{
			return;
		}
		try
		{
			if (PickedGpuLayer is GpuCompat.Choice picked && picked != GpuCompat.Current)
			{
				GpuCompat.Apply(picked);
			}
		}
		catch (Exception ex)
		{
			HoldStatus("The graphics compatibility layer could not be changed: " + ex.Message);
			return;
		}
		RefreshGpuCompatSection();
		ShowGpuCardOptions();
		try
		{
			ApplyGpuPreference();
		}
		catch (Exception ex)
		{
			HoldStatus("The GPU preference could not be set: " + ex.Message);
			return;
		}
		HoldStatus("Graphics settings saved - they take effect the next time the game starts.");
	}

	/// <summary>The layer picked in the Graphics layer list; null while "Another layer" is shown.</summary>
	private GpuCompat.Choice? PickedGpuLayer => (GpuLayerComboBox.SelectedItem as ComboBoxItem)?.Tag as GpuCompat.Choice?;

	/// <summary>Fills the Graphics layer list with the choices this game folder offers and selects the layer in App\.</summary>
	private void RefreshGpuCompatSection()
	{
		GpuCompat.Choice? current = GpuCompat.Current;
		GpuLayerComboBox.Items.Clear();
		if (current == null)
		{
			GpuLayerComboBox.Items.Add(new ComboBoxItem { Content = "Another layer, left as it is", Tag = null });
		}
		foreach (GpuCompat.Choice choice in GpuCompat.Offered)
		{
			GpuLayerComboBox.Items.Add(new ComboBoxItem { Content = GpuLayerName(choice), Tag = choice });
		}
		GpuLayerComboBox.SelectedIndex = 0;
		foreach (ComboBoxItem item in GpuLayerComboBox.Items)
		{
			if (Equals(item.Tag, current))
			{
				GpuLayerComboBox.SelectedItem = item;
			}
		}
		ShowGpuLayerHelp();
	}

	private static string GpuLayerName(GpuCompat.Choice choice)
	{
		return choice switch
		{
			GpuCompat.Choice.Cloud => "Cloud23333's layer",
			GpuCompat.Choice.Older => "The 1.1.0 layer",
			GpuCompat.Choice.Fluphus => "fluphus's AMD layer",
			_ => "Off"
		};
	}

	private void GpuLayerComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		ShowGpuLayerHelp();
	}

	private void ShowGpuLayerHelp()
	{
		bool his = GpuCompat.HisBuild;
		GpuCompatHelpText.Text = PickedGpuLayer switch
		{
			null => "App holds a layer file FGOAC scooby did not ship, so it is left as it is. Pick a layer to replace it.",
			GpuCompat.Choice.Cloud => his ? "The layer Cloud23333's update installs and runs. fluphus's AMD layer is the other choice, for a card that runs badly on his." : "Cloud23333's layer from his latest update (App\\fgoglcompat.dll), the default for AMD and Intel graphics.",
			GpuCompat.Choice.Older => "The layer FGOAC scooby shipped with 1.1.0 (App\\fgoglcompat.dll), which players report working on RX 500, RX 6000 and RX 7600 cards and on desktop Ryzen graphics.",
			GpuCompat.Choice.Fluphus => "fluphus's AMD layer (App\\opengl32.dll), which holds the game at 60 FPS. In AMD Software set Wait for Vertical Refresh to Off, Unless Application Specifies, and turn off overlays and other frame limiters." + (his ? " No graphics card can be picked while it is on." : ""),
			_ => GpuCompat.HasNvidiaAdapter() ? "Off. An NVIDIA card is present: the layers are for AMD and Intel graphics." : "Off. On AMD and Intel graphics pick a layer; Cloud23333's is the default."
		};
	}

	private void ResetDamageUi_OnClick(object sender, RoutedEventArgs e)
	{
		Slider damageNumberScaleSlider = DamageNumberScaleSlider;
		double value = (DamageTextureScaleSlider.Value = 100.0);
		damageNumberScaleSlider.Value = value;
		Slider damageNumberOpacitySlider = DamageNumberOpacitySlider;
		value = (DamageTextureOpacitySlider.Value = 100.0);
		damageNumberOpacitySlider.Value = value;
	}
}
