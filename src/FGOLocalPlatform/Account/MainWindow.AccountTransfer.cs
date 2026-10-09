using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace FGOLocalPlatform;

/// <summary>Export all and Import on the Accounts page, offered where Cloud23333's account tool has the transfer commands.</summary>
public partial class MainWindow
{
	private void ShowAccountTransfer()
	{
		AccountsPage.AccountTransferPanel.Visibility = AccountTransfer.Offered(Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server"))) ? Visibility.Visible : Visibility.Collapsed;
		AccountsPage.ExportAccountsButton.Click += ExportAccountsButton_OnClick;
		AccountsPage.ImportAccountsButton.Click += ImportAccountsButton_OnClick;
	}

	/// <summary>The tool refuses while the game or the local server runs; says which and returns false.</summary>
	private async Task<bool> CanTransferAccountsAsync()
	{
		return !GameBlocksAccountAction() && !(await ServerBlocksAccountActionAsync());
	}

	/// <summary>Shows a refusal of the transfer commands in English; false when the tool succeeded.</summary>
	private bool ReportTransferFailure(AccountToolResult result, string operation)
	{
		if (result.Ok)
		{
			return false;
		}
		if (result.Root == null)
		{
			return ReportAccountToolFailure(result, operation);
		}
		AppendAccountLog(operation + " failed: " + result.Message);
		if (AccountTransfer.ServerRunning(result.Root))
		{
			lastKnownServerRunning = true;
			ShowServerRunningAccountWarning();
		}
		else
		{
			ThemedMessageBox.Show(this, AccountTransfer.Explain(result.Message), "Move accounts", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		return true;
	}

	private async void ExportAccountsButton_OnClick(object sender, RoutedEventArgs e)
	{
		await ExecuteAccountActionAsync(async delegate
		{
			if (!(await CanTransferAccountsAsync()))
			{
				return;
			}
			SaveFileDialog dialog = new SaveFileDialog
			{
				Title = "Export all local accounts",
				Filter = AccountTransfer.FileFilter,
				FileName = AccountTransfer.DefaultFileName(DateTime.Now)
			};
			if (DialogSink.ShowDialog(dialog, this) != true)
			{
				return;
			}
			AccountToolResult result = await RunAccountToolAsync(AccountTransfer.ExportArguments(dialog.FileName));
			if (!ReportTransferFailure(result, "export-accounts"))
			{
				AppendAccountLog("Every local account exported to " + dialog.FileName);
				ThemedMessageBox.Show(this, "Every local account is in " + dialog.FileName + ": its card, its whole save and its pending gifts. Keep the file somewhere safe.", "Export all", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		});
	}

	private async void ImportAccountsButton_OnClick(object sender, RoutedEventArgs e)
	{
		await ExecuteAccountActionAsync(async delegate
		{
			if (!(await CanTransferAccountsAsync()))
			{
				return;
			}
			OpenFileDialog dialog = new OpenFileDialog
			{
				Title = "Import local accounts",
				Filter = AccountTransfer.FileFilter
			};
			if (DialogSink.ShowDialog(dialog, this) != true)
			{
				return;
			}
			AccountToolResult preview = await RunAccountToolAsync(AccountTransfer.PreviewArguments(dialog.FileName));
			if (ReportTransferFailure(preview, "preview-accounts") || preview.Root == null)
			{
				return;
			}
			AccountTransfer.Preview plan = AccountTransfer.ReadPreview(preview.Root);
			if (ThemedMessageBox.Show(this, AccountTransfer.ConfirmText(plan), "Import", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes || !(await CanTransferAccountsAsync()))
			{
				return;
			}
			AccountToolResult result = await RunAccountToolAsync(AccountTransfer.ImportArguments(dialog.FileName, plan.Token));
			if (!ReportTransferFailure(result, "import-accounts") && result.Root != null)
			{
				AppendAccountLog("Accounts imported from " + dialog.FileName + "; backup: " + result.Root["backup"]);
				await RefreshAccountsAsync(showErrors: true);
				ThemedMessageBox.Show(this, AccountTransfer.ImportedText(result.Root), "Import", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		});
	}
}
