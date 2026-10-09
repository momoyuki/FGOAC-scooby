using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private bool communityBusy;

	/// <summary>
	/// Writes the Online page's account line and returns this PC's card for the saved server, if any. cardProblem is
	/// the Online Status sentence when the card cannot be read.
	/// </summary>
	private string? ShowOnlineAccount(CommunityServer.Settings community, out string? cardProblem)
	{
		cardProblem = null;
		string? card = null;
		if (community.Host.Length > 0)
		{
			try
			{
				card = CommunityServer.ReadCard(GamePaths.GameRoot, community.Host);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
			{
				cardProblem = "The account for " + community.Host + " could not be read: " + ex.Message;
			}
		}
		string? name = (card != null) ? CommunityServer.ReadMasterName(GamePaths.GameRoot, community.Host) : null;
		OnlineAccountText.Text = cardProblem ?? ((community.Host.Length == 0) ? "Save the server's address below first" : (card == null) ? ("None on " + community.Host + " yet") : (name != null) ? (name + ", ending " + card.Substring(16) + " on " + community.Host) : ("Ending " + card.Substring(16) + " on " + community.Host));
		return card;
	}

	private async void OnlineNewAccount_OnClick(object sender, RoutedEventArgs e)
	{
		await CommunityAccountAsync(recover: false);
	}

	private async void OnlineRecoverAccount_OnClick(object sender, RoutedEventArgs e)
	{
		await CommunityAccountAsync(recover: true);
	}

	/// <summary>New or Recover Account on the saved community server; refused from Play Online's start until the game ends.</summary>
	private async Task CommunityAccountAsync(bool recover)
	{
		// Both change the card Play Online is starting or running with, and the server refuses the old one from then on.
		if (playStarting || CommunityTunnelRunning)
		{
			ThemedMessageBox.Show("Stop the game first: a new or recovered account changes the code the game plays with, and it would lose its connection.", recover ? "Recover Account" : "New Account", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		CommunityServer.Settings saved = SavedServer();
		// The card's folder is named after the address, so a hand-edited one is checked before any card is read.
		try
		{
			_ = CommunityServer.NormalizeHost(saved.Host);
		}
		catch (ArgumentException ex)
		{
			ThemedMessageBox.Show((saved.Host.Length == 0) ? "Online needs the community server's address. Save it under Community server on this page first." : ex.Message, recover ? "Recover Account" : "New Account", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (recover)
		{
			await RecoverAccountAsync(saved);
		}
		else
		{
			await NewAccountAsync(saved);
		}
		await RefreshRuntimeStatusAsync();
		await ProbeCommunityServerAsync();
	}

	/// <summary>True when Play Online can go ahead: this PC holds an account for the saved server, or has just made one.</summary>
	private async Task<bool> EnsureCommunityAccountAsync()
	{
		CommunityServer.Settings settings = SavedServer();
		try
		{
			if (CommunityServer.ReadCard(GamePaths.GameRoot, settings.Host) != null)
			{
				return true;
			}
		}
		catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
		{
			ThemedMessageBox.Show(ex.Message, "Community Server", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return false;
		}
		if (ThemedMessageBox.Show("This PC has no account on the community server " + settings.Host + " yet.\n\nCreate a new account now? If you already have one, choose No and use Recover account on the Online page with your recovery key.", "Community Server", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return false;
		}
		return await CreateCommunityAccountAsync(settings);
	}

	private async Task<bool> CreateCommunityAccountAsync(CommunityServer.Settings settings)
	{
		// The dialog sends the request itself, so a refused name keeps it open for another.
		CommunityAccountDialog dialog = new CommunityAccountDialog(this, settings.Host, (string name, bool sendMachineId) => CommunityServer.CreateAccountAsync(settings, name, sendMachineId));
		if (DialogSink.ShowDialog(dialog) != true || dialog.Account == null)
		{
			if (dialog.Failure != null)
			{
				ThemedMessageBox.Show(dialog.Failure, "New Account", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			return false;
		}
		CommunityServer.Account account = dialog.Account;
		return await TakeCommunityAccountAsync(settings, () => Task.FromResult(account), "New Account", recovered: false);
	}

	/// <summary>Runs the account request, stores the card it brings and shows its recovery key once.</summary>
	private async Task<bool> TakeCommunityAccountAsync(CommunityServer.Settings settings, Func<Task<CommunityServer.Account>> request, string caption, bool recovered)
	{
		if (communityBusy)
		{
			return false;
		}
		communityBusy = true;
		OnlineNewAccountButton.IsEnabled = false;
		OnlineRecoverAccountButton.IsEnabled = false;
		try
		{
			// The key is shown even when the card cannot be stored: it is then the only copy of the account.
			(CommunityServer.Account account, string? storeError) = await CommunityServer.TakeAccountAsync(GamePaths.GameRoot, settings.Host, request());
			DialogSink.ShowDialog(new RecoveryKeyDialog(this, settings.Host, account.RecoveryKey, recovered));
			if (storeError != null)
			{
				ThemedMessageBox.Show((recovered ? "The account was recovered" : "The account was created") + ", but this PC could not store it: " + storeError + "\n\nFix that, then use Recover Account with the key you just saved.", caption, MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return false;
			}
			return true;
		}
		catch (IOException ex2)
		{
			ThemedMessageBox.Show(ex2.Message, caption, MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return false;
		}
		finally
		{
			communityBusy = false;
			OnlineNewAccountButton.IsEnabled = true;
			OnlineRecoverAccountButton.IsEnabled = true;
		}
	}

	/// <summary>New Account on the given saved settings: asks before replacing an account this PC already holds.</summary>
	private async Task NewAccountAsync(CommunityServer.Settings settings)
	{
		string? card;
		try
		{
			card = CommunityServer.ReadCard(GamePaths.GameRoot, settings.Host);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			ThemedMessageBox.Show(ex.Message, "New Account", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (card != null && ThemedMessageBox.Show("This PC already has an account on " + settings.Host + ". A new account takes its place here, and the old one comes back only with its recovery key.\n\nCreate a new account anyway?", "New Account", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		await CreateCommunityAccountAsync(settings);
	}

	/// <summary>Recover Account on the given saved settings; refused while the game runs through the tunnel.</summary>
	private async Task RecoverAccountAsync(CommunityServer.Settings settings)
	{
		if (CommunityTunnelRunning)
		{
			ThemedMessageBox.Show("Stop the game first: recovering the account changes its code, and the running game would lose its connection.", "Recover Account", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		string? card;
		try
		{
			card = CommunityServer.ReadCard(GamePaths.GameRoot, settings.Host);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			ThemedMessageBox.Show(ex.Message, "Recover Account", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		// Recovery rotates the code and the key on the server, so the card here is replaced whatever it holds.
		if (card != null && ThemedMessageBox.Show("This PC already has an account on " + settings.Host + ". The recovered account takes its place here; if that is another account, it comes back only with its own recovery key.\n\nRecover anyway?", "Recover Account", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		RecoverAccountDialog dialog = new RecoverAccountDialog(this, settings.Host);
		if (DialogSink.ShowDialog(dialog) != true)
		{
			return;
		}
		await TakeCommunityAccountAsync(settings, () => CommunityServer.RecoverAccountAsync(settings, dialog.RecoveryKey), "Recover Account", recovered: true);
	}
}
