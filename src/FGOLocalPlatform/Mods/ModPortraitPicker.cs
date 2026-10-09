using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FGOLocalPlatform;

public partial class ModPortraitPicker : Window, IComponentConnector
{
	public sealed class CharacterGroup : INotifyPropertyChanged
	{
		private bool expanded;

		public string Key { get; }

		public bool Expanded
		{
			get
			{
				return expanded;
			}
			set
			{
				expanded = value;
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Expanded"));
			}
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		public CharacterGroup(string key)
		{
			Key = key;
		}
	}

	public sealed class Choice
	{
		private readonly string root;

		private readonly string library;

		private bool read;

		private ImageSource? thumbnail;

		public ModPortraitReference Portrait { get; }

		public string Name => Portrait.Name;

		public string Reference => Portrait.Reference;

		public string KindLabel => ModPortraits.IsCustom(Reference) ? "Your portrait" : "Game portrait";

		public CharacterGroup Group { get; }

		public string GroupKey => Group.Key;

		public string CharacterName => CharacterLabel(Portrait, GroupKey);

		public ImageSource? Thumbnail
		{
			get
			{
				if (read)
				{
					return thumbnail;
				}
				read = true;
				string text = ModPortraits.PreviewPath(root, Reference, library);
				if (!File.Exists(text))
				{
					return null;
				}
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.UriSource = new Uri(text);
				bitmapImage.DecodePixelWidth = 96;
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.EndInit();
				bitmapImage.Freeze();
				return thumbnail = bitmapImage;
			}
		}

		public Choice(ModPortraitReference portrait, string root, string library, CharacterGroup group)
		{
			Portrait = portrait;
			this.root = root;
			this.library = library;
			Group = group;
		}
	}

	private Choice[] all = Array.Empty<Choice>();

	private readonly Dictionary<string, CharacterGroup> groups = new Dictionary<string, CharacterGroup>(StringComparer.Ordinal);

	/// <summary>English names of the native portraits that are not Servants (his ModCharacterNames.json).</summary>
	private static readonly Dictionary<string, string> CastNames = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["CAST_0001"] = "Refugees",
		["CAST_0002"] = "Pilgrim Expedition Knights",
		["CAST_0003"] = "The Lion King",
		["CAST_0004"] = "Nebuchadnezzar II",
		["CAST_0005"] = "Beast VI/S",
		["CAST_0006"] = "Pirates"
	};

	private readonly Action<ModPortraitReference> selected;

	private bool synchronising;

	private static string CharacterKey(ModPortraitReference portrait)
	{
		if (ModPortraits.IsCustom(portrait.Reference))
		{
			return "custom:" + portrait.Name;
		}
		Match match = Regex.Match(portrait.Reference, "^EXP_(.+?)_talk_");
		if (!match.Success)
		{
			return portrait.Reference;
		}
		return match.Groups[1].Value;
	}

	private static string CharacterLabel(ModPortraitReference portrait, string key)
	{
		if (ModPortraits.IsCustom(portrait.Reference))
		{
			return portrait.Name;
		}
		if (key.StartsWith("SVT_") && int.TryParse(key.Substring(4), out var result))
		{
			string name = CardNames.English(1, result);
			return (name.Length == 0) ? key : name;
		}
		if (CastNames.TryGetValue(key, out string? value))
		{
			return value;
		}
		return key;
	}

	public ModPortraitPicker(Action<ModPortraitReference> selected)
	{
		this.selected = selected;
		InitializeComponent();
		WindowTheme.Apply(this);
		Kind.SelectedIndex = 0;
	}

	public void Refresh(string root, string library, IEnumerable<ModPortraitReference> references, string current)
	{
		all = references.Where((ModPortraitReference p) => p.Reference.Length > 0).Select(delegate(ModPortraitReference p)
		{
			string key = CharacterKey(p);
			if (!groups.TryGetValue(key, out CharacterGroup value))
			{
				value = (groups[key] = new CharacterGroup(key));
			}
			return new Choice(p, root, library, value);
		}).ToArray();
		Update(current);
	}

	public void Sync(string current, bool enabled)
	{
		ListBox choices = Choices;
		bool isEnabled = (None.IsEnabled = enabled);
		choices.IsEnabled = isEnabled;
		synchronising = true;
		Choices.SelectedItem = Choices.Items.Cast<Choice>().FirstOrDefault((Choice p) => p.Reference == current);
		synchronising = false;
	}

	private void Update(string? current = null)
	{
		if (Choices != null && Search != null)
		{
			if (current == null)
			{
				current = (Choices.SelectedItem as Choice)?.Reference;
			}
			string term = Search.Text.Trim();
			int kind = Kind.SelectedIndex;
			synchronising = true;
			ICollectionView defaultView = CollectionViewSource.GetDefaultView(all.Where((Choice p) => (kind <= 0 || (ModPortraits.IsCustom(p.Reference) ? (kind == 2) : (kind == 1))) && (p.CharacterName.Contains(term, StringComparison.OrdinalIgnoreCase) || p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || p.Reference.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray());
			defaultView.GroupDescriptions.Add(new PropertyGroupDescription("GroupKey"));
			Choices.ItemsSource = defaultView;
			Choices.SelectedItem = Choices.Items.Cast<Choice>().FirstOrDefault((Choice p) => p.Reference == current);
			synchronising = false;
		}
	}

	private void Filter(object sender, SelectionChangedEventArgs e)
	{
		Update();
	}

	private void SearchChanged(object sender, TextChangedEventArgs e)
	{
		Update();
	}

	private void Choose(object sender, SelectionChangedEventArgs e)
	{
		if (!synchronising && Choices.SelectedItem is Choice choice)
		{
			selected(choice.Portrait);
		}
	}

	private void WithoutPortrait(object sender, RoutedEventArgs e)
	{
		selected(new ModPortraitReference("", "No portrait"));
	}
}
