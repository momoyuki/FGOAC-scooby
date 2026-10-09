using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	/// <summary>The card list's least height on the Deck tab: one row of card tiles.</summary>
	private const double LibraryRowHeight = 200.0;

	/// <summary>
	/// The Deck tab scrolls instead of squeezing the card list below one row: the frame's least height is its height
	/// with the list one row high. The header's height, which grows on a narrow window, is in it.
	/// </summary>
	private void DeckFrame_OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (!DeckFrame.IsVisible)
		{
			return;
		}
		double need = DeckFrame.ActualHeight - CardList.ActualHeight + LibraryRowHeight;
		if (Math.Abs(DeckFrame.MinHeight - need) > 0.5)
		{
			DeckFrame.MinHeight = need;
		}
	}

	private readonly List<CardStack> availableCardStacks = new List<CardStack>();

	private readonly List<CardStack> selectedCardStacks = new List<CardStack>();

	private string currentCardSearch = "";

	private List<CardStack> filteredCardItems = new List<CardStack>();

	private void ApplyOwnedCardFilter()
	{
		AccountEntry selectedAccount = SelectedAccount;
		if (selectedAccount != null && cardCollection.SyncOwnedCopies(selectedAccount.OwnedCardCounts))
		{
			selectedCardListView.Refresh();
			PublishDeck();
		}
		bool ownedOnly = OwnedCardsOnlyCheckBox.IsChecked == true && selectedAccount != null;
		availableCardStacks.Clear();
		availableCardStacks.AddRange(CardStack.BuildEntities(cardCollection.Cards.Concat(cardCollection.SelectedCards)));
		selectedCardStacks.Clear();
		selectedCardStacks.AddRange(CardStack.Build(cardCollection.SelectedCards));
		selectedCardListView.Refresh();
		HashSet<int> ownedIds = selectedAccount?.OwnedTradingCardIds ?? new HashSet<int>();
		int selectedCardType = SelectedCardTypeId();
		cardListView.Filter = delegate(object item)
		{
			if (!(item is CardStack cardStack))
			{
				return false;
			}
			Card card = cardStack.Card;
			if (selectedCardType != 0 && card.CardTypeId != selectedCardType)
			{
				return false;
			}
			if (ownedOnly && !cardStack.Variants.Any((Card c) => ownedIds.Contains(c.TrcId)))
			{
				return false;
			}
			if (currentCardSearch.Length == 0)
			{
				return true;
			}
			return cardStack.EnglishName.Contains(currentCardSearch, StringComparison.OrdinalIgnoreCase) || cardStack.JapaneseName.Contains(currentCardSearch, StringComparison.OrdinalIgnoreCase) || cardStack.EntityLabel.Contains(currentCardSearch, StringComparison.OrdinalIgnoreCase) || cardStack.Variants.Any((Card c) => c.FileName.Contains(currentCardSearch, StringComparison.OrdinalIgnoreCase) || c.TrcId.ToString().Contains(currentCardSearch));
		};
		cardListView.SortDescriptions.Clear();
		switch ((CardSortComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString())
		{
		case "1":
			cardListView.SortDescriptions.Add(new SortDescription("EnglishName", ListSortDirection.Ascending));
			break;
		case "2":
			cardListView.SortDescriptions.Add(new SortDescription("FirstTrcId", ListSortDirection.Ascending));
			break;
		}
		cardListView.Refresh();
		filteredCardItems = ((IEnumerable)cardListView).Cast<CardStack>().ToList();
		int count = filteredCardItems.Count;
		UpdateCardItemsSource();
		if (availableCardStacks.Count == 0)
		{
			CardCatalogSummaryText.Text = "No card images in " + Path.GetFullPath(Path.Combine(GamePaths.GameRoot, cardCollection.Path ?? "")) + ". They come with the full 1.0 package (DEVICE\\print\\FGO11_AllServants, 3,707 files), not with any update: extract that package into the game folder again, then click Reload.";
			return;
		}
		int value = availableCardStacks.Count((CardStack stack) => stack.Card.CardTypeId == 1);
		int value2 = availableCardStacks.Count((CardStack stack) => stack.Card.CardTypeId == 2);
		string value3 = selectedCardType switch
		{
			1 => $"Servants {value:N0}", 
			2 => $"Craft Essences {value2:N0}", 
			_ => $"Servants {value:N0} / Craft Essences {value2:N0}", 
		};
		CardCatalogSummaryText.Text = $"{value3} - {count:N0} shown - drag into the deck, or double-click to pick art and quantity";
	}

	private int SelectedCardTypeId()
	{
		if (CardTypeComboBox.SelectedItem is ComboBoxItem { Tag: var tag } && int.TryParse(tag?.ToString(), out var result))
		{
			return result;
		}
		return 0;
	}

	private void CardTypeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		ApplyOwnedCardFilter();
	}

	private void CardSortComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (cardListView != null)
		{
			ApplyOwnedCardFilter();
		}
	}

	private void OwnedCardsOnlyCheckBox_OnChanged(object sender, RoutedEventArgs e)
	{
		if (cardListView != null)
		{
			ApplyOwnedCardFilter();
		}
	}

	private void CardPickerSearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
	{
		if (cardListView != null && sender is TextBox textBox)
		{
			currentCardSearch = textBox.Text.Trim();
			ApplyOwnedCardFilter();
		}
	}
}
