using System.Linq;
using System.Windows.Controls;

namespace FGOLocalPlatform;

/// <summary>The Accounts page's controls, under the names the Account partials use, and their handlers.</summary>
public partial class MainWindow
{
	private ComboBox AccountComboBox => AccountsPage.AccountComboBox;

	private TextBlock CurrentAccountText => AccountsPage.CurrentAccountText;

	private TextBlock CurrentAccessCodeText => AccountsPage.CurrentAccessCodeText;

	private Button UseAccountButton => AccountsPage.UseAccountButton;

	private Button CreateAccountButton => AccountsPage.CreateAccountButton;

	private ItemsControl AccountDetailsTable => AccountsPage.AccountDetailsTable;

	private Button ResetAccountButton => AccountsPage.ResetAccountButton;

	private Button DeleteAccountButton => AccountsPage.DeleteAccountButton;

	private Button RepairAccountButton => AccountsPage.RepairAccountButton;

	private Button GrantBenefitsButton => AccountsPage.GrantBenefitsButton;

	private Button RefreshAccountsButton => AccountsPage.RefreshAccountsButton;

	private Button SummonHistoryButton => AccountsPage.SummonHistoryButton;

	private TextBlock CurrentSummonSummaryText => AccountsPage.CurrentSummonSummaryText;

	private TextBlock AccountStatusText => AccountsPage.AccountStatusText;

	private WrapPanel AccountUpgradePanel => AccountsPage.AccountUpgradePanel;

	private void ConnectAccountsPage()
	{
		AccountComboBox.SelectionChanged += AccountComboBox_OnSelectionChanged;
		UseAccountButton.Click += UseAccountButton_OnClick;
		CreateAccountButton.Click += CreateAccountButton_OnClick;
		ResetAccountButton.Click += ResetAccountButton_OnClick;
		DeleteAccountButton.Click += DeleteAccountButton_OnClick;
		RepairAccountButton.Click += RepairAccountButton_OnClick;
		GrantBenefitsButton.Click += GrantBenefitsButton_OnClick;
		RefreshAccountsButton.Click += RefreshAccountsButton_OnClick;
		SummonHistoryButton.Click += SummonHistoryButton_OnClick;
		foreach (Button upgrade in AccountUpgradePanel.Children.OfType<Button>())
		{
			upgrade.Click += AccountUpgrade_OnClick;
		}
		ShowAccountTransfer();
	}
}
