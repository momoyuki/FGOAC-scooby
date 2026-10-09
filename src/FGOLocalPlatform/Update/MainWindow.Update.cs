using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private Updater.Release availableUpdate;

	/// <summary>Set by Later in the Status menu: the release stays off the Status for this launcher run.</summary>
	private bool updateDismissed;

	private bool updateInstalling;

	/// <summary>
	/// Looks for a newer release, which the Status then offers with Install. At start-up and after a
	/// session a failure says nothing on screen and only reaches logs\update.log; the button on the
	/// About page reports either way, in one sentence. False when GitHub could not be asked.
	/// </summary>
	private async Task<bool> CheckForUpdateAsync(bool announce)
	{
		if (announce)
		{
			AboutUpdateStatusText.Text = "Checking...";
		}
		try
		{
			availableUpdate = await Updater.CheckAsync();
		}
		catch (Exception ex)
		{
			Updater.Log("Check failed: " + ex);
			if (announce)
			{
				AboutUpdateStatusText.Text = "The update check could not reach GitHub: " + ex.Message + ". Check your connection, or open " + UpdateSettings.ReleasesUrl + " yourself.";
			}
			return false;
		}
		if (announce)
		{
			AboutUpdateStatusText.Text = (availableUpdate == null) ? ("You are on the latest version (" + UpdateSettings.Version + ").") : ("Update " + availableUpdate.Version + " is available. Install it from the Status on Play.");
		}
		return true;
	}

	private async void CheckUpdatesButton_OnClick(object sender, RoutedEventArgs e)
	{
		Button button = (Button)sender;
		button.IsEnabled = false;
		try
		{
			// Asked for by hand, so a release put off with Later is offered again.
			updateDismissed = false;
			bool reached = await CheckForUpdateAsync(announce: true);
			if (availableUpdate == null)
			{
				// The Logs drawer reads the status line, so the answer goes there as well.
				HoldStatus(AboutUpdateStatusText.Text, !reached);
			}
			await RefreshRuntimeStatusAsync();
		}
		finally
		{
			button.IsEnabled = true;
		}
	}

	private async void UpdateLaterItem_OnClick(object sender, RoutedEventArgs e)
	{
		updateDismissed = true;
		await RefreshRuntimeStatusAsync();
	}

	private async void InstallUpdate()
	{
		if (availableUpdate == null || updateInstalling)
		{
			return;
		}
		Updater.Release release = availableUpdate;
		updateInstalling = true;
		ShowNextStep(NextStep.None);
		StartGameButton.IsEnabled = false;
		string installRoot = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."));
		// The installer's output arrives on a background thread.
		bool installed = await Updater.InstallAsync(release, installRoot, delegate(string message)
		{
			base.Dispatcher.Invoke(delegate
			{
				RuntimeStatusText.Text = message;
				AppendInjectionLog(message + Environment.NewLine);
			});
		}, CancellationToken.None);
		updateInstalling = false;
		if (!installed)
		{
			// The installer's last line says why; the release is offered again once it has been read.
			HoldStatus(RuntimeStatusText.Text, problem: true);
			await RefreshRuntimeStatusAsync();
			return;
		}
		availableUpdate = null;
		AboutUpdateStatusText.Text = "Version " + release.Version + " is installed.";
		if (Updater.HasStagedLauncher(out var stagedPath))
		{
			RuntimeStatusText.Text = "Version " + release.Version + " is ready - the launcher restarts to finish.";
			Updater.SwapAndRestart(stagedPath);
			Close();
			return;
		}
		HoldStatus("Version " + release.Version + " is installed. Close and reopen the launcher to run it.");
	}
}
