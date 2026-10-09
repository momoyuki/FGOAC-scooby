using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private void AboutLink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
	{
		e.Handled = true;
		try
		{
			DialogSink.Open(e.Uri.AbsoluteUri);
		}
		catch (Exception ex)
		{
			ThemedMessageBox.Show(this, "Could not open the link: " + ex.Message, "About", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void SidebarLink_OnClick(object sender, RoutedEventArgs e)
	{
		try
		{
			DialogSink.Open(((FrameworkElement)sender).Tag.ToString());
		}
		catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
		{
			HoldStatus("The link could not be opened: " + ex.Message, problem: true);
		}
	}

	/// <summary>
	/// Fades the new page in and puts the focus on what the page is for. The Logs drawer is on Play and Online, with
	/// the Status of the page in view; showing Online probes the saved server when it has no probe yet.
	/// </summary>
	private void SectionTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (e.OriginalSource != SectionTabs)
		{
			return;
		}
		if (SectionTabs.Template?.FindName("PART_SelectedContentHost", SectionTabs) is UIElement host)
		{
			host.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150.0)));
		}
		bool online = SectionTabs.SelectedItem == OnlineTab;
		LogsDrawer.Visibility = (online || SectionTabs.SelectedItem == PlayTab) ? Visibility.Visible : Visibility.Collapsed;
		LogsStatusText.SetBinding(TextBlock.TextProperty, new Binding("Text")
		{
			Source = online ? OnlineStatusText : RuntimeStatusText
		});
		if (online && base.IsLoaded && !startupChecking)
		{
			_ = ProbeCommunityServerAsync();
		}
		base.Dispatcher.InvokeAsync(delegate
		{
			Control? target = (SectionTabs.SelectedItem == PlayTab) ? StartGameButton : online ? PlayOnlineButton : (SectionTabs.SelectedItem == CardsTab) ? SearchTextBox : (SectionTabs.SelectedItem == SettingsTab) ? SettingsFilterBox : null;
			if (target != null && target.IsEnabled && target.IsVisible)
			{
				target.Focus();
			}
		}, DispatcherPriority.Input);
	}

	/// <summary>Ctrl+1 to Ctrl+9 pick the pages shown in the sidebar, in order; Esc closes the Logs drawer.</summary>
	private void MainWindow_OnKeyDown(object sender, KeyEventArgs e)
	{
		if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D9)
		{
			List<TabItem> shown = SectionTabs.Items.OfType<TabItem>().Where((TabItem tab) => tab.Visibility == Visibility.Visible).ToList();
			int index = e.Key - Key.D1;
			if (index < shown.Count)
			{
				SectionTabs.SelectedItem = shown[index];
				e.Handled = true;
			}
		}
		else if (e.Key == Key.Escape && !logsCollapsed && LogsDrawer.IsVisible)
		{
			ToggleLogs_OnClick(this, e);
			e.Handled = true;
		}
	}
}
