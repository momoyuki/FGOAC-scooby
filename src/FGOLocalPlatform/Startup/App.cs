using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		// The test suites build the App for its resources only; its start would open the launcher in their process.
		if (DialogSink.Captured != null)
		{
			return;
		}
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		try
		{
			Directory.SetCurrentDirectory(GamePaths.GameRoot);
		}
		catch (Exception ex)
		{
			DialogSink.ShowSystemBox(ex.Message, "FGOAC scooby - Startup Check");
			Shutdown(4);
			return;
		}
		base.OnStartup(e);
		TakeUpdateBackup();
		// The window comes first; the install checks run behind it and report in its status line.
		MainWindow window = new MainWindow();
		WindowVisibility.KeepMaximizedOnWorkArea(window);
		DialogSink.Show(window);
		WindowVisibility.EnsureVisible(window);
		_ = window.CheckInstallAsync();
	}

	/// <summary>
	/// Before the window, whose start-up already writes the deck file and the settings file. A backup
	/// that fails is logged to logs\update.log and taken again at the next start.
	/// </summary>
	private static void TakeUpdateBackup()
	{
		string gameFolder = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."));
		try
		{
			string? folder = ProtectedSet.TakeBackup(gameFolder, UpdateSettings.Version);
			if (folder != null)
			{
				Updater.Log("Copied the protected set to " + folder + " before the first start of " + UpdateSettings.Version + ".");
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			Updater.Log("The protected set could not be copied to backup\\pre-" + UpdateSettings.Version + ": " + ex.Message);
		}
	}

	private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		ThemedMessageBox.Show(StartupDiagnostics.Explain((!(e.Exception is UnauthorizedAccessException)) ? 1 : 4) + "\n\n" + e.Exception, "FGOAC scooby - Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		e.Handled = true;
	}
}
