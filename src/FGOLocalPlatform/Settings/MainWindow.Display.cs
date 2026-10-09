using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private static readonly IReadOnlyDictionary<string, (int Width, int Height)[]> ResolutionPresets = new Dictionary<string, (int, int)[]>
	{
		["16:9"] = new(int, int)[7]
		{
			(1280, 720),
			(1600, 900),
			(1920, 1080),
			(2560, 1440),
			(3840, 2160),
			(5120, 2880),
			(7680, 4320)
		},
		["16:10"] = new(int, int)[7]
		{
			(1280, 800),
			(1440, 900),
			(1680, 1050),
			(1920, 1200),
			(2560, 1600),
			(3840, 2400),
			(5120, 3200)
		},
		["21:9"] = new(int, int)[7]
		{
			(1680, 720),
			(2560, 1080),
			(3360, 1440),
			(3440, 1440),
			(3840, 1600),
			(5040, 2160),
			(5120, 2160)
		},
		["32:9"] = new(int, int)[4]
		{
			(2560, 720),
			(3840, 1080),
			(5120, 1440),
			(7680, 2160)
		},
		["4:3"] = new(int, int)[6]
		{
			(1280, 960),
			(1600, 1200),
			(1920, 1440),
			(2560, 1920),
			(3200, 2400),
			(5760, 4320)
		},
		["9:16"] = new(int, int)[7]
		{
			(720, 1280),
			(900, 1600),
			(1080, 1920),
			(1440, 2560),
			(2160, 3840),
			(2880, 5120),
			(4320, 7680)
		},
		["10:16"] = new(int, int)[7]
		{
			(800, 1280),
			(900, 1440),
			(1050, 1680),
			(1200, 1920),
			(1600, 2560),
			(2400, 3840),
			(3200, 5120)
		},
		["9:21"] = new(int, int)[7]
		{
			(720, 1680),
			(1080, 2560),
			(1440, 3360),
			(1440, 3440),
			(1600, 3840),
			(2160, 5040),
			(2160, 5120)
		},
		["9:32"] = new(int, int)[4]
		{
			(720, 2560),
			(1080, 3840),
			(1440, 5120),
			(2160, 7680)
		},
		["3:4"] = new(int, int)[8]
		{
			(720, 960),
			(900, 1200),
			(1080, 1440),
			(1200, 1600),
			(1440, 1920),
			(1920, 2560),
			(2400, 3200),
			(4320, 5760)
		}
	};

	private bool updatingResolutionOptions;

	private static bool TryParseResolution(string? text, out int width, out int height)
	{
		width = 0;
		height = 0;
		Match match = Regex.Match(text ?? "", "^\\s*(\\d{3,4})\\s*[xX×]\\s*(\\d{3,4})\\s*$");
		if (match.Success && int.TryParse(match.Groups[1].Value, out width))
		{
			return int.TryParse(match.Groups[2].Value, out height);
		}
		return false;
	}

	private static string DetectAspectRatio(int width, int height)
	{
		foreach (var (result, source) in ResolutionPresets)
		{
			if (source.Any(((int Width, int Height) item) => item.Width == width && item.Height == height))
			{
				return result;
			}
		}
		if (height <= 0)
		{
			return "custom";
		}
		double aspect = (double)width / (double)height;
		(string, double) tuple = (from item in new (string Name, double Value)[10]
			{
				("4:3", 1.3333333333333333),
				("16:10", 1.6),
				("16:9", 1.7777777777777777),
				("21:9", 2.3333333333333335),
				("32:9", 3.5555555555555554),
				("9:16", 0.5625),
				("10:16", 0.625),
				("9:21", 0.42857142857142855),
				("9:32", 9.0 / 32.0),
				("3:4", 0.75)
			}
			select (Name: item.Name, Math.Abs(aspect - item.Value)) into item
			orderby item.Item2
			select item).First();
		if (!(tuple.Item2 <= 0.08))
		{
			return "custom";
		}
		return tuple.Item1;
	}

	private void PopulateResolutionOptions(string aspectRatio, string? preferredText)
	{
		updatingResolutionOptions = true;
		try
		{
			ResolutionComboBox.Items.Clear();
			string preferred = preferredText?.Trim().Replace('×', 'x') ?? "";
			if (!ResolutionPresets.TryGetValue(aspectRatio, out (int, int)[] value))
			{
				ResolutionComboBox.Text = (TryParseResolution(preferred, out var _, out var _) ? preferred : "1920x1080");
				SyncCustomResolutionInputs(ResolutionComboBox.Text);
				return;
			}
			(int, int)[] array = value;
			for (int height = 0; height < array.Length; height++)
			{
				var (value2, value3) = array[height];
				ResolutionComboBox.Items.Add(new ComboBoxItem
				{
					Content = $"{value2}x{value3}"
				});
			}
			string text;
			int preferredWidth;
			int preferredHeight;
			if (value.Any(((int Width, int Height) item) => string.Equals($"{item.Width}x{item.Height}", preferred, StringComparison.OrdinalIgnoreCase)))
			{
				text = preferred;
			}
			else if (TryParseResolution(preferred, out preferredWidth, out preferredHeight))
			{
				(int, int) tuple2 = value.OrderBy(((int Width, int Height) item) => Math.Abs(item.Width - preferredWidth) + Math.Abs(item.Height - preferredHeight)).First();
				text = $"{tuple2.Item1}x{tuple2.Item2}";
			}
			else
			{
				text = $"{value[0].Item1}x{value[0].Item2}";
			}
			ResolutionComboBox.Text = text;
			SyncCustomResolutionInputs(text);
		}
		finally
		{
			updatingResolutionOptions = false;
		}
	}

	private void AspectRatioComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!updatingResolutionOptions)
		{
			PopulateResolutionOptions(SelectedTag(AspectRatioComboBox) ?? "custom", CurrentResolutionText());
		}
	}

	private string CurrentResolutionText()
	{
		if (!int.TryParse(CustomWidthTextBox.Text, out var result) || !int.TryParse(CustomHeightTextBox.Text, out var result2))
		{
			return ResolutionComboBox.Text;
		}
		return $"{result}x{result2}";
	}

	private void SyncCustomResolutionInputs(string? resolutionText)
	{
		if (TryParseResolution(resolutionText, out var width, out var height))
		{
			CustomWidthTextBox.Text = width.ToString();
			CustomHeightTextBox.Text = height.ToString();
		}
	}

	private void ResolutionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!updatingResolutionOptions)
		{
			string text = (ResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
			SyncCustomResolutionInputs(text ?? ResolutionComboBox.Text);
		}
	}

	private void PopulateMonitors(string device)
	{
		MonitorComboBox.Items.Clear();
		MonitorComboBox.Items.Add(new ComboBoxItem
		{
			Content = "Follow Primary Display",
			Tag = ""
		});
		bool flag = string.IsNullOrEmpty(device);
		foreach (DisplayMonitor.Entry item in DisplayMonitor.GetConnected())
		{
			MonitorComboBox.Items.Add(new ComboBoxItem
			{
				Content = item.Label,
				Tag = item.Device
			});
			flag |= string.Equals(item.Device, device, StringComparison.OrdinalIgnoreCase);
		}
		if (!flag)
		{
			MonitorComboBox.Items.Add(new ComboBoxItem
			{
				Content = device + " (disconnected - using the primary display)",
				Tag = device
			});
		}
		SelectTag(MonitorComboBox, device);
	}

	private void MonitorComboBox_OnDropDownOpened(object sender, EventArgs e)
	{
		PopulateMonitors(SelectedTag(MonitorComboBox) ?? "");
	}

	private int gpuPreferenceShown;

	private static string GameExe => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "ago.exe"));

	private static string LauncherSettingsPath => Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");

	/// <summary>
	/// The three GPU preferences, each named with the adapter Windows picks for it, showing the one saved for the game
	/// after the first start's High performance. A card Cloud23333's launcher picked is named under the box: his launch
	/// script routes the game to it ahead of this preference.
	/// </summary>
	private void ShowGpuPreference()
	{
		if (GpuCard.Available(GamePaths.GameRoot))
		{
			ShowGpuCard();
			return;
		}
		gpuCard = null;
		GpuCardRefreshButton.Visibility = Visibility.Collapsed;
		GpuPreferenceComboBox.ToolTip = "The game's GPU preference, the same setting as Windows' own Graphics settings page.";
		GpuPreferenceComboBox.IsEnabled = true;
		try
		{
			GpuPreference.WriteOnce(LauncherSettingsPath, GameExe);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException || ex is System.Security.SecurityException)
		{
			AppendServerControlLog("The GPU preference was not set to High performance: " + ex.Message);
		}
		gpuPreferenceShown = GpuPreference.Read(GameExe);
		GpuPreferenceComboBox.Items.Clear();
		foreach (int preference in new int[3] { GpuPreference.LetWindowsDecide, GpuPreference.PowerSaving, GpuPreference.HighPerformance })
		{
			GpuPreferenceComboBox.Items.Add(new ComboBoxItem
			{
				Content = GpuPreference.Label(preference, GpuPreference.AdapterFor(preference)),
				Tag = preference.ToString(CultureInfo.InvariantCulture)
			});
		}
		SelectTag(GpuPreferenceComboBox, gpuPreferenceShown.ToString(CultureInfo.InvariantCulture));
		string? hisPick = null;
		try
		{
			hisPick = (File.Exists(LauncherSettingsPath) && JsonNode.Parse(File.ReadAllText(LauncherSettingsPath)) is JsonObject settings) ? GpuPreference.HisPick(settings) : null;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException)
		{
		}
		GpuPreferenceHelpText.Text = "Set when you save or press Play; used the next time the game starts." + ((hisPick == null) ? "" : (" Cloud23333's launcher has a graphics card picked" + ((hisPick.Length == 0) ? "" : (", " + hisPick)) + ", which the game uses instead; clear it there to use this one."));
	}

	/// <summary>Writes the picked GPU preference for the game when it differs from the one shown; his card pick replaces it.</summary>
	private void ApplyGpuPreference()
	{
		if (gpuCard != null)
		{
			return;
		}
		int picked = int.TryParse(SelectedTag(GpuPreferenceComboBox), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : gpuPreferenceShown;
		if (picked != gpuPreferenceShown)
		{
			GpuPreference.Write(GameExe, picked);
			gpuPreferenceShown = picked;
		}
	}

	private GpuCard? gpuCard;

	/// <summary>
	/// Cloud23333's card pick, on a game folder with his App\gpu-runtime: the saved pick at once, then the cards his
	/// probe lists, read off the UI thread as his launcher reads them.
	/// </summary>
	private void ShowGpuCard()
	{
		gpuCard = new GpuCard();
		JsonObject? graphics = null;
		try
		{
			graphics = (File.Exists(LauncherSettingsPath) && JsonNode.Parse(File.ReadAllText(LauncherSettingsPath)) is JsonObject settings) ? (settings["graphics"] as JsonObject) : null;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException)
		{
		}
		gpuCard.Load(graphics);
		GpuCardRefreshButton.Visibility = Visibility.Visible;
		GpuPreferenceComboBox.ToolTip = "The graphics card the game starts on; Cloud23333's launcher shows the same pick.";
		ShowGpuCardOptions();
		_ = DetectGpuCardsAsync(gpuCard);
	}

	private void ShowGpuCardOptions()
	{
		if (gpuCard == null)
		{
			return;
		}
		bool layerOn = GpuCompat.Current == GpuCompat.Choice.Fluphus;
		GpuPreferenceComboBox.Items.Clear();
		foreach (GpuCard.Option option in gpuCard.Options)
		{
			ComboBoxItem item = new ComboBoxItem
			{
				Content = option.Label,
				DataContext = option
			};
			GpuPreferenceComboBox.Items.Add(item);
			if (option == gpuCard.Selected)
			{
				GpuPreferenceComboBox.SelectedItem = item;
			}
		}
		GpuPreferenceComboBox.IsEnabled = !layerOn;
		GpuPreferenceHelpText.Text = gpuCard.Status(layerOn);
	}

	private async Task DetectGpuCardsAsync(GpuCard card)
	{
		GpuCardRefreshButton.IsEnabled = false;
		string runtime = GpuCard.RuntimeDirectory(GamePaths.GameRoot);
		try
		{
			card.Detected(await Task.Run(() => GpuCard.ListAdapters(runtime)));
		}
		catch (Exception ex)
		{
			card.Failed(ex.Message);
		}
		finally
		{
			GpuCardRefreshButton.IsEnabled = true;
		}
		if (card == gpuCard)
		{
			SelectGpuCard();
			ShowGpuCardOptions();
		}
	}

	private async void GpuCardRefreshButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (gpuCard != null)
		{
			SelectGpuCard();
			await DetectGpuCardsAsync(gpuCard);
		}
	}

	/// <summary>Takes the card picked in the box; the entry shown for the saved pick keeps it as saved.</summary>
	private void SelectGpuCard()
	{
		if (gpuCard != null && GpuPreferenceComboBox.SelectedItem is ComboBoxItem { DataContext: GpuCard.Option option } && option != gpuCard.Selected)
		{
			gpuCard.Select(option.Luid);
		}
	}

	/// <summary>His three keys in the graphics object; none while the AMD layer is on, as his routing does not start beside it.</summary>
	private void WriteGpuCard(JsonObject graphics, bool layerOn)
	{
		if (gpuCard == null)
		{
			return;
		}
		SelectGpuCard();
		if (layerOn)
		{
			gpuCard.Select("");
		}
		gpuCard.Save(graphics);
	}

	private bool showingWidescreen;

	/// <summary>The three Widescreen switches from the Settings file's "widescreen" object; defaults on, on, off.</summary>
	private void ShowWidescreen(JsonObject? widescreen)
	{
		showingWidescreen = true;
		WidescreenSwitch.IsChecked = widescreen?["enable"]?.GetValue<bool>() ?? true;
		VertPlusSwitch.IsChecked = widescreen?["vertPlus"]?.GetValue<bool>() ?? true;
		HudAnchorSwitch.IsChecked = widescreen?["hudAnchor"]?.GetValue<bool>() ?? false;
		showingWidescreen = false;
	}

	/// <summary>Writes the three switches; anchors and anchorFix, set by hand, are kept as saved.</summary>
	private void WriteWidescreen(JsonObject settings)
	{
		JsonObject widescreen = (settings["widescreen"] as JsonObject) ?? new JsonObject();
		widescreen["enable"] = WidescreenSwitch.IsChecked == true;
		widescreen["vertPlus"] = VertPlusSwitch.IsChecked == true;
		widescreen["hudAnchor"] = HudAnchorSwitch.IsChecked == true;
		if (settings["widescreen"] != widescreen)
		{
			settings["widescreen"] = widescreen;
		}
	}

	private void WidescreenSwitch_OnChanged(object sender, RoutedEventArgs e)
	{
		if (!showingWidescreen && SaveLauncherSettings(out string _, out int _, out int _, out string _, out int _))
		{
			HoldStatus("Widescreen settings saved - they take effect the next time the game starts.");
		}
	}
}
