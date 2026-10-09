using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FGOLocalPlatform;

/// <summary>
/// The Photo Mode page. The panel opens the game's model archives, so it is built the first time the page is shown,
/// against the game started from this install when one is running.
/// </summary>
public partial class PhotoModePage : UserControl
{
	public PhotoModePage()
	{
		InitializeComponent();
	}

	private void Page_OnLoaded(object sender, RoutedEventArgs e)
	{
		if (Panel.Content != null)
		{
			return;
		}
		int? gamePid = null;
		foreach (Process process in Process.GetProcessesByName("ago"))
		{
			using (process)
			{
				try
				{
					if (string.Equals(process.MainModule?.FileName, Path.Combine(GamePaths.GameRoot, "ago.exe"), StringComparison.OrdinalIgnoreCase))
					{
						gamePid = process.Id;
						break;
					}
				}
				catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
				{
				}
			}
		}
		PhotoWindow panel = new PhotoWindow(Window.GetWindow(this), Path.Combine(GamePaths.GameRoot, "fgo-launcher.json"), gamePid);
		panel.Background = Brushes.Transparent;
		panel.Foreground = (Brush)FindResource("TextBrush");
		Panel.Content = panel;
	}
}
