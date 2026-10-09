using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class SummonSettingsWindow : UserControl, IComponentConnector
{
	private readonly string settingsPath;

	private readonly List<SummonCardOption> cards = new List<SummonCardOption>();

	private ICollectionView? view;

	private bool batching;

	private bool dirty;

	private string? loadedText;

	public SummonSettingsWindow(string appRoot, string? configurationPath = null)
	{
		InitializeComponent();
		string fullPath = Path.GetFullPath(Path.Combine(appRoot, "..", "Server", "artemis"));
		settingsPath = configurationPath ?? Path.Combine(fullPath, "config", "fgo_summon_weights.json");
		JsonObject jsonObject = JsonNode.Parse(File.ReadAllText(Path.Combine(appRoot, "deck.json"))).AsObject();
		string fullPath2 = Path.GetFullPath(Path.Combine(appRoot, jsonObject["CardsPath"].GetValue<string>()));
		string path = Path.Combine(fullPath, "titles", "fgo", "data", "summon_candidates-11.51.json");
		if (!File.Exists(path))
		{
			path = Path.Combine(fullPath, "titles", "fgo", "data", "summon_candidates.json");
		}
		foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path))["cards"].AsArray())
		{
			SummonCardOption summonCardOption = new SummonCardOption
			{
				TcId = item["tc_id"].GetValue<int>(),
				Kind = item["type"].GetValue<int>(),
				EntityId = item["entity_id"].GetValue<int>(),
				Name = item["name"].GetValue<string>(),
				Rarity = item["rarity"].GetValue<int>(),
				Category = (item["category"]?.GetValue<string>() ?? "regular"),
				HoloType = (item["holo_type"]?.GetValue<int>() ?? 0),
				AcquisitionNote = string.Join("\n", (from n in item["acquisition_notes"]?.AsArray()
					select n.GetValue<string>()) ?? Enumerable.Empty<string>()),
				ImagePath = Path.Combine(fullPath2, Path.GetFileName(item["file_name"].GetValue<string>()))
			};
			cards.Add(summonCardOption);
			summonCardOption.PropertyChanged += delegate(object? _, PropertyChangedEventArgs e)
			{
				if (e.PropertyName == "WeightText" && !batching)
				{
					dirty = true;
					Recalculate();
				}
			};
		}
		view = CollectionViewSource.GetDefaultView(cards);
		view.Filter = Filter;
		CardsGrid.ItemsSource = (IEnumerable)view;
		LoadSettings();
		try
		{
			RefreshPresetList();
		}
		catch (Exception ex)
		{
			StatusText.Text = "Could not list the presets: " + ex.Message;
		}
	}

	private bool Filter(object value)
	{
		SummonCardOption summonCardOption = (SummonCardOption)value;
		string text = SearchBox.Text.Trim();
		if (CategoryFilter.SelectedIndex switch
		{
			1 => summonCardOption.Category == "regular", 
			2 => summonCardOption.Category == "supplemental", 
			3 => summonCardOption.IsStory, 
			_ => !summonCardOption.IsStory, 
		} && (KindFilter.SelectedIndex == 0 || summonCardOption.Kind == KindFilter.SelectedIndex))
		{
			if (text.Length != 0 && !summonCardOption.Name.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.EnglishName.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.FileNumber.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.FileName.Contains(text, StringComparison.OrdinalIgnoreCase))
			{
				return summonCardOption.TcId.ToString().Contains(text);
			}
			return true;
		}
		return false;
	}

	private void Search_OnChanged(object sender, TextChangedEventArgs e)
	{
		ICollectionView? obj = view;
		if (obj != null)
		{
			obj.Refresh();
		}
	}

	private void Filter_OnChanged(object sender, SelectionChangedEventArgs e)
	{
		ICollectionView? obj = view;
		if (obj != null)
		{
			obj.Refresh();
		}
		if (EditButtons != null && CategoryFilter != null)
		{
			EditButtons.IsEnabled = CategoryFilter.SelectedIndex != 3;
		}
	}

	private void Grid_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
	{
		((DispatcherObject)this).Dispatcher.BeginInvoke((DispatcherPriority)4, (Delegate)new Action(Recalculate));
	}

	private void Grid_OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
	{
		if (e.Row.Item is SummonCardOption { IsStory: not false })
		{
			e.Cancel = true;
			StatusText.Text = "Story-fixed cards are read-only and cannot be added to the random pool.";
		}
	}

	private void Grid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (StatusText != null && CardsGrid.SelectedItem is SummonCardOption { IsStory: not false } summonCardOption)
		{
			StatusText.Text = summonCardOption.AcquisitionNote.Split('\n')[0];
		}
	}

	/// <summary>
	/// Fills the table from the text of a weights file and returns how many cards were left out.
	/// The live file is read strictly, because the server rejects a card id it cannot draw; a
	/// preset made on another install is read tolerantly, dropping the ids this game does not have.
	/// </summary>
	private int ApplyWeightsText(string? text, bool tolerant)
	{
		JsonObject jsonObject = ((text == null) ? null : JsonNode.Parse(text).AsObject());
		JsonObject jsonObject2 = null;
		int skipped = 0;
		if (jsonObject != null)
		{
			JsonNode? jsonNode = jsonObject["version"];
			if (jsonNode == null || jsonNode.GetValue<int>() != 1 || !(jsonObject["weights"] is JsonObject jsonObject3))
			{
				throw new InvalidDataException("the config version or format is wrong");
			}
			jsonObject2 = jsonObject3;
			HashSet<string> hashSet = (from c in cards
				where !c.IsStory
				select c.TcId.ToString(CultureInfo.InvariantCulture)).ToHashSet();
			foreach (KeyValuePair<string, JsonNode> item in jsonObject2.ToList())
			{
				if (!hashSet.Contains(item.Key) && tolerant)
				{
					jsonObject2.Remove(item.Key);
					skipped++;
					continue;
				}
				if (!hashSet.Contains(item.Key) || !(item.Value is JsonValue jsonValue) || !jsonValue.TryGetValue<int>(out var value) || value < 0 || value > 1000000)
				{
					throw new InvalidDataException("Card " + item.Key + " cannot be drawn, or its weight is invalid");
				}
			}
			if (!jsonObject2.Any<KeyValuePair<string, JsonNode>>((KeyValuePair<string, JsonNode> pair) => pair.Value.GetValue<int>() > 0))
			{
				throw new InvalidDataException("at least one card needs a weight above 0");
			}
		}
		batching = true;
		foreach (SummonCardOption card in cards)
		{
			card.WeightText = ((!card.IsStory) ? ((jsonObject2 == null) ? 1 : (jsonObject2[card.TcId.ToString()]?.GetValue<int>() ?? 0)) : 0).ToString(CultureInfo.InvariantCulture);
		}
		batching = false;
		Recalculate();
		return skipped;
	}

	private void LoadSettings()
	{
		try
		{
			string text = (File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null);
			ApplyWeightsText(text, tolerant: false);
			loadedText = text;
			dirty = false;
			StatusText.Text = ((text == null) ? "No custom rates saved yet - every drawable card has the same chance." : "Saved rates loaded. A draw already under way, and its retries, keep their original result.");
		}
		catch (Exception ex)
		{
			batching = false;
			loadedText = (File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null);
			dirty = true;
			Recalculate();
			StatusText.Text = "Could not read the rates: " + ex.Message + ". The server rejects an invalid file - fix the values here and save.";
		}
	}

	private void Recalculate()
	{
		bool flag = cards.Count > 0 && cards.All((SummonCardOption c) => c.Valid);
		decimal num = ((IEnumerable<SummonCardOption>)cards).Sum((Func<SummonCardOption, decimal>)((SummonCardOption c) => c.Weight));
		foreach (SummonCardOption card in cards)
		{
			card.SetTotal(flag ? num : 0m);
		}
		SaveButton.IsEnabled = flag && num > 0m;
		decimal num2 = cards.Where((SummonCardOption c) => c.Kind == 1).Sum((Func<SummonCardOption, decimal>)((SummonCardOption c) => c.Weight));
		SummaryText.Text = ((!flag) ? "Enter each weight as a whole number from 0 to 1,000,000 - invalid input cannot be saved." : ((num <= 0m) ? "Every card is excluded - enable at least one." : $"{cards.Count((SummonCardOption c) => !c.IsStory)} drawable · {cards.Count((SummonCardOption c) => c.IsStory)} story-fixed · {cards.Count((SummonCardOption c) => c.Weight > 0)} enabled  |  Servant {num2 * 100m / num:0.####}% · Craft Essence {(num - num2) * 100m / num:0.####}%  |  total 100%"));
		if (dirty)
		{
			StatusText.Text = "You have unsaved changes. Once saved, the server uses the new rates on its next draw. This page only sets draw rates. It does not draw or grant any cards.";
		}
	}

	private void SetWeights(Func<SummonCardOption, int> selector)
	{
		CardsGrid.CancelEdit();
		CardsGrid.CancelEdit(DataGridEditingUnit.Row);
		batching = true;
		foreach (SummonCardOption card in cards)
		{
			card.WeightText = ((!card.IsStory) ? selector(card) : 0).ToString(CultureInfo.InvariantCulture);
		}
		batching = false;
		dirty = true;
		Recalculate();
	}

	private void EqualAll_OnClick(object sender, RoutedEventArgs e)
	{
		SetWeights((SummonCardOption _) => 1);
	}

	private void OnlyOne_OnClick(object sender, RoutedEventArgs e)
	{
		if (CardsGrid.SelectedItems.Count != 1)
		{
			StatusText.Text = "Select one card first, then set it to 100%.";
			return;
		}
		SummonCardOption selected = (SummonCardOption)CardsGrid.SelectedItem;
		if (selected.IsStory)
		{
			StatusText.Text = "A story-fixed card cannot be set to draw randomly.";
			return;
		}
		SetWeights((SummonCardOption c) => (c == selected) ? 1 : 0);
	}

	private void EqualSelected_OnClick(object sender, RoutedEventArgs e)
	{
		HashSet<SummonCardOption> selected = (from SummonCardOption c in CardsGrid.SelectedItems
			where !c.IsStory
			select c).ToHashSet();
		if (selected.Count == 0)
		{
			StatusText.Text = "Select the cards that should share the chance first.";
			return;
		}
		SetWeights((SummonCardOption c) => selected.Contains(c) ? 1 : 0);
	}

	private void Exclude_OnClick(object sender, RoutedEventArgs e)
	{
		HashSet<SummonCardOption> selected = CardsGrid.SelectedItems.Cast<SummonCardOption>().ToHashSet();
		if (selected.Count == 0)
		{
			StatusText.Text = "Select the cards to exclude first.";
			return;
		}
		if (cards.Any((SummonCardOption c) => !c.Valid))
		{
			StatusText.Text = "Fix the invalid weights first.";
			return;
		}
		SetWeights((SummonCardOption c) => (!selected.Contains(c)) ? c.Weight : 0);
	}

	/// <summary>The shape the server reads, keyed by card id, so a preset carries to any install.</summary>
	private string BuildWeightsJson()
	{
		JsonObject jsonObject = new JsonObject();
		foreach (SummonCardOption item in cards.Where((SummonCardOption c) => !c.IsStory))
		{
			jsonObject[item.TcId.ToString(CultureInfo.InvariantCulture)] = item.Weight;
		}
		return new JsonObject
		{
			["version"] = 1,
			["weights"] = jsonObject
		}.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		});
	}

	private void Save_OnClick(object sender, RoutedEventArgs e)
	{
		if (!CardsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true) || !CardsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true))
		{
			return;
		}
		Recalculate();
		if (!SaveButton.IsEnabled)
		{
			return;
		}
		try
		{
			if ((File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null) != loadedText)
			{
				throw new IOException("Another window changed the rates file - click Reload before editing.");
			}
			string contents = BuildWeightsJson();
			AtomicFile.WriteAllText(settingsPath, contents);
			loadedText = contents;
			dirty = false;
			StatusText.Text = "Saved. The local server uses the new rates from its next draw, with no need to restart the game; after a launcher update, stop and start the server once.";
		}
		catch (Exception ex)
		{
			StatusText.Text = "Could not save - the existing file was left unchanged: " + ex.Message;
		}
	}

	public bool DiscardConfirmed()
	{
		if (dirty)
		{
			return ThemedMessageBox.Show("Discard the unsaved draw-rate changes?", "Draw Rates", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
		}
		return true;
	}

	private void Reload_OnClick(object sender, RoutedEventArgs e)
	{
		if (DiscardConfirmed())
		{
			LoadSettings();
		}
	}

	private string PresetsFolder => Path.Combine(Path.GetDirectoryName(settingsPath), "summon-presets");

	/// <summary>This page is a UserControl, so the dialogs it opens belong to the window around it.</summary>
	private Window OwnerWindow => Window.GetWindow(this);

	private void RefreshPresetList(string? select = null)
	{
		PresetFolder.Fill(PresetComboBox, PresetsFolder, select);
	}

	private static bool LooksLikePreset(string text)
	{
		try
		{
			return JsonNode.Parse(text) is JsonObject o && o["version"]?.GetValue<int>() == 1 && o["weights"] is JsonObject;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private void PresetSave_OnClick(object sender, RoutedEventArgs e)
	{
		if (!CardsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true) || !CardsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true))
		{
			return;
		}
		Recalculate();
		if (!SaveButton.IsEnabled)
		{
			return;
		}
		NamePromptDialog prompt = new NamePromptDialog(OwnerWindow, "Save preset", "Name for this rate table", PresetComboBox.SelectedItem as string ?? "");
		if (DialogSink.ShowDialog(prompt) != true || prompt.Result == null)
		{
			return;
		}
		string target = PresetFolder.PathFor(PresetFolder.Ensure(PresetsFolder), prompt.Result);
		if (File.Exists(target) && ThemedMessageBox.Show(OwnerWindow, "Replace the preset \"" + prompt.Result + "\"?", "Save preset", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			AtomicFile.WriteAllText(target, BuildWeightsJson());
			RefreshPresetList(prompt.Result);
			StatusText.Text = "Preset saved: " + prompt.Result + ". The live rates are unchanged until you click Save. Export sends a copy to share.";
		}
		catch (Exception ex)
		{
			StatusText.Text = "The preset could not be saved: " + ex.Message;
		}
	}

	private void ApplyPresetFile(string path, string name)
	{
		int skipped = ApplyWeightsText(File.ReadAllText(path), tolerant: true);
		dirty = true;
		StatusText.Text = "Preset loaded: " + name + ((skipped == 1) ? " (1 card in the file is not in your game and was left out)" : ((skipped > 1) ? $" ({skipped} cards in the file are not in your game and were left out)" : "")) + ". Click Save to make the server use it.";
	}

	private void PresetLoad_OnClick(object sender, RoutedEventArgs e)
	{
		if (!(PresetComboBox.SelectedItem is string name) || (dirty && !DiscardConfirmed()))
		{
			return;
		}
		try
		{
			ApplyPresetFile(PresetFolder.PathFor(PresetsFolder, name), name);
		}
		catch (Exception ex)
		{
			StatusText.Text = "The preset could not be loaded: " + ex.Message;
		}
	}

	private void PresetDelete_OnClick(object sender, RoutedEventArgs e)
	{
		if (!(PresetComboBox.SelectedItem is string name) || ThemedMessageBox.Show(OwnerWindow, "Delete the preset \"" + name + "\"?", "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		File.Delete(PresetFolder.PathFor(PresetsFolder, name));
		RefreshPresetList();
		StatusText.Text = "Preset deleted: " + name;
	}

	private void PresetExport_OnClick(object sender, RoutedEventArgs e)
	{
		if (!(PresetComboBox.SelectedItem is string name))
		{
			StatusText.Text = "Select a preset to export, or Save as first.";
			return;
		}
		try
		{
			if (PresetFolder.Export(OwnerWindow, PresetFolder.PathFor(PresetsFolder, name), name))
			{
				StatusText.Text = "Exported: " + name + ".json - send it to anyone with the launcher; they add it with Import.";
			}
		}
		catch (Exception ex)
		{
			StatusText.Text = "The preset could not be exported: " + ex.Message;
		}
	}

	private void PresetImport_OnClick(object sender, RoutedEventArgs e)
	{
		if (dirty && !DiscardConfirmed())
		{
			return;
		}
		try
		{
			string? name = PresetFolder.Import(OwnerWindow, PresetsFolder, LooksLikePreset, "That file is not a draw-rate preset. Pick a .json file that was exported from the Presets row.");
			if (name == null)
			{
				return;
			}
			RefreshPresetList(name);
			ApplyPresetFile(PresetFolder.PathFor(PresetsFolder, name), name);
		}
		catch (Exception ex)
		{
			StatusText.Text = "The preset could not be imported: " + ex.Message;
		}
	}

	private void PresetFolderLink_OnClick(object sender, RoutedEventArgs e)
	{
		PresetFolder.OpenFolder(PresetsFolder);
	}
}
