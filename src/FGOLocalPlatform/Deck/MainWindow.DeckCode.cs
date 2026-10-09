using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private void DeckCodeCopyButton_OnClick(object sender, RoutedEventArgs e)
	{
		int count = cardCollection.SelectedCards.Count;
		if (count == 0)
		{
			HoldStatus("The deck is empty - nothing to copy.");
			return;
		}
		string code = DeckCode.Encode(cardCollection.SelectedCards.Select(DeckCode.SlotOf).ToList(), LoadoutComboBox.SelectedItem as string ?? "");
		try
		{
			Clipboard.SetText(code);
		}
		catch (ExternalException)
		{
			HoldStatus("The clipboard is busy. Press Copy code again.", problem: true);
			return;
		}
		HoldStatus($"Deck code copied: {Cards(count)}");
	}

	private void DeckCodeImportButton_OnClick(object sender, RoutedEventArgs e)
	{
		ImportDeckCode();
	}

	private void DeckCodeTextBox_OnKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			e.Handled = true;
			ImportDeckCode();
		}
	}

	private void DeckCodeTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
	{
		DeckCodeHint.Visibility = (DeckCodeTextBox.Text.Length == 0) ? Visibility.Visible : Visibility.Collapsed;
	}

	/// <summary>
	/// Fills the deck from the pasted code with the cards this PC has images for, in the code's order, and
	/// saves it as a loadout under the code's name, never over a loadout already there.
	/// </summary>
	private void ImportDeckCode()
	{
		if (DeckCodeTextBox.Text.Trim().Length == 0)
		{
			HoldStatus("Paste a deck code first.");
			return;
		}
		DeckCode.Reading reading = DeckCode.Decode(DeckCodeTextBox.Text);
		if (reading.Refusal != null)
		{
			HoldStatus(reading.Refusal, problem: true);
			return;
		}
		// Each card image is one card id and kind; the lowest file name wins should two ever share them.
		Dictionary<(int, bool), string> images = new Dictionary<(int, bool), string>();
		foreach (Card card in cardCollection.Cards.Concat(cardCollection.SelectedCards).Where((Card c) => c.CopyNumber == 1).OrderBy((Card c) => c.FileName, StringComparer.OrdinalIgnoreCase))
		{
			images.TryAdd((card.TrcId, card.CardTypeId == 2), card.Path);
		}
		List<string> paths = new List<string>();
		List<int> copies = new List<int>();
		foreach (DeckCode.Slot slot in reading.Slots)
		{
			if (images.TryGetValue((slot.CardId, slot.CraftEssence), out string? path))
			{
				paths.Add(path);
				copies.Add(slot.Copy);
			}
		}
		int total = reading.Slots.Count;
		int missing = total - paths.Count;
		if (paths.Count == 0)
		{
			HoldStatus($"None of that deck's {Cards(total)} are on this PC.", problem: true);
			return;
		}
		cardCollection.Reload(paths, null, copies);
		ApplyOwnedCardFilter();
		PublishDeck();
		DeckCodeTextBox.Clear();
		int held = cardCollection.SelectedCards.Count;
		string status = $"Deck loaded from code: {held} of {Cards(total)}" + ((missing > 0) ? $", {missing} not on this PC" : "") + ((total - missing > held) ? $", {total - missing - held} more than the account owns" : "");
		string name = reading.Name.Trim();
		try
		{
			name = PresetFolder.FreeName(PresetFolder.Ensure(LoadoutFolder), (name.Length == 0) ? $"Imported {DateTime.Now:yyyy-MM-dd}" : name);
			AtomicFile.WriteAllText(PresetFolder.PathFor(LoadoutFolder, name), LoadoutJson(name));
			RefreshLoadoutList(name);
			HoldStatus(status + ". Saved as the loadout " + name + ".");
		}
		catch (Exception ex)
		{
			HoldStatus(status + ". The loadout could not be saved: " + ex.Message, problem: true);
		}
	}

	private static string Cards(int count)
	{
		return (count == 1) ? "1 card" : $"{count} cards";
	}
}
