using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hanhua;
using Microsoft.Win32;

namespace FGOLocalPlatform;

public partial class ModSettingsView : UserControl, IComponentConnector
{
	private ModLoopEditor? loopEditor;

	private ModSettings settings = new ModSettings();

	private ModLoadingSettings loadingImages = new ModLoadingSettings();

	private readonly string gameRoot;

	private readonly string root;

	private bool loaded;

	private bool filteringTargets;

	private List<ModQuest> quests = new List<ModQuest>();

	private List<ModQuest> banners = new List<ModQuest>();

	private ModPortraitReference[] portraits = Array.Empty<ModPortraitReference>();

	private Point dragStart;

	private ModLine? dragLine;

	private ModPortraitPicker? picker;

	private DispatcherTimer? dragScroll;

	private ScrollViewer? dragViewport;

	private int dragDirection;

	private bool updatingLoadingSelection;

	private int SelectedTrigger
	{
		get
		{
			if (TargetKind.SelectedIndex != 1)
			{
				object selectedValue = Trigger.SelectedValue;
				if (selectedValue is int)
				{
					return (int)selectedValue;
				}
				return -1;
			}
			return 0;
		}
	}

	private void OpenLoopEditor(object sender, RoutedEventArgs e)
	{
		if (e is MouseButtonEventArgs e2 && Ancestor<DataGridRow>(e2.OriginalSource as DependencyObject) == null)
		{
			return;
		}
		object selectedItem = Tracks.SelectedItem;
		ModTrack track = selectedItem as ModTrack;
		if (track == null)
		{
			return;
		}
		if (loopEditor != null)
		{
			loopEditor.Activate();
			return;
		}
		loopEditor = new ModLoopEditor(root, track, async delegate(double begin, double end)
		{
			ModTrack replacement = await ModAudioEditing.UpdateLoop(root, track, begin, end);
			int num = settings.Tracks.IndexOf(track);
			if (num < 0)
			{
				throw new InvalidOperationException("The track was removed.");
			}
			settings.Tracks[num] = replacement;
			settings.BgmEnabled = Bgm.IsChecked == true;
			await ModStorage.SaveBgmAndClean(root, settings);
			RefreshTracks();
			Tracks.SelectedItem = replacement;
			Message("Music saved; a running game switches to it now.");
		})
		{
			Owner = Window.GetWindow(this)
		};
		try
		{
			DialogSink.ShowDialog(loopEditor);
		}
		finally
		{
			loopEditor = null;
		}
	}

	public ModSettingsView()
		: this(GamePaths.GameRoot, ModStorage.Root)
	{
	}

