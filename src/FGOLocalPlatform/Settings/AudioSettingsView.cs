using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class AudioSettingsView : UserControl, IComponentConnector
{
	private readonly string settingsPath;

	private readonly DispatcherTimer saveTimer;

	private bool ready;

	public AudioSettingsView()
		: this(Path.Combine(GamePaths.GameRoot, "audio-volume.ini"))
	{
	}

	public AudioSettingsView(string path)
	{
		settingsPath = path;
		InitializeComponent();
		saveTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(120.0)
		};
		saveTimer.Tick += delegate
		{
			Save();
		};
		Reload();
		base.Unloaded += delegate
		{
			if (saveTimer.IsEnabled)
			{
				Save();
			}
		};
	}

	/// <summary>Shows the volumes and the output channels as the file holds them now.</summary>
	public void Reload()
	{
		ready = false;
		string path = settingsPath;
		Dictionary<string, int> dictionary = new Dictionary<string, int>
		{
			{ "voice", 100 },
			{ "bgm", 100 },
			{ "effects", 100 }
		};
		try
		{
			if (File.Exists(path))
			{
				foreach (string item in File.ReadLines(path))
				{
					string[] array = item.Split('=', 2);
					if (array.Length == 2 && dictionary.ContainsKey(array[0]) && int.TryParse(array[1], out var result))
					{
						dictionary[array[0]] = Math.Clamp(result, 0, 100);
					}
				}
			}
			StatusText.Text = "Drag a slider to set the volume; your settings are kept automatically.";
		}
		catch (Exception ex)
		{
			StatusText.Text = "Could not read the volume settings: " + ex.Message;
		}
		VoiceSlider.Value = dictionary["voice"];
		BgmSlider.Value = dictionary["bgm"];
		EffectsSlider.Value = dictionary["effects"];
		// Only the hook of Cloud23333's 1.10 update reads the output channels.
		if (!GameBuild.Is1100(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", ".."))))
		{
			OutputChannelsRow.Visibility = Visibility.Visible;
			int channels = 2;
			try
			{
				channels = File.Exists(path) ? Channels(File.ReadAllText(path)) : 2;
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
			}
			OutputChannelsComboBox.SelectedIndex = (channels == 4) ? 1 : 0;
		}
		ready = true;
	}

	private void Volume_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (ready && !saveTimer.IsEnabled)
		{
			saveTimer.Start();
		}
	}

	public bool Save()
	{
		saveTimer.Stop();
		try
		{
			AtomicFile.WriteAllText(settingsPath, Merge(File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : "", (int)BgmSlider.Value, (int)VoiceSlider.Value, (int)EffectsSlider.Value));
			StatusText.Text = "Saved - applied live while the game is running, and kept for next time.";
			return true;
		}
		catch (Exception ex)
		{
			StatusText.Text = "Could not save the volume settings: " + ex.Message;
			return false;
		}
	}

	/// <summary>
	/// The file with the three volumes set in [audio] and every other key kept, such as the outputChannels Cloud23333's
	/// launcher writes there; a file with nothing in it gets the three keys alone.
	/// </summary>
	internal static string Merge(string text, int bgm, int voice, int effects)
	{
		if (text.Trim().Length == 0)
		{
			return $"[audio]\nbgm={bgm}\nvoice={voice}\neffects={effects}\n";
		}
		foreach ((string key, int value) in new (string, int)[3] { ("effects", effects), ("voice", voice), ("bgm", bgm) })
		{
			text = IniText.Set(text, "audio", key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}
		return text;
	}

	/// <summary>The file with [audio] outputChannels set to <paramref name="channels"/> and every other key kept.</summary>
	internal static string WithChannels(string text, int channels)
	{
		return IniText.Set(text, "audio", "outputChannels", (channels == 4) ? "4" : "2");
	}

	/// <summary>The output channels [audio] outputChannels asks for: 4, or 2 for anything else, as Cloud23333's launcher reads it.</summary>
	internal static int Channels(string text)
	{
		return (IniText.Parse(text).Section("audio").TryGetValue("outputChannels", out string? value) && value.Trim() == "4") ? 4 : 2;
	}

	private void OutputChannels_OnChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!ready)
		{
			return;
		}
		try
		{
			AtomicFile.WriteAllText(settingsPath, WithChannels(File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : "", (OutputChannelsComboBox.SelectedIndex == 1) ? 4 : 2));
			StatusText.Text = "Saved - the output takes effect the next time the game starts.";
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			StatusText.Text = "Could not save the output channels: " + ex.Message;
		}
	}

	private void SetAll(int value)
	{
		VoiceSlider.Value = value;
		BgmSlider.Value = value;
		EffectsSlider.Value = value;
		Save();
	}

	private void Mute_OnClick(object sender, RoutedEventArgs e)
	{
		SetAll(0);
	}

	private void Defaults_OnClick(object sender, RoutedEventArgs e)
	{
		SetAll(100);
	}
}
