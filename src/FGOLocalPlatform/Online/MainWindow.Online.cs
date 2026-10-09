using System;
using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	/// <summary>Why the last Play Online did not reach the game, shown on the Online page until the next Play Online.</summary>
	private string? onlineProblem;

	private DateTime onlineStatusHoldUntil = DateTime.MinValue;

	/// <summary>Writes the Online page's Status sentence and colours its dot.</summary>
	private void ShowOnlineStatus(string text, string tone)
	{
		OnlineStatusText.Text = text;
		OnlineStatusDot.Fill = ToneBrush(tone);
	}

	/// <summary>A problem the Online Status keeps for the player to read before the two-second tick replaces it.</summary>
	private void HoldOnlineStatus(string text)
	{
		ShowOnlineStatus(text, "problem");
		onlineStatusHoldUntil = DateTime.UtcNow.AddSeconds(12.0);
	}

	/// <summary>The Online page on the status tick: its sentence, its buttons and its progress line.</summary>
	private void ShowOnlinePage(bool gameRunning, bool busy, CommunityServer.Settings community, string? card, string? cardProblem)
	{
		(string Text, string Tone)? probe = (card != null) ? ProbeStatus(community, card) : null;
		(string text, string tone) = startupChecking ? ("Checking the game folder. Play Online is ready in a moment.", "busy")
			: (installProblem != null) ? (installProblem, "problem")
			: gameRunning ? ("The game is running. Stop game ends it and closes its window.", "ready")
			: launcherProcessRunning ? ("The game is starting. The title screen appears in about a minute.", "busy")
			: (onlineProblem != null) ? (onlineProblem, "problem")
			: (community.Host.Length == 0) ? ("Online needs the community server's address. Save it under Community server below, then press Play Online.", "next")
			: (cardProblem != null) ? (cardProblem, "problem")
			: (card == null) ? ("This PC has no account on " + community.Host + " yet. Choose New account, or Recover account if you have a recovery key.", "next")
			: (probe is { Tone: not "ready" }) ? probe.Value
			: (cardCollection.SelectedCards.Count == 0) ? ("The deck is empty. Pick up to 30 cards on Cards, then press Play Online.", "next")
			: probe ?? ("Ready to play online on " + community.Host + ". Press Play Online.", "ready");
		if (!busy && !(playStarting && sessionOnline) && DateTime.UtcNow >= onlineStatusHoldUntil)
		{
			ShowOnlineStatus(text, tone);
		}
		CheckAgainButton.Visibility = (card != null && !gameRunning && !launcherProcessRunning) ? Visibility.Visible : Visibility.Collapsed;
		CheckAgainButton.IsEnabled = !probing && !playStarting;
		OnlineStatusProgress.Visibility = (startupChecking || probing || (playStarting && sessionOnline) || (launcherProcessRunning && !gameRunning && sessionOnline)) ? Visibility.Visible : Visibility.Hidden;
		PlayOnlineButton.IsEnabled = !gameRunning && !launcherProcessRunning && !busy && !startupChecking && installProblem == null && community.Host.Length > 0;
		OnlineStopGameButton.IsEnabled = gameRunning || launcherProcessRunning;
	}

	private async void PlayOnlineButton_OnClick(object sender, RoutedEventArgs e)
	{
		await StartSessionAsync(online: true);
	}

	private async void CheckAgainButton_OnClick(object sender, RoutedEventArgs e)
	{
		onlineProblem = null;
		await ProbeCommunityServerAsync(again: true);
	}

	private async void CommunityServerPanel_OnSaved(object? sender, EventArgs e)
	{
		onlineProblem = null;
		await RefreshRuntimeStatusAsync();
		await ProbeCommunityServerAsync();
	}
}