	public ModSettingsView(string gameRoot, string root)
	{
		this.gameRoot = gameRoot;
		this.root = root;
		InitializeComponent();
		MasterEditor.PublicationMetadata = () => (Title: PackageTitle.Text.Trim(), Author: PackageAuthor.Text.Trim(), Version: PackageVersion.Text.Trim(), Description: PackageDescription.Text);
		MasterEditor.Configure(gameRoot, root);
		Installer.Configure(root);
		TargetKind.SelectedIndex = 0;
		Trigger.SelectedIndex = 0;
		Repeat.SelectedIndex = 0;
		base.Loaded += delegate
		{
			if (!loaded)
			{
				Load();
			}
			UpdatePickerVisibility();
		};
		base.Unloaded += delegate
		{
			picker?.Close();
			picker = null;
			StopDragScroll();
			loopEditor?.Close();
		};
		Tabs.SelectionChanged += delegate(object _, SelectionChangedEventArgs e)
		{
			if (e.OriginalSource == Tabs)
			{
				UpdatePickerVisibility();
			}
		};
		base.IsVisibleChanged += delegate
		{
			UpdatePickerVisibility();
		};
		base.IsEnabledChanged += delegate
		{
			SyncPicker();
		};
		base.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.None)
			{
				Save(this, new RoutedEventArgs());
				e.Handled = true;
			}
		};
	}

	private void Load()
	{
		loaded = true;
		try
		{
			loadingImages = ModLoading.Load(root);
			settings = ModStorage.Load(root);
			Bgm.IsChecked = settings.BgmEnabled;
			Dialogue.IsChecked = settings.DialogueEnabled;
			PackageTitle.Text = settings.Title;
			PackageAuthor.Text = settings.Author;
			PackageVersion.Text = settings.Version;
			PackageDescription.Text = settings.Description;
			LibraryPath.Text = settings.PortraitLibraryPath;
			quests = ModStorage.Quests(gameRoot, GameLanguage.Folder(GamePaths.GameRoot));
			banners = ModStorage.Banners(gameRoot);
			Pool.ItemsSource = ModStorage.Pools(gameRoot);
			Pool.SelectedIndex = 0;
			ModPortraits.MigrateLegacy(gameRoot, root, settings);
			RefreshEvents();
			portraits = ModPortraits.References(gameRoot, root, settings.PortraitLibraryPath);
			RefreshLibrary();
			UpdateEventNames();
			FilterTargets(this, new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None));
			if (settings.Dialogues.Count > 0)
			{
				Events.SelectedIndex = 0;
			}
			UpdateConverterNote();
		}
		catch (Exception ex)
		{
			base.IsEnabled = false;
			Message("The mod files could not be read, so nothing was changed: " + ex.Message);
		}
	}

	private void Message(string source)
	{
		Status.Text = source;
	}

	/// <summary>Adding music converts through Cloud23333's App\1_MOD\Tools\ffmpeg.exe; without it the page says so.</summary>
	private void UpdateConverterNote()
	{
		bool present = File.Exists(Path.Combine(root, "Tools", "ffmpeg.exe"));
		ImportMusicButton.IsEnabled = present;
		ConverterNote.Visibility = present ? Visibility.Collapsed : Visibility.Visible;
		ConverterNote.Text = "Adding music needs App\\1_MOD\\Tools\\ffmpeg.exe, which Cloud23333's latest update installs.";
	}

	private void UpdateEventNames()
	{
		foreach (ModDialogue d in settings.Dialogues)
		{
			d.QuestName = ((d.TargetKind == 0) ? quests : banners).FirstOrDefault((ModQuest q) => q.Id == d.Quest)?.Name ?? "Not in this game's files";
		}
	}

	private void TargetKindChanged(object sender, SelectionChangedEventArgs e)
	{
		if (Trigger != null)
		{
			Trigger.IsEnabled = TargetKind.SelectedIndex == 0;
			if (!Trigger.IsEnabled)
			{
				Trigger.SelectedIndex = 0;
			}
			FilterTargets(sender, new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None));
		}
	}

	private void LibraryPathChanged(object sender, TextChangedEventArgs e)
	{
		if (LibraryPathHint != null)
		{
			LibraryPathHint.Visibility = (LibraryPath.Text.Length == 0) ? Visibility.Visible : Visibility.Collapsed;
		}
	}

	private void FilterTargets(object sender, TextChangedEventArgs e)
	{
		if (QuestSearchHint != null)
		{
			QuestSearchHint.Visibility = (QuestSearch.Text.Length == 0) ? Visibility.Visible : Visibility.Collapsed;
		}
		if (Quest == null)
		{
			return;
		}
		filteringTargets = true;
		ModQuest selected = Quest.SelectedItem as ModQuest;
		string term = QuestSearch.Text.Trim();
		foreach (ModQuest q in quests)
		{
			q.Modified = settings.Dialogues.Any((ModDialogue d) => d.TargetKind == 0 && d.Quest == q.Id) || loadingImages.Entries.Any((ModLoadingEntry modLoadingEntry) => modLoadingEntry.Quest == q.Id && modLoadingEntry.Enabled);
		}
		foreach (ModQuest q2 in banners)
		{
			q2.Modified = settings.Dialogues.Any((ModDialogue d) => d.TargetKind == 1 && d.Quest == q2.Id);
		}
		Quest.ItemsSource = ((TargetKind.SelectedIndex == 1) ? banners : quests).Where((ModQuest modQuest) => modQuest.Id.ToString().Contains(term) || modQuest.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || modQuest.OriginalName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
		Quest.SelectedItem = Quest.Items.Cast<ModQuest>().FirstOrDefault((ModQuest modQuest) => modQuest.Id == selected?.Id) ?? Quest.Items.Cast<ModQuest>().FirstOrDefault();
		filteringTargets = false;
		RefreshEvents();
	}

	private void QuestChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!filteringTargets && e.OriginalSource == Quest)
		{
			RefreshEvents();
		}
	}

	private void RefreshEvents()
	{
		if (Events != null && Quest != null)
		{
			ModDialogue modDialogue = Events.SelectedItem as ModDialogue;
			int? quest = (Quest.SelectedItem as ModQuest)?.Id;
			ModDialogue[] array = settings.Dialogues.Where((ModDialogue d) => d.TargetKind == TargetKind.SelectedIndex && d.Quest == quest).ToArray();
			Events.ItemsSource = array;
			Events.SelectedItem = (Enumerable.Contains(array, modDialogue) ? modDialogue : array.FirstOrDefault());
			RefreshTriggers((Events.SelectedItem as ModDialogue)?.Trigger);
			UpdateLoadingSelection();
		}
	}

	private void RefreshTriggers(int? saved = null)
	{
		int selected = saved ?? ((Trigger.SelectedValue is int num) ? num : 0);
		Trigger.Items.Clear();
		Add(0, "Quest start");
		if (TargetKind.SelectedIndex == 0)
		{
			if (Quest.SelectedItem is ModQuest modQuest)
			{
				foreach (int item in modQuest.BattleWaves.Where((int w) => w >= 1 && w <= 3))
				{
					Add(item, $"Wave {item}");
				}
			}
			Add(4, "After battle");
		}
		if (!Trigger.Items.Cast<ComboBoxItem>().Any((ComboBoxItem item) => (int)item.Tag == selected))
		{
			if (saved.HasValue)
			{
				Add(selected, $"Wave {selected} (this quest has no such wave)", enabled: false);
			}
			else
			{
				selected = 0;
			}
		}
		Trigger.SelectedValue = selected;
		void Add(int value, string label, bool enabled = true)
		{
			Trigger.Items.Add(new ComboBoxItem
			{
				Tag = value,
				Content = label,
				IsEnabled = enabled
			});
		}
	}

	private bool ValidTrigger(ModQuest quest, int value)
	{
		if (value != 0 && value != 4)
		{
			return quest.BattleWaves.Contains(value);
		}
		return true;
	}

	private void EventChanged(object sender, SelectionChangedEventArgs e)
	{
		if (e.OriginalSource != Events)
		{
			return;
		}
		if (!(Events.SelectedItem is ModDialogue modDialogue))
		{
			Lines.ItemsSource = null;
			ContinueScreen.IsChecked = false;
			ContinueCustomImage.IsChecked = false;
			LineChanged(this, null);
			return;
		}
		RefreshTriggers(modDialogue.Trigger);
		Repeat.SelectedIndex = (modDialogue.Once ? 1 : 0);
		Lines.ItemsSource = modDialogue.Lines;
		ContinueScreen.IsChecked = modDialogue.ContinueScreen;
		ContinueCustomImage.IsChecked = modDialogue.ContinueCustomImage;
		ContinueImageInfo.Text = File.Exists(ModContinueImage.PathFor(root, modDialogue.Id)) ? "Image added. Save and build, then restart the game." : "No image of your own: the screen is black.";
		if (modDialogue.Lines.Count > 0)
		{
			Lines.SelectedIndex = 0;
		}
		else
		{
			LineChanged(this, null);
		}
	}

	private void AddEvent(object sender, RoutedEventArgs e)
	{
		object selectedItem = Quest.SelectedItem;
		ModQuest q = selectedItem as ModQuest;
		if (q == null)
		{
			Message("Pick a quest or banner first.");
			return;
		}
		int kind = TargetKind.SelectedIndex;
		int t = SelectedTrigger;
		if (!ValidTrigger(q, t))
		{
			Message("Pick a wave this quest has.");
			return;
		}
		ModDialogue modDialogue = settings.Dialogues.FirstOrDefault((ModDialogue x) => x.TargetKind == kind && x.Quest == q.Id && x.Trigger == t);
		if (modDialogue == null)
		{
			modDialogue = new ModDialogue
			{
				Quest = q.Id,
				QuestName = q.Name,
				TargetKind = kind,
				Trigger = t,
				Once = (Repeat.SelectedIndex == 1)
			};
			settings.Dialogues.Add(modDialogue);
		}
		FilterTargets(this, new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None));
		Events.SelectedItem = modDialogue;
	}

	private void UpdateEvent(object sender, RoutedEventArgs e)
	{
		object selectedItem = Events.SelectedItem;
		ModDialogue d = selectedItem as ModDialogue;
		if (d == null)
		{
			return;
		}
		selectedItem = Quest.SelectedItem;
		ModQuest q = selectedItem as ModQuest;
		if (q != null)
		{
			int kind = TargetKind.SelectedIndex;
			int t = SelectedTrigger;
			if (!ValidTrigger(q, t))
			{
				Message("Pick a wave this quest has.");
				return;
			}
			if (settings.Dialogues.Any((ModDialogue x) => x != d && x.TargetKind == kind && x.Quest == q.Id && x.Trigger == t))
			{
				Message("That quest or banner already has this trigger.");
				return;
			}
			d.TargetKind = kind;
			d.Quest = q.Id;
			d.QuestName = q.Name;
			d.Trigger = t;
			d.Once = Repeat.SelectedIndex == 1;
			FilterTargets(this, new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None));
			Events.SelectedItem = d;
			Message("Trigger updated. Save to keep it.");
		}
	}

	private void RemoveEvent(object sender, RoutedEventArgs e)
	{
		if (Events.SelectedItem is ModDialogue item)
		{
			settings.Dialogues.Remove(item);
			FilterTargets(this, new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None));
		}
	}

	private void ContinueOptionsChanged(object sender, RoutedEventArgs e)
	{
		if (!(Events.SelectedItem is ModDialogue modDialogue))
		{
			Message("Pick a trigger first.");
			ContinueScreen.IsChecked = false;
			ContinueCustomImage.IsChecked = false;
		}
		else
		{
			modDialogue.ContinueScreen = ContinueScreen.IsChecked == true;
			modDialogue.ContinueCustomImage = ContinueCustomImage.IsChecked == true;
		}
	}

	private void ImportContinueImage(object sender, RoutedEventArgs e)
	{
		if (!(Events.SelectedItem is ModDialogue modDialogue))
		{
			Message("Pick a trigger first.");
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp"
		};
		if (DialogSink.ShowDialog(openFileDialog, Window.GetWindow(this)) != true)
		{
			return;
		}
		try
		{
			ModContinueImage.Import(root, modDialogue.Id, openFileDialog.FileName);
			ContinueImageInfo.Text = "Image added. Save and build, then restart the game.";
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
	}

	private void PreviewContinueImage(object sender, RoutedEventArgs e)
	{
		if (!(Events.SelectedItem is ModDialogue modDialogue))
		{
			return;
		}
		try
		{
			Grid grid = new Grid
			{
				Background = Brushes.Black
			};
			if (modDialogue.ContinueCustomImage)
			{
				string text = ModContinueImage.PathFor(root, modDialogue.Id);
				if (!File.Exists(text))
				{
					Message("Choose an image first.");
					return;
				}
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.UriSource = new Uri(text);
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.EndInit();
				grid.Children.Add(new Image
				{
					Source = bitmapImage,
					Stretch = Stretch.Uniform
				});
			}
			grid.Children.Add(new TextBlock
			{
				Text = "Tap to continue",
				Foreground = Brushes.White,
				Background = Brushes.Black,
				FontSize = 24.0,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Bottom,
				Margin = new Thickness(20.0)
			});
			Window preview = new Window
			{
				Title = "After the dialogue",
				Owner = Window.GetWindow(this),
				Content = grid,
				Width = 960.0,
				Height = 580.0,
				WindowStartupLocation = WindowStartupLocation.CenterOwner
			};
			grid.MouseLeftButtonUp += delegate
			{
				preview.Close();
			};
			DialogSink.ShowDialog(preview);
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
	}

	private void AddLine(object sender, RoutedEventArgs e)
	{
		if (Events.SelectedItem is ModDialogue modDialogue)
		{
			ModLine modLine = new ModLine
			{
				Portrait = ((Lines.SelectedItem as ModLine)?.Portrait ?? "")
			};
			modDialogue.Lines.Add(modLine);
			Lines.SelectedItem = modLine;
			Lines.ScrollIntoView(modLine);
			Lines.CurrentCell = new DataGridCellInfo(modLine, Lines.Columns[0]);
			Lines.Focus();
			Lines.BeginEdit();
		}
	}

	private void DuplicateLine(object sender, RoutedEventArgs e)
	{
		if (Events.SelectedItem is ModDialogue modDialogue && Lines.SelectedItem is ModLine modLine)
		{
			ModLine modLine2 = new ModLine
			{
				Name = modLine.Name,
				Text = modLine.Text,
				Portrait = modLine.Portrait
			};
			modDialogue.Lines.Insert(modDialogue.Lines.IndexOf(modLine) + 1, modLine2);
			Lines.SelectedItem = modLine2;
		}
	}

	private void RemoveLine(object sender, RoutedEventArgs e)
	{
		if (Events.SelectedItem is ModDialogue modDialogue && Lines.SelectedItem is ModLine item)
		{
			modDialogue.Lines.Remove(item);
		}
	}

	private void MoveUp(object sender, RoutedEventArgs e)
	{
		Move(-1);
	}

	private void MoveDown(object sender, RoutedEventArgs e)
	{
		Move(1);
	}

	private void Move(int delta)
	{
		if (Events.SelectedItem is ModDialogue modDialogue && Lines.SelectedItem is ModLine modLine)
		{
			int num = modDialogue.Lines.IndexOf(modLine);
			int num2 = num + delta;
			if (num2 >= 0 && num2 < modDialogue.Lines.Count)
			{
				modDialogue.Lines.Move(num, num2);
				Lines.Items.Refresh();
				Lines.SelectedItem = modLine;
				Lines.ScrollIntoView(modLine);
			}
		}
	}

	private void LineChanged(object sender, SelectionChangedEventArgs? e)
	{
		if (e == null || e.OriginalSource == Lines)
		{
			UpdatePortraitPreview();
			SyncPicker();
		}
	}

	private void StyleDialogueCorner(object sender, RoutedEventArgs e)
	{
		Visit(Lines);
		void Visit(DependencyObject node)
		{
			if (node is Button button && button.Command == DataGrid.SelectAllCommand)
			{
				FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border));
				frameworkElementFactory.SetValue(Border.BackgroundProperty, FindResource("PlateBrush"));
				frameworkElementFactory.SetValue(Border.BorderBrushProperty, FindResource("LineBrush"));
				frameworkElementFactory.SetValue(Border.BorderThicknessProperty, new Thickness(0.0, 0.0, 1.0, 1.0));
				button.Template = new ControlTemplate(typeof(Button))
				{
					VisualTree = frameworkElementFactory
				};
				button.Width = Lines.RowHeaderWidth;
				button.IsHitTestVisible = false;
				button.Focusable = false;
			}
			else
			{
				for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
				{
					Visit(VisualTreeHelper.GetChild(node, i));
				}
			}
		}
	}

	private void UpdatePortraitPreview()
	{
		if (PortraitPreview != null)
		{
			string text = (Lines.SelectedItem as ModLine)?.Portrait;
			string text2 = ((text == null) ? "" : ModPortraits.PreviewPath(root, text, settings.PortraitLibraryPath));
			PortraitPreview.Source = null;
			if (File.Exists(text2))
			{
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.UriSource = new Uri(text2);
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.EndInit();
				bitmapImage.Freeze();
				PortraitPreview.Source = bitmapImage;
			}
			PortraitInfo.Text = string.IsNullOrEmpty(text) ? "No portrait" : (ModPortraits.IsCustom(text) ? "Your portrait" : "Game portrait");
		}
	}

	private void UpdatePickerVisibility()
	{
		if (!loaded || !base.IsVisible || !DialogueTab.IsSelected)
		{
			picker?.Hide();
		}
		else if (base.IsEnabled)
		{
			ShowPicker();
		}
	}

	private void SyncPicker()
	{
		picker?.Sync((Lines.SelectedItem as ModLine)?.Portrait ?? "", base.IsEnabled && base.IsVisible && DialogueTab.IsSelected && Lines.SelectedItem is ModLine);
	}

	private void ShowPicker()
	{
		if (!loaded || !base.IsVisible || !base.IsEnabled || !DialogueTab.IsSelected)
		{
			return;
		}
		Window window = Window.GetWindow(this);
		if (window == null)
		{
			return;
		}
		if (picker == null)
		{
			picker = new ModPortraitPicker(delegate(ModPortraitReference p)
			{
				if (base.IsEnabled && base.IsVisible && DialogueTab.IsSelected && Lines.SelectedItem is ModLine modLine)
				{
					modLine.Portrait = p.Reference;
					UpdatePortraitPreview();
					SyncPicker();
				}
			})
			{
				Owner = window
			};
			picker.Closed += delegate
			{
				picker = null;
			};
			Rect workArea = SystemParameters.WorkArea;
			picker.Height = Math.Min(700.0, workArea.Height);
			picker.Left = Math.Max(workArea.Left, Math.Min(workArea.Right - picker.Width, window.Left + window.ActualWidth + 8.0));
			picker.Top = Math.Max(workArea.Top, Math.Min(workArea.Bottom - picker.Height, window.Top));
		}
		picker.Refresh(root, settings.PortraitLibraryPath, portraits, (Lines.SelectedItem as ModLine)?.Portrait ?? "");
		SyncPicker();
		if (!picker.IsVisible)
		{
			DialogSink.Show(picker);
		}
	}

	private async void ImportPortrait(object sender, RoutedEventArgs e)
	{
		OpenFileDialog pick = new OpenFileDialog
		{
			Filter = "PNG images|*.png",
			Multiselect = true
		};
		if (DialogSink.ShowDialog(pick, Window.GetWindow(this)) != true)
		{
			return;
		}
		base.IsEnabled = false;
		try
		{
			(List<ModPortraitReference> Added, List<string> Errors) result = await Task.Run(() => ModPortraits.ImportBatch(root, pick.FileNames, settings.PortraitLibraryPath));
			RefreshPortraitReferences();
			LibraryItems.SelectedItem = LibraryItems.Items.Cast<ModPortraitReference>().FirstOrDefault((ModPortraitReference p) => p.Reference == result.Added.LastOrDefault()?.Reference);
			Message($"Imported {result.Added.Count} portraits; {result.Errors.Count} failed." + ((result.Errors.Count == 0) ? "" : ("\n" + string.Join("\n", result.Errors))));
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
		finally
		{
			base.IsEnabled = true;
		}
	}

	private void RefreshPortraitReferences()
	{
		portraits = ModPortraits.References(gameRoot, root, settings.PortraitLibraryPath);
		RefreshLibrary();
		UpdatePortraitPreview();
	}

	private void RefreshLibrary()
	{
		string selected = (LibraryItems.SelectedItem as ModPortraitReference)?.Reference;
		LibraryItems.ItemsSource = portraits.Where((ModPortraitReference p) => ModPortraits.IsCustom(p.Reference)).ToArray();
		LibraryItems.SelectedItem = LibraryItems.Items.Cast<ModPortraitReference>().FirstOrDefault((ModPortraitReference p) => p.Reference == selected);
		LibraryChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
		picker?.Refresh(root, settings.PortraitLibraryPath, portraits, (Lines.SelectedItem as ModLine)?.Portrait ?? "");
		SyncPicker();
	}

	private void LibraryChanged(object sender, SelectionChangedEventArgs e)
	{
		ModPortraitReference modPortraitReference = LibraryItems.SelectedItem as ModPortraitReference;
		PortraitName.Text = modPortraitReference?.Name ?? "";
		PortraitName.IsEnabled = modPortraitReference != null;
		LibraryReference.Text = modPortraitReference?.Reference ?? "";
		LibraryPreview.Source = null;
		string text = ((modPortraitReference == null) ? "" : ModPortraits.PreviewPath(root, modPortraitReference.Reference, settings.PortraitLibraryPath));
		if (File.Exists(text))
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.UriSource = new Uri(text);
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			LibraryPreview.Source = bitmapImage;
		}
	}

	private void UseLibrary(object sender, RoutedEventArgs e)
	{
		try
		{
			string text = LibraryPath.Text.Trim();
			if (text.Length > 0)
			{
				Directory.CreateDirectory(ModPortraits.CustomDirectory(root, text));
			}
			ModPortraits.References(gameRoot, root, text);
			settings.PortraitLibraryPath = text;
			RefreshPortraitReferences();
			Message("Portrait folder in use. Save to keep it.");
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
	}

	private void OpenLibrary(object sender, RoutedEventArgs e)
	{
		string text = ModPortraits.CustomDirectory(root, settings.PortraitLibraryPath);
		Directory.CreateDirectory(text);
		DialogSink.Open(text);
	}

	private void RenamePortrait(object sender, RoutedEventArgs e)
	{
		if (!(LibraryItems.SelectedItem is ModPortraitReference modPortraitReference))
		{
			return;
		}
		try
		{
			ModPortraits.Rename(root, modPortraitReference.Reference, PortraitName.Text, settings.PortraitLibraryPath);
			RefreshPortraitReferences();
			Message("Portrait renamed; the lines that use it keep it.");
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
	}

	private void RemovePortrait(object sender, RoutedEventArgs e)
	{
		if (!(LibraryItems.SelectedItem is ModPortraitReference modPortraitReference))
		{
			return;
		}
		try
		{
			ModPortraits.Remove(root, modPortraitReference.Reference, settings.Dialogues, settings.PortraitLibraryPath);
			RefreshPortraitReferences();
			Message("Portrait deleted. Save and build to apply.");
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
	}

	private void PoolChanged(object sender, SelectionChangedEventArgs e)
	{
		if (Tracks != null)
		{
			RefreshTracks();
		}
	}

	private void RefreshTracks()
	{
		object selectedItem = Pool.SelectedItem;
		ModPool p = selectedItem as ModPool;
		if ((object)p != null)
		{
			Tracks.ItemsSource = settings.Tracks.Where((ModTrack t) => t.Pool == p.Key).ToArray();
		}
	}

	private async void ImportMusic(object sender, RoutedEventArgs e)
	{
		object selectedItem = Pool.SelectedItem;
		if (!(selectedItem is ModPool p))
		{
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Audio" + "|*.ogg;*.mp3;*.wav;*.flac;*.m4a;*.aac|" + "All files" + "|*.*",
			Multiselect = true
		};
		if (DialogSink.ShowDialog(openFileDialog, Window.GetWindow(this)) != true)
		{
			return;
		}
		base.IsEnabled = false;
		try
		{
			ModTrack last = null;
			string[] fileNames = openFileDialog.FileNames;
			foreach (string source in fileNames)
			{
				last = await ModStorage.Import(root, source, p.Key, null, null, autoLoop: true, AutoLoudness.IsChecked == true);
				settings.Tracks.Add(last);
			}
			settings.BgmEnabled = Bgm.IsChecked == true;
			await ModStorage.SaveBgmAndClean(root, settings);
			RefreshTracks();
			Tracks.SelectedItem = last;
			Message("Music saved; a running game switches to it now.");
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
		finally
		{
			base.IsEnabled = true;
		}
	}

	private async void UpdateLoudness(object sender, RoutedEventArgs e)
	{
		object selectedItem = Tracks.SelectedItem;
		if (!(selectedItem is ModTrack t))
		{
			return;
		}
		base.IsEnabled = false;
		try
		{
			ModTrack replacement = await ModStorage.Import(root, ModStorage.TrackPath(root, t.File), t.Pool, t.LoopBegin, t.LoopEnd, autoLoop: false, AutoLoudness.IsChecked == true);
			replacement.Title = t.Title;
			settings.Tracks[settings.Tracks.IndexOf(t)] = replacement;
			settings.BgmEnabled = Bgm.IsChecked == true;
			await ModStorage.SaveBgmAndClean(root, settings);
			RefreshTracks();
			Tracks.SelectedItem = replacement;
			Message("Music saved; a running game switches to it now.");
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
		finally
		{
			base.IsEnabled = true;
		}
	}

	private void ShowBgmUsage(object sender, RoutedEventArgs e)
	{
		if (Pool.SelectedItem is ModPool modPool)
		{
			TextBox textBox = new TextBox
			{
				Text = ((modPool.Usage.Length > 0) ? modPool.Usage : modPool.Label),
				IsReadOnly = true,
				TextWrapping = TextWrapping.Wrap,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				Margin = new Thickness(12.0)
			};
			Window obj = new Window
			{
				Title = "Where the game plays " + modPool.Key,
				Owner = Window.GetWindow(this),
				Width = 850.0,
				Height = 550.0,
				Content = textBox,
				Background = base.Background,
				Foreground = base.Foreground,
				WindowStartupLocation = WindowStartupLocation.CenterOwner
			};
			WindowTheme.Apply(obj);
			textBox.Background = (Brush)FindResource("PlateLowBrush");
			DialogSink.Show(obj);
		}
	}

	private void ExportBgmUsage(object sender, RoutedEventArgs e)
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			FileName = "bgm-usage.csv",
			Filter = "CSV|*.csv"
		};
		if (DialogSink.ShowDialog(saveFileDialog, Window.GetWindow(this)) != true)
		{
			return;
		}
		try
		{
			File.WriteAllText(saveFileDialog.FileName, "Track,Usage,References\r\n" + string.Join("\r\n", from p in ModStorage.Pools(gameRoot)
				select string.Join(",", Cell(p.Key), Cell(p.Label), Cell(p.Usage))), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			Message("Usage list exported.");
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
		static string Cell(string value)
		{
			return "\"" + value.Replace("\"", "\"\"") + "\"";
		}
	}

	private async void RemoveTrack(object sender, RoutedEventArgs e)
	{
		if (!(Tracks.SelectedItem is ModTrack item))
		{
			return;
		}
		settings.Tracks.Remove(item);
		RefreshTracks();
		base.IsEnabled = false;
		try
		{
			settings.BgmEnabled = Bgm.IsChecked == true;
			await ModStorage.SaveBgmAndClean(root, settings);
			Message("Track removed and saved; a running game switches now.");
		}
		catch (Exception ex)
		{
			Message("The removal could not be saved: " + ex.Message);
		}
		finally
		{
			base.IsEnabled = true;
		}
	}

	private void ReadOptions()
	{
		settings.BgmEnabled = Bgm.IsChecked == true;
		settings.DialogueEnabled = Dialogue.IsChecked == true;
		settings.Title = PackageTitle.Text.Trim();
		settings.Author = PackageAuthor.Text.Trim();
		settings.Version = PackageVersion.Text.Trim();
		settings.Description = PackageDescription.Text;
		settings.PortraitLibraryPath = LibraryPath.Text.Trim();
	}

	private async void SaveAudioLive(object sender, RoutedEventArgs e)
	{
		base.IsEnabled = false;
		try
		{
			settings.BgmEnabled = Bgm.IsChecked == true;
			await ModStorage.SaveBgmAndClean(root, settings);
			Message("Music saved; a running game switches to it now.");
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
		finally
		{
			base.IsEnabled = true;
		}
	}

	private async void Save(object sender, RoutedEventArgs e)
	{
		if (ImportedTab.IsSelected)
		{
			Installer.Refresh();
		}
		else if (MasterTab.IsSelected)
		{
			await MasterEditor.SaveCurrent();
			Installer.Refresh();
		}
		else
		{
			if (!base.IsEnabled)
			{
				return;
			}
			ReadOptions();
			base.IsEnabled = false;
			Message("Building...");
			try
			{
				await Task.Run(delegate
				{
					ModDialogueCompiler.Build(gameRoot, root, settings);
					ModStorage.Save(root, settings);
					if (loadingImages.Entries.Count > 0)
					{
						ModLoading.Build(gameRoot, root, loadingImages);
					}
					ModStorage.Organize(root);
				});
				Process[] processesByName = Process.GetProcessesByName("ago");
				bool flag = processesByName.Length != 0;
				Process[] array = processesByName;
				for (int num = 0; num < array.Length; num++)
				{
					array[num].Dispose();
				}
				Message(flag ? "Saved. The running game keeps the old dialogue until it restarts." : "Saved. It applies the next time the game starts.");
			}
			catch (Exception ex)
			{
				Message("Saving failed: " + ex.Message);
			}
			finally
			{
				base.IsEnabled = true;
			}
		}
	}

	private async void ExportMaster(object sender, RoutedEventArgs e)
	{
		base.IsEnabled = false;
		try
		{
			await MasterEditor.ExportCurrent();
			Status.Text = MasterEditor.Status.Text;
		}
		finally
		{
			base.IsEnabled = true;
		}
	}

	private void ExportAudio(object sender, RoutedEventArgs e)
	{
		ExportPackage(audio: true);
	}

	private void ExportDialogue(object sender, RoutedEventArgs e)
	{
		ExportPackage(audio: false);
	}

	private async void ExportPackage(bool audio)
	{
		ReadOptions();
		SaveFileDialog pick = new SaveFileDialog
		{
			Filter = "ZIP|*.zip",
			FileName = string.Concat(string.Concat(((settings.Title.Length == 0) ? "FGO-MOD" : settings.Title).Select((char c) => (!Path.GetInvalidFileNameChars().Contains(c)) ? c : '_')), audio ? "-Audio.zip" : "-Dialogue.zip")
		};
		if (DialogSink.ShowDialog(pick, Window.GetWindow(this)) != true)
		{
			return;
		}
		base.IsEnabled = false;
		Message("Building...");
		try
		{
			await Task.Run(delegate
			{
				ModPackage.Export(gameRoot, root, settings, pick.FileName, audio);
			});
			Message("Package exported: " + pick.FileName);
		}
		catch (Exception ex)
		{
			Message("Export failed: " + ex.Message);
		}
		finally
		{
			base.IsEnabled = true;
		}
	}

	private void OpenFolder(object sender, RoutedEventArgs e)
	{
		Directory.CreateDirectory(root);
		DialogSink.Open(root);
	}

	private void FitDialogueColumns(object sender, SizeChangedEventArgs e)
	{
		if (Lines.Columns.Count >= 2)
		{
			double val = Lines.ActualWidth - Lines.RowHeaderWidth - Lines.Columns[0].Width.Value - SystemParameters.VerticalScrollBarWidth - 2.0;
			Lines.Columns[1].Width = new DataGridLength(Math.Max(160.0, val));
		}
	}

	private void NumberRow(object sender, DataGridRowEventArgs e)
	{
		e.Row.Header = (e.Row.GetIndex() + 1).ToString("D2");
	}

	private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
	{
		while (node != null)
		{
			if (node is T result)
			{
				return result;
			}
			node = VisualTreeHelper.GetParent(node);
		}
		return null;
	}

	private void StartDrag(object sender, MouseButtonEventArgs e)
	{
		dragLine = null;
		if (Ancestor<DataGridRowHeader>(e.OriginalSource as DependencyObject) != null)
		{
			dragLine = Ancestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as ModLine;
			dragStart = e.GetPosition(Lines);
		}
	}

	private void DragLine(object sender, MouseEventArgs e)
	{
		if (dragLine != null && e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(Lines) - dragStart).Length > SystemParameters.MinimumVerticalDragDistance)
		{
			ModLine data = dragLine;
			dragLine = null;
			try
			{
				DragDrop.DoDragDrop(Lines, data, DragDropEffects.Move);
			}
			finally
			{
				StopDragScroll();
			}
		}
	}

	private static T? Descendant<T>(DependencyObject node) where T : DependencyObject
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(node, i);
			if (child is T result)
			{
				return result;
			}
			T val = Descendant<T>(child);
			if (val != null)
			{
				return val;
			}
		}
		return null;
	}

	private void UpdateDragScroll(Point point)
	{
		if (point.X < 0.0 || point.X > Lines.ActualWidth || point.Y < 0.0 || point.Y > Lines.ActualHeight)
		{
			StopDragScroll();
			return;
		}
		dragDirection = ((point.Y < 48.0) ? (-1) : ((point.Y > Lines.ActualHeight - 28.0) ? 1 : 0));
		if (dragDirection == 0)
		{
			StopDragScroll();
			return;
		}
		if (dragViewport == null)
		{
			dragViewport = Descendant<ScrollViewer>(Lines);
		}
		if (dragViewport == null)
		{
			return;
		}
		if (dragScroll == null)
		{
			dragScroll = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(80.0)
			};
			dragScroll.Tick += delegate
			{
				if (dragDirection < 0)
				{
					dragViewport?.LineUp();
				}
				else if (dragDirection > 0)
				{
					dragViewport?.LineDown();
				}
			};
		}
		dragScroll.Start();
	}

	private void StopDragScroll()
	{
		dragScroll?.Stop();
		dragViewport = null;
		dragDirection = 0;
	}

	private void ScrollDrag(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(typeof(ModLine)))
		{
			UpdateDragScroll(e.GetPosition(Lines));
			e.Effects = DragDropEffects.Move;
		}
		else
		{
			StopDragScroll();
		}
	}

	private void LeaveDrag(object sender, DragEventArgs e)
	{
		Point position = e.GetPosition(Lines);
		if (position.X < 0.0 || position.X > Lines.ActualWidth || position.Y < 0.0 || position.Y > Lines.ActualHeight)
		{
			StopDragScroll();
		}
	}

	private void DropLine(object sender, DragEventArgs e)
	{
		StopDragScroll();
		if (Events.SelectedItem is ModDialogue modDialogue && e.Data.GetData(typeof(ModLine)) is ModLine modLine && Ancestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is ModLine item)
		{
			int num = modDialogue.Lines.IndexOf(modLine);
			int num2 = modDialogue.Lines.IndexOf(item);
			if (num >= 0 && num2 >= 0)
			{
				modDialogue.Lines.Move(num, num2);
				Lines.Items.Refresh();
				Lines.SelectedItem = modLine;
			}
			e.Handled = true;
		}
	}

	private void UpdateLoadingSelection()
	{
		if (LoadingEnabled == null)
		{
			return;
		}
		ModQuest q = Quest.SelectedItem as ModQuest;
		bool flag = q != null && TargetKind.SelectedIndex == 0;
		LoadingEnabled.IsEnabled = flag;
		updatingLoadingSelection = true;
		try
		{
			LoadingEnabled.IsChecked = flag && loadingImages.Entries.Any((ModLoadingEntry e) => e.Quest == q.Id && e.Enabled);
		}
		finally
		{
			updatingLoadingSelection = false;
		}
		LoadingPreview.Source = null;
		string path = (flag ? ModLoading.Source(root, q.Id) : "");
		if (File.Exists(path))
		{
			LoadingPreview.Source = TextureDocument.LoadPng(path);
		}
		LoadingInfo.Text = (!flag) ? "Loading images are for quests only." : (File.Exists(path) ? "Image chosen. Save loading images, then restart the game." : "No image of your own: the game's loading screen shows.");
	}

	private void LoadingChanged(object sender, RoutedEventArgs e)
	{
		if (updatingLoadingSelection)
		{
			return;
		}
		object selectedItem = Quest.SelectedItem;
		ModQuest q = selectedItem as ModQuest;
		if (q != null && TargetKind.SelectedIndex == 0)
		{
			ModLoadingEntry modLoadingEntry = loadingImages.Entries.FirstOrDefault((ModLoadingEntry x) => x.Quest == q.Id);
			if (modLoadingEntry == null)
			{
				modLoadingEntry = new ModLoadingEntry
				{
					Quest = q.Id
				};
				loadingImages.Entries.Add(modLoadingEntry);
			}
			modLoadingEntry.Enabled = LoadingEnabled.IsChecked == true;
			FilterTargets(this, new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None));
		}
	}

	private void ImportLoadingImage(object sender, RoutedEventArgs e)
	{
		object selectedItem = Quest.SelectedItem;
		ModQuest q = selectedItem as ModQuest;
		if (q == null || TargetKind.SelectedIndex != 0)
		{
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Choose a loading image",
			Filter = "Images" + "|*.png;*.jpg;*.jpeg;*.bmp"
		};
		if (DialogSink.ShowDialog(openFileDialog, Window.GetWindow(this)) != true)
		{
			return;
		}
		try
		{
			ModLoading.Import(root, q.Id, openFileDialog.FileName);
			ModLoadingEntry modLoadingEntry = loadingImages.Entries.FirstOrDefault((ModLoadingEntry x) => x.Quest == q.Id);
			if (modLoadingEntry == null)
			{
				loadingImages.Entries.Add(new ModLoadingEntry
				{
					Quest = q.Id
				});
			}
			else
			{
				modLoadingEntry.Enabled = true;
			}
			FilterTargets(this, new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None));
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
	}

	private void PreviewLoadingImage(object sender, RoutedEventArgs e)
	{
		if (LoadingPreview.Source != null)
		{
			Window obj = new Window
			{
				Owner = Window.GetWindow(this),
				Title = "Loading image",
				Width = 960.0,
				Height = 580.0,
				Content = new Image
				{
					Source = LoadingPreview.Source,
					Stretch = Stretch.Uniform
				},
				Background = Brushes.Black,
				WindowStartupLocation = WindowStartupLocation.CenterOwner
			};
			WindowTheme.Apply(obj);
			DialogSink.ShowDialog(obj);
		}
	}

	private async void SaveLoadingImages(object sender, RoutedEventArgs e)
	{
		base.IsEnabled = false;
		try
		{
			await Task.Run(delegate
			{
				ModLoading.Build(gameRoot, root, loadingImages);
			});
			Message("Loading images saved; they show the next time the game starts.");
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
		finally
		{
			base.IsEnabled = true;
			UpdateLoadingSelection();
		}
	}
}
