using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FGOLocalPlatform;

/// <summary>
/// The settings files Cloud23333's launcher and updater also write. When one changed while this window was in the
/// background, the pages read them again as it comes back, so a later save does not write back what they showed before.
/// </summary>
public partial class MainWindow
{
	private Dictionary<string, DateTime>? sharedSettingsStamps;

	private static string[] SharedSettingsFiles()
	{
		string app = GamePaths.GameRoot;
		return new string[5]
		{
			Path.Combine(app, "fgo-launcher.json"),
			Path.Combine(app, "segatools.ini"),
			Path.Combine(app, "graphics-effects.ini"),
			Path.Combine(app, "audio-volume.ini"),
			EventSwitches.SettingsPath(Path.GetFullPath(Path.Combine(app, "..", "Server")))
		};
	}

	private static Dictionary<string, DateTime> StampSharedSettings()
	{
		return SharedSettingsFiles().ToDictionary((string path) => path, (string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue);
	}

	private void Window_OnDeactivated(object? sender, EventArgs e)
	{
		sharedSettingsStamps = StampSharedSettings();
	}

	private void Window_OnActivated(object? sender, EventArgs e)
	{
		Dictionary<string, DateTime>? before = sharedSettingsStamps;
		sharedSettingsStamps = null;
		if (before != null && StampSharedSettings().Any((KeyValuePair<string, DateTime> file) => before[file.Key] != file.Value))
		{
			LoadLauncherSettings();
			ControlsPanel.LoadBindings(Path.Combine(GamePaths.GameRoot, "segatools.ini"));
			BattleEffectsPanel.Reload();
			AudioPanel.Reload();
			CursorPanel.Reload();
			EventsPanel.Reload();
		}
	}
}
