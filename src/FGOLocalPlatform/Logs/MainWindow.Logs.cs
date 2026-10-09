using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	/// <summary>What the Logs drawer shows: the launcher log, the Game log or the local server's log.</summary>
	private enum LogSource
	{
		Launcher,
		Game,
		Server
	}

	private bool refreshingLogs;

	private LogSource logSource = LogSource.Game;

	/// <summary>The last file tail put in the shown Launcher or Server box.</summary>
	private string logTailSnapshot = "";

	private bool logsCollapsed = true;

	private double expandedLogWidth = 440.0;

	private static string ReadTail(string path, int maximumLines = 140, int maximumBytes = 196608)
	{
		if (!File.Exists(path))
		{
			return "Waiting for the log file: " + path;
		}
		try
		{
			using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			long num = Math.Max(0L, fileStream.Length - maximumBytes);
			fileStream.Seek(num, SeekOrigin.Begin);
			using StreamReader streamReader = new StreamReader(fileStream, Encoding.UTF8, true, 4096, false);
			if (num > 0)
			{
				streamReader.ReadLine();
			}
			string[] array = streamReader.ReadToEnd().Replace("\0", "").Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
			return string.Join(Environment.NewLine, array.Skip(Math.Max(0, array.Length - maximumLines)));
		}
		catch (IOException ex)
		{
			return "The log file is busy right now: " + ex.Message;
		}
		catch (UnauthorizedAccessException ex2)
		{
			return "Could not read the log: " + ex2.Message;
		}
	}

	private async Task RefreshLogPanelsAsync()
	{
		if (refreshingLogs || windowClosing)
		{
			return;
		}
		refreshingLogs = true;
		try
		{
			(string? tailPath, TextBox? tailBox) = logSource switch
			{
				LogSource.Launcher => (Path.Combine(GamePaths.LogsRoot, LauncherLog.FileName), LauncherLogTextBox),
				LogSource.Server => (Path.GetFullPath(Path.Combine(GamePaths.LogsRoot, "fgo.log")), ServerLogTextBox),
				_ => ((string?)null, (TextBox?)null)
			};
			string text = Path.Combine(GamePaths.LogsRoot, "fgo-inject-live.log");
			string injectionPath = (File.Exists(text) ? text : Path.Combine(GamePaths.LogsRoot, "fgo-last-launch.log"));
			bool readInjectionLog = !launcherLogOwned;
			var (text2, text3) = await Task.Run(() => ((tailPath != null) ? Redaction.Apply(ReadTail(tailPath)) : null, readInjectionLog ? Redaction.Apply(ReadTail(injectionPath)) : null));
			if (tailBox != null && text2 != null && !string.Equals(text2, logTailSnapshot, StringComparison.Ordinal))
			{
				logTailSnapshot = text2;
				tailBox.Text = text2;
				tailBox.ScrollToEnd();
			}
			if (!launcherLogOwned && text3 != null && !string.Equals(text3, injectionLogSnapshot, StringComparison.Ordinal))
			{
				injectionLogSnapshot = text3;
				InjectionLogTextBox.Text = text3;
				InjectionLogTextBox.ScrollToEnd();
			}
		}
		finally
		{
			refreshingLogs = false;
		}
	}

	private static void AppendServerControlLog(string text)
	{
		try
		{
			LauncherLog.Append(GamePaths.LogsRoot, text);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
		}
	}

	private void SetLogsCollapsed(bool collapsed)
	{
		logsCollapsed = collapsed;
		Grid logPanel = LogPanel;
		Visibility visibility = (LogPanelSplitter.Visibility = (collapsed ? Visibility.Collapsed : Visibility.Visible));
		logPanel.Visibility = visibility;
		LogPanelColumn.MinWidth = ((!collapsed) ? 310 : 0);
		LogPanelColumn.Width = new GridLength(collapsed ? 0.0 : expandedLogWidth);
		LogSplitterColumn.Width = new GridLength((!collapsed) ? 7 : 0);
	}

	private void ToggleLogs_OnClick(object sender, RoutedEventArgs e)
	{
		if (!logsCollapsed)
		{
			expandedLogWidth = Math.Max(310.0, LogPanelColumn.ActualWidth);
		}
		SetLogsCollapsed(!logsCollapsed);
		SaveLayoutSettings();
	}

	/// <summary>Shows one source in the Logs drawer; the next refresh fills its box.</summary>
	private void ShowLogSource(LogSource source)
	{
		logSource = source;
		logTailSnapshot = "";
		LauncherLogSourceButton.IsChecked = source == LogSource.Launcher;
		GameLogSourceButton.IsChecked = source == LogSource.Game;
		ServerLogSourceButton.IsChecked = source == LogSource.Server;
		LauncherLogTextBox.Visibility = (source == LogSource.Launcher) ? Visibility.Visible : Visibility.Collapsed;
		InjectionLogTextBox.Visibility = (source == LogSource.Game) ? Visibility.Visible : Visibility.Collapsed;
		ServerLogTextBox.Visibility = (source == LogSource.Server) ? Visibility.Visible : Visibility.Collapsed;
	}

	private async void LogSource_OnClick(object sender, RoutedEventArgs e)
	{
		ShowLogSource((sender == LauncherLogSourceButton) ? LogSource.Launcher : (sender == ServerLogSourceButton) ? LogSource.Server : LogSource.Game);
		SaveLayoutSettings();
		await RefreshLogPanelsAsync();
	}

	private void OpenLogsButton_OnClick(object sender, RoutedEventArgs e)
	{
		string logsRoot = GamePaths.LogsRoot;
		Directory.CreateDirectory(logsRoot);
		DialogSink.Open("explorer.exe", logsRoot);
	}
}
