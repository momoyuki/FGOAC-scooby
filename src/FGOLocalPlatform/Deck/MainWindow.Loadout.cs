using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private string LoadoutFolder => Path.Combine(GamePaths.GameRoot, "deck-loadouts");

	private void RefreshLoadoutList(string? select = null)
	{
		PresetFolder.Fill(LoadoutComboBox, LoadoutFolder, select);
		LoadoutChips.ItemsSource = LoadoutComboBox.ItemsSource;
	}

	/// <summary>
	/// A loadout holds card file names, never paths: every install's card folder holds the same
	/// names, so the file a player shares loads on someone else's machine.
	/// </summary>
	private string LoadoutJson(string name)
	{
		JsonArray cards = new JsonArray();
		foreach (Card card in cardCollection.SelectedCards)
		{
			cards.Add(new JsonObject
			{
				["file"] = card.FileName,
				["copy"] = card.CopyNumber
			});
		}
		return new JsonObject
		{
			["format"] = "fgoac-loadout",
			["version"] = 1,
			["name"] = name,
			["cards"] = cards
		}.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}) + Environment.NewLine;
	}

	private static bool LooksLikeLoadout(string text)
	{
		try
		{
			return JsonNode.Parse(text) is JsonObject o && o["format"]?.GetValue<string>() == "fgoac-loadout" && o["cards"] is JsonArray;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private void LoadoutSaveButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (cardCollection.SelectedCards.Count == 0)
		{
			HoldStatus("The deck is empty - nothing to save.");
			return;
		}
		NamePromptDialog prompt = new NamePromptDialog(this, "Save loadout", "Name for this deck", LoadoutComboBox.SelectedItem as string ?? "");
		if (DialogSink.ShowDialog(prompt) != true || prompt.Result == null)
		{
			return;
		}
		string target = PresetFolder.PathFor(PresetFolder.Ensure(LoadoutFolder), prompt.Result);
		if (File.Exists(target) && ThemedMessageBox.Show(this, "Replace the loadout \"" + prompt.Result + "\"?", "Save loadout", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			AtomicFile.WriteAllText(target, LoadoutJson(prompt.Result));
			RefreshLoadoutList(prompt.Result);
			HoldStatus("Loadout saved: " + prompt.Result + ". Export sends a copy to share.");
		}
		catch (Exception ex)
		{
			HoldStatus("The loadout could not be saved: " + ex.Message);
		}
	}

	private void ApplyLoadoutFile(string path)
	{
		if (!(JsonNode.Parse(File.ReadAllText(path)) is JsonObject loadout) || !(loadout["cards"] is JsonArray cards))
		{
			HoldStatus("That file is not a deck loadout.");
			return;
		}
		List<string> paths = new List<string>();
		List<int> copies = new List<int>();
		int missing = 0;
		foreach (JsonNode? entry in cards)
		{
			// GetFileName drops any folder part, so a shared file cannot point outside the card folder.
			string file = Path.GetFileName(entry?["file"]?.GetValue<string>() ?? "");
			if (file.Length == 0)
			{
				continue;
			}
			// Counted here rather than from what Reload keeps: Reload also stops at 30 cards and
			// drops duplicates, and neither of those is a card the player is missing.
			string cardPath = Path.Combine(cardCollection.Path, file);
			if (!File.Exists(cardPath))
			{
				missing++;
				continue;
			}
			paths.Add(cardPath);
			copies.Add(Math.Clamp(entry?["copy"]?.GetValue<int>() ?? 1, 1, 30));
		}
		if (paths.Count == 0)
		{
			HoldStatus((missing > 0) ? "None of that loadout's cards are in your card folder." : "That loadout holds no cards.");
			return;
		}
		cardCollection.Reload(paths, null, copies);
		ApplyOwnedCardFilter();
		PublishDeck();
		string name = loadout["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);
		HoldStatus($"Loadout loaded: {name} - {cardCollection.SelectedCards.Count} cards" + ((missing == 1) ? "; 1 not in your card folder was left out." : ((missing > 1) ? $"; {missing} not in your card folder were left out." : ".")));
	}

	private void LoadoutLoadButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (LoadoutComboBox.SelectedItem is string name)
		{
			LoadLoadout(name);
		}
	}

	/// <summary>A loadout's button above the deck: loads it and selects it in the list for Delete and Export.</summary>
	private void LoadoutChip_OnClick(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is string name)
		{
			LoadoutComboBox.SelectedItem = name;
			LoadLoadout(name);
		}
	}

	private void LoadLoadout(string name)
	{
		try
		{
			ApplyLoadoutFile(PresetFolder.PathFor(LoadoutFolder, name));
		}
		catch (Exception ex)
		{
			HoldStatus("The loadout could not be loaded: " + ex.Message);
		}
	}

	private void LoadoutDeleteButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (!(LoadoutComboBox.SelectedItem is string name) || ThemedMessageBox.Show(this, "Delete the loadout \"" + name + "\"?", "Delete loadout", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		File.Delete(PresetFolder.PathFor(LoadoutFolder, name));
		RefreshLoadoutList();
		HoldStatus("Loadout deleted: " + name);
	}

	private void LoadoutExportButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (!(LoadoutComboBox.SelectedItem is string name))
		{
			HoldStatus("Select a loadout to export, or Save as first.");
			return;
		}
		try
		{
			if (PresetFolder.Export(this, PresetFolder.PathFor(LoadoutFolder, name), name))
			{
				HoldStatus("Exported: " + name + ".json - send it to anyone with the launcher; they add it with Import.");
			}
		}
		catch (Exception ex)
		{
			HoldStatus("The loadout could not be exported: " + ex.Message);
		}
	}

	private void LoadoutImportButton_OnClick(object sender, RoutedEventArgs e)
	{
		try
		{
			string? name = PresetFolder.Import(this, LoadoutFolder, LooksLikeLoadout, "That file is not a deck loadout. Pick a .json file that was exported from the Loadouts row.");
			if (name == null)
			{
				return;
			}
			RefreshLoadoutList(name);
			ApplyLoadoutFile(PresetFolder.PathFor(LoadoutFolder, name));
		}
		catch (Exception ex)
		{
			HoldStatus("The loadout could not be imported: " + ex.Message);
		}
	}

	private void LoadoutFolderLink_OnClick(object sender, RoutedEventArgs e)
	{
		PresetFolder.OpenFolder(LoadoutFolder);
	}
}
