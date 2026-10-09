using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DeckReaderUI;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private const int MaxSelection = 30;

	private readonly Config config;

	private void PublishDeck()
	{
		GameCommunication.GameRoot = GamePaths.GameRoot;
		config.CardsPath = cardCollection.Path;
		config.SelectedCards = cardCollection.GetSelectedPaths();
		config.SelectedCardCopies = cardCollection.SelectedCards.Select((Card c) => c.CopyNumber).ToList();
		config.Save(Path.Combine(GamePaths.GameRoot, "deck.json"));
		if (!GameCommunication.UpdateCards(cardCollection.SelectedCards))
		{
			HoldStatus("The deck is saved, but the running game could not take it now; the game reads the saved deck instead.");
		}
		UpdateDeckStatus();
	}

	private void UpdateDeckStatus()
	{
		DeckStatusText.Text = $"{cardCollection.SelectedCards.Count} of 30 cards";
		UpdateHeroCard();
	}

	private void AddSelectedCard()
	{
		Card card = (iconCardView ? selectedIconCard : (CardList.SelectedItem as CardStack)?.Card);
		if (card == null)
		{
			return;
		}
		CardVariantsWindow cardVariantsWindow = new CardVariantsWindow(card, cardCollection.Cards, cardCollection.SelectedCards, SelectedAccount?.OwnedCardCounts ?? new Dictionary<int, int>(), OwnedCardsOnlyCheckBox.IsChecked == true)
		{
			Owner = this
		};
		if (DialogSink.ShowDialog(cardVariantsWindow) != true)
		{
			return;
		}
		foreach (KeyValuePair<ushort, int> quantity in cardVariantsWindow.Quantities)
		{
			CardStack.Move(cardCollection, quantity.Key, quantity.Value, add: true);
		}
		ClearIconSelection();
		ApplyOwnedCardFilter();
		PublishDeck();
	}

	private void RemoveSelectedCard()
	{
		if (SelectedCardList.SelectedItem is CardStack cardStack)
		{
			int selectedIndex = SelectedCardList.SelectedIndex;
			CardQuantityWindow cardQuantityWindow = new CardQuantityWindow(cardStack.DisplayName, cardStack.Count, add: false)
			{
				Owner = this
			};
			if (DialogSink.ShowDialog(cardQuantityWindow) == true && CardStack.Move(cardCollection, cardStack.TrcId, cardQuantityWindow.Quantity, add: false) != 0)
			{
				cardListView.Refresh();
				selectedCardListView.Refresh();
				ApplyOwnedCardFilter();
				SelectedCardList.SelectedIndex = Math.Min(selectedIndex, selectedCardStacks.Count - 1);
				PublishDeck();
			}
		}
	}

	private void DeckCopyAdd_OnClick(object sender, RoutedEventArgs e)
	{
		ChangeDeckCopies((sender as FrameworkElement)?.Tag as CardStack, 1);
	}

	private void DeckCopyRemove_OnClick(object sender, RoutedEventArgs e)
	{
		ChangeDeckCopies((sender as FrameworkElement)?.Tag as CardStack, -1);
	}

	private void DeckRemoveAllCopies_OnClick(object sender, RoutedEventArgs e)
	{
		CardStack? stack = (sender as FrameworkElement)?.Tag as CardStack;
		ChangeDeckCopies(stack, -(stack?.Count ?? 0));
	}

	/// <summary>Adds or removes copies of one deck card from its tile, within the 30-card deck and the copies the account owns.</summary>
	private void ChangeDeckCopies(CardStack? stack, int change)
	{
		if (stack == null || change == 0)
		{
			return;
		}
		if (CardStack.Move(cardCollection, stack.TrcId, Math.Abs(change), change > 0) == 0)
		{
			HoldStatus((cardCollection.SelectedCards.Count >= MaxSelection) ? "The deck holds 30 cards, the most it can." : ("No more copies of " + stack.EnglishName + " can go in the deck: the account owns no more."));
			return;
		}
		cardListView.Refresh();
		selectedCardListView.Refresh();
		ApplyOwnedCardFilter();
		PublishDeck();
	}

	private void SelectedCardList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if ((SelectedCardList.SelectedItem as CardStack)?.Card != null)
		{
			CardList.UnselectAll();
			ClearIconSelection();
		}
	}

	private void SelectedCardList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		// Two quick presses on a tile's stepper are two steps, not a double-click on the tile.
		for (DependencyObject? node = e.OriginalSource as DependencyObject; node != null; node = ((node is Visual || node is Visual3D) ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node)))
		{
			if (node is ButtonBase)
			{
				return;
			}
		}
		RemoveSelectedCard();
	}

	private void ClearButton_OnClick(object sender, RoutedEventArgs e)
	{
		cardCollection.DeselectAll();
		ApplyOwnedCardFilter();
		PublishDeck();
	}
}
