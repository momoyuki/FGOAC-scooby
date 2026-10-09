using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private bool refreshingStatus;

	private bool lastKnownGameRunning;

	private DateTime statusHoldUntil = DateTime.MinValue;

	/// <summary>A status sentence the player should get to read before the two-second tick replaces it.</summary>
	private void HoldStatus(string text, bool problem = false)
	{
		RuntimeStatusText.Text = text;
		SetStatusTone(problem ? "problem" : "next");
		statusHoldUntil = DateTime.UtcNow.AddSeconds(12.0);
	}

	/// <summary>Colours the dot beside the status sentence: ready, busy, problem, or a next step to take.</summary>
	private void SetStatusTone(string tone)
	{
		StatusDot.Fill = ToneBrush(tone);
	}

	private Brush ToneBrush(string tone)
	{
		return (Brush)FindResource(tone switch
		{
			"ready" => "IceBrush",
			"busy" => "InfoBrush",
			"problem" => "DangerBrush",
			_ => "WarnBrush",
		});
	}

	private async Task RefreshRuntimeStatusAsync()
	{
		if (refreshingStatus || windowClosing)
		{
			return;
		}
		refreshingStatus = true;
		try
		{
			bool gameRunning = (lastKnownGameRunning = IsThisGameRunning());
			CommunityServer.Settings community = SavedServer();
			ShowMasterLine();
			string? card = ShowOnlineAccount(community, out string? cardProblem);
			// The local server's own ports, asked while the game is idle: they name the Next step too.
			bool[]? serverPorts = null;
			if (!gameRunning && !launcherProcessRunning)
			{
				serverPorts = await Task.WhenAll(ServerSettingsView.ConfiguredPorts().Take(3).Select(IsPortOpenAsync));
			}
			string text;
			string tone;
			NextStep next = NextStep.None;
			if (startupChecking)
			{
				text = "Checking the game folder. Play is ready in a moment.";
				tone = "busy";
			}
			else if (savedReport != null)
			{
				text = "Report saved: " + savedReport + ". Attach it to your post in Discord.";
				tone = "ready";
				next = NextStep.OpenFolder;
			}
			else if (installProblem != null)
			{
				text = installProblem;
				tone = "problem";
			}
			else if (gameRunning)
			{
				text = "The game is running. Stop game ends it and closes its window.";
				tone = "ready";
			}
			else if (launcherProcessRunning)
			{
				text = "The game is starting. The title screen appears in about a minute.";
				tone = "busy";
			}
			else if (playProblem != null)
			{
				text = playProblem;
				tone = "problem";
			}
			else if (whatsNewBody != null)
			{
				text = "Updated to " + UpdateSettings.Version + ". What's new lists the changes.";
				tone = "ready";
				next = NextStep.WhatsNew;
			}
			else if (availableUpdate != null && !updateDismissed)
			{
				text = "Update " + availableUpdate.Version + " is available. Install puts it in place; the launcher restarts to finish.";
				tone = "next";
				next = NextStep.Install;
			}
			else
			{
				bool[] source = serverPorts ?? Array.Empty<bool>();
				bool anyAccount = (AccountComboBox.ItemsSource as IEnumerable<AccountEntry>)?.Any() == true;
				bool currentAccount = (AccountComboBox.ItemsSource as IEnumerable<AccountEntry>)?.Any((AccountEntry account) => account.IsCurrent) == true;
				(text, tone) = localToolMissing ? (CommunityServer.Offered ? "This copy has no local server. Play online on the Online page, or unpack the full game package." : "This copy has no local server. Unpack the full game package.", "problem")
					: (!anyAccount && !firstRunning) ? ("There is no local account yet. Choose New account in the Master menu, then press Play.", "next")
					: (!currentAccount && !firstRunning) ? ("No Master is selected. Pick one in the Master menu, then press Play.", "next")
					: (cardCollection.SelectedCards.Count == 0) ? ("The deck is empty. Pick up to 30 cards on Cards, then press Play.", "next")
					: source.All((bool value) => value) ? ("Local server running. Press Play.", "ready")
					: source.Any((bool value) => value) ? ("The local server answers on some of its ports but not all. Press Stop server, then press Play.", "problem")
					: ("Ready. Play starts the local server first.", "ready");
			}
			// Start or Stop server by hand whenever Local is idle and nothing else is the next step.
			if (next == NextStep.None && serverPorts != null && !localToolMissing && !startupChecking && installProblem == null)
			{
				next = serverPorts.Any((bool value) => value) ? NextStep.StopServer : NextStep.StartServer;
			}
			bool busy = serverConfiguring || stoppingServer || updateInstalling || windowClosing || firstRunning;
			if (!busy && DateTime.UtcNow >= statusHoldUntil)
			{
				RuntimeStatusText.Text = text;
				SetStatusTone(tone);
			}
			ShowNextStep((busy || playStarting) ? NextStep.None : next);
			StatusProgress.Visibility = (startupChecking || firstRunning || serverConfiguring || stoppingServer || updateInstalling || (playStarting && !sessionOnline) || (launcherProcessRunning && !gameRunning && !sessionOnline)) ? Visibility.Visible : Visibility.Hidden;
			StartGameButton.IsEnabled = !gameRunning && !launcherProcessRunning && !busy && !startupChecking && installProblem == null && !localToolMissing;
			StopGameButton.IsEnabled = gameRunning || launcherProcessRunning;
			ShowOnlinePage(gameRunning, busy, community, card, cardProblem);
			SetAccountControlsEnabled(!accountToolRunning);
		}
		finally
		{
			refreshingStatus = false;
		}
	}

	private static async Task<bool> IsPortOpenAsync(int port)
	{
		using TcpClient client = new TcpClient();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500.0));
		try
		{
			await client.ConnectAsync("127.0.0.1", port, timeout.Token);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
