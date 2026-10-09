using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FGOLocalPlatform;

/// <summary>
/// Cloud23333's event switches on the Events page, ported from his 1.10 launcher: his catalogue grouped by edition and
/// kind, each group folding open, a tick per event, its place inside its kind, and each change saved at once into the
/// file his server reads. Read afresh every time the page is shown, so a change his launcher made is there.
/// </summary>
public partial class EventSwitchesView : UserControl
{
	private readonly string serverRoot;

	private EventSwitches.Entry[] entries = Array.Empty<EventSwitches.Entry>();

	private readonly Dictionary<int, CheckBox> boxes = new Dictionary<int, CheckBox>();

	private readonly Dictionary<string, bool> expanded = new Dictionary<string, bool>();

	private readonly List<(ListBoxItem Row, string Edition, string? Category)> rows = new List<(ListBoxItem, string, string?)>();

	private bool ready;

	/// <summary>True once an event was moved, until Release order: the order is then saved in ordered_ids.</summary>
	private bool customOrder;

	public EventSwitchesView()
		: this(Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server")))
	{
	}

	public EventSwitchesView(string serverRoot)
	{
		this.serverRoot = serverRoot;
		InitializeComponent();
	}

	private void View_OnLoaded(object sender, RoutedEventArgs e)
	{
		Reload();
	}

	internal void Reload()
	{
		ready = false;
		try
		{
			EventSwitches.Entry[] catalogue = EventSwitches.ReadCatalog(EventSwitches.CatalogPath(serverRoot));
			JsonObject settings = EventSwitches.Read(EventSwitches.SettingsPath(serverRoot));
			HashSet<int> hidden = EventSwitches.Hidden(settings);
			int[] order = EventSwitches.Order(settings);
			customOrder = order.Length > 0;
			entries = EventSwitches.Grouped(EventSwitches.Sort(catalogue, order));
			Build(entries.Where((EventSwitches.Entry entry) => !hidden.Contains(entry.Id)).Select((EventSwitches.Entry entry) => entry.Id).ToHashSet());
			ready = true;
			StatusText.Text = $"{entries.Length - hidden.Count((int id) => entries.Any((EventSwitches.Entry entry) => entry.Id == id))} of {entries.Length} events on.";
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is InvalidOperationException || ex is KeyNotFoundException)
		{
			EventList.Items.Clear();
			StatusText.Text = "The event list could not be read: " + ex.Message;
		}
	}

	private static string Key(string edition, string? category)
	{
		return edition + "/" + category;
	}

	/// <summary>Editions start open, kinds folded, as in his launcher; a group keeps how the player left it.</summary>
	private bool Expanded(string edition, string? category)
	{
		return expanded.TryGetValue(Key(edition, category), out bool open) ? open : (category == null);
	}

	private void Build(HashSet<int> visible)
	{
		int? selected = (EventList.SelectedItem as ListBoxItem)?.Tag as int?;
		EventList.Items.Clear();
		boxes.Clear();
		rows.Clear();
		Dictionary<(string, string?), EventSwitches.Group> groups = EventSwitches.Groups(entries).ToDictionary((EventSwitches.Group group) => (group.Edition, group.Category));
		string? edition = null;
		string? category = null;
		foreach (EventSwitches.Entry entry in entries)
		{
			if (edition != entry.Edition)
			{
				edition = entry.Edition;
				category = null;
				AddHeading(groups[(edition, null)]);
			}
			if (category != entry.Category)
			{
				category = entry.Category;
				AddHeading(groups[(edition, category)]);
			}
			CheckBox box = new CheckBox
			{
				IsChecked = visible.Contains(entry.Id),
				Content = new TextBlock
				{
					Text = EventSwitches.Label(entry, entries),
					TextWrapping = TextWrapping.Wrap
				},
				ToolTip = entry.Name,
				VerticalContentAlignment = VerticalAlignment.Center
			};
			ListBoxItem row = new ListBoxItem
			{
				Tag = entry.Id,
				Padding = new Thickness(36.0, 6.0, 8.0, 6.0),
				Content = box
			};
			box.Click += delegate
			{
				EventList.SelectedItem = row;
				Save();
			};
			EventList.Items.Add(row);
			boxes.Add(entry.Id, box);
			rows.Add((row, entry.Edition, entry.Category));
			if (selected == entry.Id)
			{
				EventList.SelectedItem = row;
			}
		}
		ShowGroups();
	}

	private void AddHeading(EventSwitches.Group group)
	{
		System.Windows.Shapes.Path caret = new System.Windows.Shapes.Path
		{
			Data = Geometry.Parse("M0,0 L4,4 L8,0"),
			Stroke = (Brush)FindResource("TextSoftBrush"),
			StrokeThickness = 1.5,
			Width = 8.0,
			Height = 5.0,
			Margin = new Thickness(0.0, 0.0, 12.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			RenderTransformOrigin = new Point(0.5, 0.5)
		};
		TextBlock title = new TextBlock
		{
			Text = $"{group.Title} ({group.Count})",
			FontWeight = (group.Category == null) ? FontWeights.SemiBold : FontWeights.Normal,
			VerticalAlignment = VerticalAlignment.Center
		};
		StackPanel header = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		header.Children.Add(caret);
		header.Children.Add(title);
		ToggleButton toggle = new ToggleButton
		{
			Style = (Style)FindResource("HeadingToggleStyle"),
			IsChecked = Expanded(group.Edition, group.Category),
			Content = header,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			HorizontalContentAlignment = HorizontalAlignment.Left
		};
		void Show()
		{
			bool open = toggle.IsChecked == true;
			caret.RenderTransform = new RotateTransform(open ? 0.0 : -90.0);
			expanded[Key(group.Edition, group.Category)] = open;
			ShowGroups();
		}
		toggle.Checked += delegate
		{
			Show();
		};
		toggle.Unchecked += delegate
		{
			Show();
		};
		ListBoxItem row = new ListBoxItem
		{
			Focusable = false,
			Padding = new Thickness((group.Category == null) ? 0.0 : 18.0, (group.Category == null) ? 12.0 : 2.0, 4.0, 2.0),
			Content = toggle
		};
		EventList.Items.Add(row);
		if (group.Category != null)
		{
			rows.Add((row, group.Edition, group.Category));
		}
		Show();
	}

	/// <summary>A kind shows while its edition is open; an event while its kind is open too.</summary>
	private void ShowGroups()
	{
		foreach ((ListBoxItem row, string edition, string? category) in rows)
		{
			bool shown = Expanded(edition, null) && (!(row.Tag is int) || Expanded(edition, category));
			row.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
			if (!shown && row.IsSelected)
			{
				row.IsSelected = false;
			}
		}
	}

	private HashSet<int> Visible()
	{
		return boxes.Where((KeyValuePair<int, CheckBox> pair) => pair.Value.IsChecked == true).Select((KeyValuePair<int, CheckBox> pair) => pair.Key).ToHashSet();
	}

	private void Save()
	{
		if (!ready)
		{
			return;
		}
		try
		{
			EventSwitches.Save(EventSwitches.SettingsPath(serverRoot), entries, Visible(), customOrder ? entries.Select((EventSwitches.Entry entry) => entry.Id) : null);
			StatusText.Text = $"Saved: {Visible().Count} of {entries.Length} events on. The game shows the change the next time it starts.";
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is System.Text.Json.JsonException)
		{
			StatusText.Text = "The event switches could not be saved: " + ex.Message;
		}
	}

	private void SetAll(bool on)
	{
		if (!ready)
		{
			return;
		}
		foreach (CheckBox box in boxes.Values)
		{
			box.IsChecked = on;
		}
		Save();
	}

	private void AllOn_OnClick(object sender, RoutedEventArgs e)
	{
		SetAll(on: true);
	}

	private void AllOff_OnClick(object sender, RoutedEventArgs e)
	{
		SetAll(on: false);
	}

	private void Move_OnClick(object sender, RoutedEventArgs e)
	{
		if (!ready || !((EventList.SelectedItem as ListBoxItem)?.Tag is int selected))
		{
			StatusText.Text = "Pick an event in the list first, then move it.";
			return;
		}
		EventSwitches.Entry[] moved = EventSwitches.Move(entries, selected, ((sender as Button)?.Tag as string) ?? "");
		if (moved.Select((EventSwitches.Entry entry) => entry.Id).SequenceEqual(entries.Select((EventSwitches.Entry entry) => entry.Id)))
		{
			return;
		}
		HashSet<int> visible = Visible();
		entries = moved;
		customOrder = true;
		Build(visible);
		EventList.ScrollIntoView(EventList.SelectedItem);
		Save();
	}

	private void ReleaseOrder_OnClick(object sender, RoutedEventArgs e)
	{
		if (!ready)
		{
			return;
		}
		HashSet<int> visible = Visible();
		entries = EventSwitches.Grouped(EventSwitches.Sort(entries, Array.Empty<int>()));
		customOrder = false;
		Build(visible);
		Save();
	}
}
