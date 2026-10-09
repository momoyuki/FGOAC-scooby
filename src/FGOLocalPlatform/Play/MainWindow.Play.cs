using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	/// <summary>
	/// The Play page shows the first card of the deck, which is the Servant the cabinet reads first.
	/// With an empty deck it shows the game's own mark instead.
	/// </summary>
	private void UpdateHeroCard()
	{
		Card lead = cardCollection.SelectedCards.FirstOrDefault();
		HeroCardImage.Source = lead?.Thumbnail;
		bool haveCard = HeroCardImage.Source != null;
		HeroCardImage.Visibility = (haveCard ? Visibility.Visible : Visibility.Collapsed);
		HeroFallbackImage.Visibility = (haveCard ? Visibility.Collapsed : Visibility.Visible);
	}

	private void StatusMenuButton_OnClick(object sender, RoutedEventArgs e)
	{
		PillStartServerItem.IsEnabled = !localToolMissing;
		PillStopServerItem.IsEnabled = !localToolMissing;
		PillUpdateLaterItem.Visibility = (nextStep == NextStep.Install) ? Visibility.Visible : Visibility.Collapsed;
		ContextMenu menu = StatusMenuButton.ContextMenu;
		menu.PlacementTarget = StatusMenuButton;
		menu.Placement = PlacementMode.Bottom;
		menu.IsOpen = true;
	}

	private void EditDeckButton_OnClick(object sender, RoutedEventArgs e)
	{
		SectionTabs.SelectedItem = CardsTab;
	}
}
