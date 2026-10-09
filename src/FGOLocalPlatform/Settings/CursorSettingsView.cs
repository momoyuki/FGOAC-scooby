using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FGOLocalPlatform;

public partial class CursorSettingsView : UserControl, IComponentConnector
{
	public sealed class Preferences
	{
		public int Size { get; set; } = 48;

		public int Outline { get; set; } = 2;

		public double X { get; set; } = 50.0;

		public double Y { get; set; } = 50.0;
	}

	private readonly string directory = Path.Combine(GamePaths.GameRoot, "cursor");

	private readonly string ini = Path.Combine(GamePaths.GameRoot, "segatools.ini");

	/// <summary>The mouse buttons Tap with offers, as virtual keys: left, right, middle, X1 and X2.</summary>
	public static IReadOnlyList<int> TapButtons { get; } = new int[5] { 0x01, 0x02, 0x04, 0x05, 0x06 };

	private BitmapSource? artwork;

	private bool ready;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern uint GetPrivateProfileInt(string section, string key, int fallback, string file);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool WritePrivateProfileString(string section, string key, string? value, string file);

	/// <summary>Shows Tap with as segatools.ini holds it now.</summary>
	public void Reload()
	{
		ready = false;
		ShowTapButton(StockControls.Read(ini).TouchKey);
		ready = true;
	}

	public CursorSettingsView()
	{
		InitializeComponent();
		try
		{
			string path = Path.Combine(directory, "settings.json");
			Preferences preferences = (File.Exists(path) ? (JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new Preferences()) : new Preferences());
			SizeSlider.Value = preferences.Size;
			OutlineSlider.Value = preferences.Outline;
			HotX.Value = preferences.X;
			HotY.Value = preferences.Y;
			string path2 = Path.Combine(directory, "source.png");
			if (File.Exists(path2))
			{
				artwork = CursorArtwork.Load(path2);
				ImageName.Text = "Saved custom image";
			}
		}
		catch (Exception ex)
		{
			Status.Text = "Could not read the saved pointer settings: " + ex.Message;
		}
		Mode.SelectedIndex = Math.Clamp((int)GetPrivateProfileInt("touch", "cursorStyle", 0, ini), 0, 2);
		try
		{
			string text = Path.Combine(directory, "custom.cur");
			if (Mode.SelectedIndex == 2 && artwork != null && File.Exists(text))
			{
				byte[] array = File.ReadAllBytes(text);
				if (array.Length >= 38 && BitConverter.ToUInt16(array, 2) == 2 && BitConverter.ToUInt16(array, 36) == 24)
				{
					string text2 = text + ".24bpp.bak";
					if (!File.Exists(text2))
					{
						File.Copy(text, text2);
					}
					byte[] bytes = CursorArtwork.Build(artwork, (int)SizeSlider.Value, (int)OutlineSlider.Value, HotX.Value / 100.0, HotY.Value / 100.0);
					AtomicFile.WriteAllBytes(text, bytes);
					Status.Text = "Upgraded the old cursor color format - it takes effect the next time the game starts.";
				}
			}
		}
		catch (Exception ex2)
		{
			Status.Text = "Could not upgrade the cursor format, save again: " + ex2.Message;
		}
		ShowTapButton(StockControls.Read(ini).TouchKey);
		TapHookNote.Visibility = StockHook.Reads(GamePaths.GameRoot, "clickKey") ? Visibility.Collapsed : Visibility.Visible;
		ready = true;
		RefreshPreview();
	}

	/// <summary>Why <paramref name="key"/> cannot be the touch button, in one sentence; null when it can.</summary>
	public static string? TapRefusal(int key, StockControls stock, Bindings bindings)
	{
		if (key == stock.TouchKey)
		{
			return null;
		}
		string name = Chord.Name(key);
		string? action = stock.KeyAction(key);
		if (action != null)
		{
			return name + " is " + action + " in Controls, so it cannot be the touch button.";
		}
		Chord chord = Chord.FromKeys(new int[1] { key })!;
		return bindings.Conflict(chord, "so it cannot be the touch button.");
	}

