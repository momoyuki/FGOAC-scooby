using System;
using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private SummonSettingsWindow? summonSettingsPanel;

	private void SummonTab_OnSelected(object sender, RoutedEventArgs e)
	{
		if (sender != e.OriginalSource || summonSettingsPanel != null)
		{
			return;
		}
		try
		{
			summonSettingsPanel = new SummonSettingsWindow(GamePaths.GameRoot);
			SummonTabContent.Content = summonSettingsPanel;
			SummonTabProblemText.Visibility = Visibility.Collapsed;
		}
		catch (Exception ex)
		{
			SummonTabProblemText.Text = "Draw rates are a setting of the local server, and its files could not be read, so there is nothing to change here. Online play uses the community server's own rates.\n\n" + ex.Message;
			SummonTabProblemText.Visibility = Visibility.Visible;
		}
	}
}
