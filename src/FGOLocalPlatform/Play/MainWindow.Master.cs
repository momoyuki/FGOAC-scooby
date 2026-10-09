using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private string masterLineShown = "";

	/// <summary>Writes the Master line: the local account in use.</summary>
	private void ShowMasterLine()
	{
		string name;
		string detail = "";
		AccountEntry? current = (AccountComboBox.ItemsSource as IEnumerable<AccountEntry>)?.FirstOrDefault((AccountEntry account) => account.IsCurrent);
		if (current != null)
		{
			name = current.MasterName + "  ";
			detail = "ID " + current.AimeId;
		}
		else
		{
			name = !accountsRead ? "Reading accounts..." : (localToolMissing ? "Local accounts need the Server folder" : "No Master selected");
		}
		string shown = name + "|" + detail;
		if (shown != masterLineShown)
		{
			masterLineShown = shown;
			MasterLineText.Inlines.Clear();
			MasterLineText.Inlines.Add(new Run(name));
			if (detail.Length > 0)
			{
				MasterLineText.Inlines.Add(new Run(detail)
				{
					FontFamily = (FontFamily)FindResource("DigitFont")
				});
			}
		}
	}

	private void MasterLineButton_OnClick(object sender, RoutedEventArgs e)
	{
		ContextMenu menu = new ContextMenu
		{
			PlacementTarget = MasterLineButton,
			Placement = PlacementMode.Bottom
		};
		List<AccountEntry> accounts = (AccountComboBox.ItemsSource as IEnumerable<AccountEntry>)?.ToList() ?? new List<AccountEntry>();
		foreach (AccountEntry account in accounts)
		{
			MenuItem item = MenuEntry(account.MasterName + "   ID " + account.AimeId, delegate
			{
				AccountComboBox.SelectedItem = account;
				UseAccountButton_OnClick(MasterLineButton, new RoutedEventArgs());
			});
			item.IsChecked = account.IsCurrent;
			item.IsEnabled = !lastKnownGameRunning && !accountToolRunning;
			menu.Items.Add(item);
		}
		if (accounts.Count > 0)
		{
			menu.Items.Add(new Separator());
		}
		MenuItem create = MenuEntry("New account", delegate
		{
			CreateAccountButton_OnClick(MasterLineButton, new RoutedEventArgs());
		});
		create.IsEnabled = !localToolMissing;
		menu.Items.Add(create);
		menu.IsOpen = true;
	}

	/// <summary>The Master line's Manage button: the accounts page in one click.</summary>
	private void ManageAccountsButton_OnClick(object sender, RoutedEventArgs e)
	{
		SectionTabs.SelectedItem = AccountTab;
	}

	private static MenuItem MenuEntry(string header, Action onClick)
	{
		MenuItem item = new MenuItem
		{
			Header = header
		};
		item.Click += delegate
		{
			onClick();
		};
		return item;
	}
}