	/// <summary>Writes [touch] clickKey; the left mouse button, the stock hook's default, removes the key.</summary>
	public static void WriteTapButton(string iniPath, int key)
	{
		if (!WritePrivateProfileString("touch", "clickKey", (key == StockControls.LeftMouseButton) ? null : key.ToString(CultureInfo.InvariantCulture), iniPath))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}
	}

	/// <summary>The Tap with choices, with a saved key that is not one of them kept as its own entry.</summary>
	private void ShowTapButton(int key)
	{
		TapButton.Items.Clear();
		foreach (int button in TapButtons.Contains(key) ? TapButtons : TapButtons.Append(key))
		{
			TapButton.Items.Add(new ComboBoxItem
			{
				Content = Chord.Name(button),
				Tag = button
			});
		}
		TapButton.SelectedItem = TapButton.Items.Cast<ComboBoxItem>().First((ComboBoxItem item) => (int)item.Tag == key);
	}

	private void TapButton_OnChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!ready || !(TapButton.SelectedItem is ComboBoxItem { Tag: int key }))
		{
			return;
		}
		StockControls stock = StockControls.Read(ini);
		try
		{
			string? refusal = TapRefusal(key, stock, Bindings.Load(Bindings.PathIn(GamePaths.GameRoot), stock, delegate
			{
			}));
			if (refusal == null)
			{
				WriteTapButton(ini, key);
				TapStatus.Text = "Saved - it takes effect the next time the game starts.";
				return;
			}
			TapStatus.Text = refusal;
		}
		catch (Exception ex) when (ex is Win32Exception || ex is IOException || ex is UnauthorizedAccessException)
		{
			TapStatus.Text = "Could not save: " + ex.Message;
		}
		ready = false;
		ShowTapButton(stock.TouchKey);
		ready = true;
	}

	private void Choose_OnClick(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
			Title = "Choose a pointer image"
		};
		if (DialogSink.ShowDialog(openFileDialog, null) != true)
		{
			return;
		}
		try
		{
			artwork = CursorArtwork.Load(openFileDialog.FileName);
			ImageName.Text = Path.GetFileName(openFileDialog.FileName);
			Mode.SelectedIndex = 2;
			RefreshPreview();
		}
		catch (Exception ex)
		{
			Status.Text = "Could not open the image: " + ex.Message;
		}
	}

	private void Settings_OnChanged(object sender, RoutedEventArgs e)
	{
		if (ready)
		{
			RefreshPreview();
			Status.Text = "Settings are not saved yet.";
		}
	}

	private void RefreshPreview()
	{
		// The preview, the outline and the hotspot shape a custom image only.
		Visibility custom = (Mode.SelectedIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
		PreviewBorder.Visibility = custom;
		OutlinePanel.Visibility = custom;
		HotspotPanel.Visibility = custom;
		Hotspot.Visibility = ((artwork == null || Mode.SelectedIndex != 2) ? Visibility.Collapsed : Visibility.Visible);
		if (artwork == null || Mode.SelectedIndex != 2)
		{
			Preview.Source = null;
			return;
		}
		int num = (int)SizeSlider.Value;
		Preview.Source = CursorArtwork.Preview(artwork, num, (int)OutlineSlider.Value);
		Canvas.SetLeft(Hotspot, (double)(130 - num) / 2.0 + HotX.Value / 100.0 * (double)(num - 1) - 2.5);
		Canvas.SetTop(Hotspot, (double)(130 - num) / 2.0 + HotY.Value / 100.0 * (double)(num - 1) - 2.5);
	}

	private void Save_OnClick(object sender, RoutedEventArgs e)
	{
		try
		{
			string text = "";
			if (Mode.SelectedIndex == 2)
			{
				if (artwork == null)
				{
					throw new InvalidOperationException("Choose a pointer image first.");
				}
				byte[] bytes = CursorArtwork.Build(artwork, (int)SizeSlider.Value, (int)OutlineSlider.Value, HotX.Value / 100.0, HotY.Value / 100.0);
				Directory.CreateDirectory(directory);
				text = "cursor\\custom.cur";
				File.WriteAllBytes(Path.Combine(GamePaths.GameRoot, text), bytes);
				PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
				pngBitmapEncoder.Frames.Add(BitmapFrame.Create(artwork));
				using (FileStream stream = File.Create(Path.Combine(directory, "source.png")))
				{
					pngBitmapEncoder.Save(stream);
				}
				File.WriteAllText(Path.Combine(directory, "settings.json"), JsonSerializer.Serialize(new Preferences
				{
					Size = (int)SizeSlider.Value,
					Outline = (int)OutlineSlider.Value,
					X = HotX.Value,
					Y = HotY.Value
				}));
			}
			(string, string)[] array = new(string, string)[4]
			{
				("cursorFile", text),
				("cursorSize", ((int)SizeSlider.Value).ToString()),
				("cursorStyle", Mode.SelectedIndex.ToString()),
				("cursor", "1")
			};
			for (int i = 0; i < array.Length; i++)
			{
				(string, string) tuple = array[i];
				if (!WritePrivateProfileString("touch", tuple.Item1, tuple.Item2, ini))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}
			}
			Status.Text = "Pointer settings saved - they take effect the next time the game starts.";
		}
		catch (Exception ex)
		{
			Status.Text = "Could not save: " + ex.Message;
		}
	}
}
