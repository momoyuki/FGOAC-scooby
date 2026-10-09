using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;

namespace FGOLocalPlatform;

/// <summary>New Account: the Master name, chosen once, and the machine id box; Create sends the request and keeps the dialog open when the server refuses the name.</summary>
public partial class CommunityAccountDialog : Window, IComponentConnector
{
	private readonly Func<string, bool, Task<CommunityServer.Account>> create;

	private bool creating;

	/// <summary>The account the server made, once Create has succeeded.</summary>
	public CommunityServer.Account? Account { get; private set; }

	/// <summary>Why the request failed for a reason other than the name; the dialog closes with it.</summary>
	public string? Failure { get; private set; }

	public bool SendMachineId => MachineIdCheckBox.IsChecked == true;

	public CommunityAccountDialog(Window owner, string host, Func<string, bool, Task<CommunityServer.Account>> create)
	{
		InitializeComponent();
		WindowTheme.Apply(this);
		base.Owner = owner;
		this.create = create;
		LabelText.Text = "Get a new account on the community server " + host + ". It comes with a recovery key, shown once, that brings the account back on any PC.";
		NameTextBox.Focus();
	}

	private async void CreateButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (creating)
		{
			return;
		}
		string name = NameTextBox.Text;
		string? problem = CommunityServer.MasterNameProblem(name);
		if (problem != null)
		{
			ShowNameProblem(problem);
			return;
		}
		creating = true;
		CreateButton.IsEnabled = false;
		CancelButton.IsEnabled = false;
		NameProblemText.Visibility = Visibility.Collapsed;
		try
		{
			Account = await create(name, SendMachineId);
			creating = false;
			base.DialogResult = true;
		}
		catch (CommunityServer.MasterNameRefusedException ex)
		{
			creating = false;
			ShowNameProblem(ex.Message);
		}
		catch (Exception ex) when (ex is IOException || ex is ArgumentException)
		{
			creating = false;
			Failure = ex.Message;
			base.DialogResult = false;
		}
		finally
		{
			CreateButton.IsEnabled = true;
			CancelButton.IsEnabled = true;
		}
	}

	/// <summary>Says why the name was refused and selects it for the next try.</summary>
	private void ShowNameProblem(string text)
	{
		NameProblemText.Text = text;
		NameProblemText.Visibility = Visibility.Visible;
		NameTextBox.Focus();
		NameTextBox.SelectAll();
	}

	private void Window_OnClosing(object? sender, CancelEventArgs e)
	{
		// The request is on its way; the account it makes must reach this PC.
		e.Cancel = creating;
	}
}
