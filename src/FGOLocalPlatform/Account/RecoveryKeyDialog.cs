using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Win32;

namespace FGOLocalPlatform;

public partial class RecoveryKeyDialog : Window, IComponentConnector
{
	private readonly string host;

	private bool kept;

	/// <summary>Shows a new account's key, or with recovered set the key a recovery rotated to.</summary>
	public RecoveryKeyDialog(Window owner, string host, string recoveryKey, bool recovered)
	{
		InitializeComponent();
		WindowTheme.Apply(this);
		base.Owner = owner;
		this.host = host;
		HeadingText.Text = recovered ? ("Your account on " + host + " is back on this PC.") : ("Your account on " + host + " is ready.");
		NoticeText.Text = Notice(recovered);
		KeyTextBox.Text = recoveryKey;
		KeyTextBox.SelectAll();
		KeyTextBox.Focus();
	}

	public static string Notice(bool recovered)
	{
		string notice = "This key is the only way to get the account back if this PC loses it, and it is not shown again.";
		return recovered ? ("Recovering gave the account this new key, and the previous key no longer works. " + notice) : notice;
	}

	private void CopyButton_OnClick(object sender, RoutedEventArgs e)
	{
		try
		{
			Clipboard.SetText(KeyTextBox.Text);
			kept = true;
		}
		catch (ExternalException ex)
		{
			ThemedMessageBox.Show(this, "Could not copy the key: " + ex.Message + "\nSelect it and press Ctrl+C, or use Save to File.", "Recovery Key", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void SaveButton_OnClick(object sender, RoutedEventArgs e)
	{
		SaveFileDialog dialog = new SaveFileDialog
		{
			Title = "Save the recovery key",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			FileName = "FGOAC recovery key " + host + ".txt",
			Filter = "Text file (*.txt)|*.txt",
			DefaultExt = ".txt",
			AddExtension = true
		};
		if (DialogSink.ShowDialog(dialog, this) != true)
		{
			return;
		}
		try
		{
			File.WriteAllText(dialog.FileName, "Community server: " + host + "\r\nRecovery key: " + KeyTextBox.Text + "\r\n");
			kept = true;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			ThemedMessageBox.Show(this, "Could not save the key: " + ex.Message + "\nChoose another folder, or copy the key instead.", "Recovery Key", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void DoneButton_OnClick(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void Dialog_OnClosing(object? sender, CancelEventArgs e)
	{
		if (!kept && ThemedMessageBox.Show(this, "Close without saving the recovery key? It is not shown again.", "Recovery Key", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			e.Cancel = true;
		}
	}
}
