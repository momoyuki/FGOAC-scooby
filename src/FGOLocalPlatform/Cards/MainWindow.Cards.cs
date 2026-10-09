using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private sealed record CardIconRow(IReadOnlyList<CardStack> Cards);

	private readonly DataTemplate cardListItemTemplate;

	private readonly ItemsPanelTemplate cardListItemsPanel;

	private readonly DataTemplate cardIconRowTemplate;

	private bool iconCardView;

	private int iconColumnCount;

	private Card? selectedIconCard;

	private Button? selectedIconButton;

	private void Reload()
	{
		string text = CardsPathTextBox.Text.Trim();
		if (text.Length != 0)
		{
			List<string> selectedPaths = cardCollection.GetSelectedPaths();
			List<int> selectedCopies = cardCollection.SelectedCards.Select((Card c) => c.CopyNumber).ToList();
			cardCollection.Path = text;
			cardCollection.Reload(selectedPaths, null, selectedCopies);
			cardListView.Refresh();
			selectedCardListView.Refresh();
			ApplyOwnedCardFilter();
			PublishDeck();
		}
	}

	private void SetCardViewMode(bool useIcons)
	{
		iconCardView = useIcons;
		ClearIconSelection();
		CardList.ItemTemplate = (useIcons ? cardIconRowTemplate : cardListItemTemplate);
		CardList.ItemsPanel = cardListItemsPanel;
		ScrollViewer.SetCanContentScroll(CardList, canContentScroll: true);
		VirtualizingPanel.SetIsVirtualizing(CardList, value: true);
		VirtualizingPanel.SetVirtualizationMode(CardList, VirtualizationMode.Recycling);
		SetViewButtonState(ListViewModeButton, !useIcons);
		SetViewButtonState(IconViewModeButton, useIcons);
		UpdateCardItemsSource();
	}

	private static void SetViewButtonState(Button button, bool active)
	{
		if (!active)
		{
			button.ClearValue(Control.BackgroundProperty);
			button.ClearValue(Control.BorderBrushProperty);
		}
		else
		{
			button.Background = (Brush)Application.Current.Resources["PlateHighBrush"];
			button.BorderBrush = (Brush)Application.Current.Resources["IceBrush"];
		}
	}

	private void UpdateCardItemsSource()
	{
		if (!iconCardView)
		{
			CardList.ItemsSource = (IEnumerable)cardListView;
			return;
		}
		int columns = Math.Max(1, (int)Math.Floor(Math.Max(144.0, CardList.ActualWidth - 18.0) / 144.0));
		iconColumnCount = columns;
		CardList.ItemsSource = (from item in filteredCardItems.Select((CardStack card, int index) => new { card, index })
			group item by item.index / columns into @group
			select new CardIconRow(@group.Select(item => item.card).ToArray())).ToList();
	}

	private void ListViewModeButton_OnClick(object sender, RoutedEventArgs e)
	{
		SetCardViewMode(useIcons: false);
	}

	private void IconViewModeButton_OnClick(object sender, RoutedEventArgs e)
	{
		SetCardViewMode(useIcons: true);
	}

	private void CardList_OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (iconCardView)
		{
			Size newSize = e.NewSize;
			if (Math.Max(1, (int)Math.Floor(Math.Max(144.0, newSize.Width - 18.0) / 144.0)) != iconColumnCount)
			{
				UpdateCardItemsSource();
			}
		}
	}

	private void IconCardButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: CardStack tag } button)
		{
			Card card = tag.Card;
			if (selectedIconButton != null && selectedIconButton != button)
			{
				selectedIconButton.ClearValue(Control.BackgroundProperty);
				selectedIconButton.ClearValue(Control.BorderBrushProperty);
			}
			selectedIconCard = card;
			selectedIconButton = button;
			button.Background = new SolidColorBrush(Color.FromRgb(53, 24, 50));
			button.BorderBrush = new SolidColorBrush(Color.FromRgb(168, 79, 150));
			SelectedCardList.UnselectAll();
		}
	}

	private void IconCardButton_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		IconCardButton_OnClick(sender, e);
		AddSelectedCard();
		e.Handled = true;
	}

	private void ClearIconSelection()
	{
		if (selectedIconButton != null)
		{
			selectedIconButton.ClearValue(Control.BackgroundProperty);
			selectedIconButton.ClearValue(Control.BorderBrushProperty);
		}
		selectedIconButton = null;
		selectedIconCard = null;
	}

	private async void BuildThumbnailCacheButton_OnClick(object sender, RoutedEventArgs e)
	{
		BuildThumbnailCacheButton.IsEnabled = false;
		List<Card> cards = (from @group in cardCollection.Cards.Concat(cardCollection.SelectedCards).GroupBy<Card, string>((Card card) => card.Path, StringComparer.OrdinalIgnoreCase)
			select @group.First()).ToList();
		try
		{
			var (value, num) = await Task.Run(() => Card.WarmThumbnailCache(cards, delegate(int processed, int total)
			{
				((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Func<object>)(() => BuildThumbnailCacheButton.Content = $"Caching {processed:N0}/{total:N0}"), Array.Empty<object>());
			}));
			BuildThumbnailCacheButton.Content = ((num == 0) ? $"Cached +{value:N0}" : $"Done +{value:N0} / failed {num:N0}");
		}
		finally
		{
			BuildThumbnailCacheButton.IsEnabled = true;
		}
	}

	private void CardList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!iconCardView && (CardList.SelectedItem as CardStack)?.Card != null)
		{
			SelectedCardList.UnselectAll();
		}
	}

	private void CardList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (!iconCardView)
		{
			ListView cardList = CardList;
			object originalSource = e.OriginalSource;
			if (ItemsControl.ContainerFromElement(cardList, (DependencyObject)((originalSource is DependencyObject) ? originalSource : null)) is ListViewItem)
			{
				e.Handled = true;
				AddSelectedCard();
			}
		}
	}

	private void ReloadButton_OnClick(object sender, RoutedEventArgs e)
	{
		Reload();
	}

	private void CardsPathTextBox_OnKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			Reload();
		}
	}
}
