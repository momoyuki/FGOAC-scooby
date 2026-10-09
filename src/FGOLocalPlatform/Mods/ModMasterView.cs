using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Hanhua;
using Microsoft.Win32;

namespace FGOLocalPlatform;

public partial class ModMasterView : UserControl, IComponentConnector
{
	private string gameRoot = "";

	private string root = "";

	private ModMasterSettings settings = new ModMasterSettings();

	public Func<(string Title, string Author, string Version, string Description)>? PublicationMetadata { get; set; }

	public ModMasterView()
	{
		InitializeComponent();
	}

	public void Configure(string gameRoot, string root)
	{
		this.gameRoot = gameRoot;
		this.root = root;
		Reload();
	}

	public void SyncEnabled()
	{
		CheckBox enabled = Enabled;
		bool value = (settings.Enabled = ModInstall.Enabled(root, "master"));
		enabled.IsChecked = value;
	}

	public void Reload()
	{
		try
		{
			settings = ModMaster.Load(root);
			Enabled.IsChecked = settings.Enabled;
			Slots.ItemsSource = ModMaster.EditableSlots;
			Slots.SelectedIndex = 0;
		}
		catch (Exception ex)
		{
			base.IsEnabled = false;
			Message(ex.Message);
		}
	}

	private void Message(string text)
	{
		Status.Text = text;
	}

	private void SelectSlot(object sender, SelectionChangedEventArgs e)
	{
		if (gameRoot.Length == 0 || !(Slots.SelectedItem is MasterSprite masterSprite))
		{
			return;
		}
		try
		{
			Original.Source = ModMaster.Original(gameRoot, masterSprite);
			Replacement.Source = ModMaster.Preview(gameRoot, root, settings, masterSprite);
			string text = ((masterSprite == ModMaster.GenderExtras[0] || masterSprite == ModMaster.GenderExtras[1]) ? "The confirmation image is replaced on its own; until then the game's shows." : ((masterSprite == ModMaster.GenderExtras[3] || masterSprite == ModMaster.GenderExtras[4]) ? "Unless replaced, the image when not chosen is built from the portrait and the background." : ((masterSprite == ModMaster.GenderExtras[2]) ? "The background of the image when not chosen. Keep its transparency and size." : "")));
			Status.Text = masterSprite.Name + " · " + masterSprite.Width + "×" + masterSprite.Height + ((text.Length == 0) ? "" : ("\n" + text));
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
	}

	private void Read()
	{
		settings.Enabled = Enabled.IsChecked == true;
		if (PublicationMetadata != null)
		{
			(string, string, string, string) tuple = PublicationMetadata();
			settings.Title = tuple.Item1;
			settings.Author = tuple.Item2;
			settings.Version = tuple.Item3;
			settings.Description = tuple.Item4;
		}
	}

	private void Import(object sender, RoutedEventArgs e)
	{
		if (!(Slots.SelectedItem is MasterSprite slot))
		{
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "PNG|*.png"
		};
		if (DialogSink.ShowDialog(openFileDialog, Window.GetWindow(this)) != true)
		{
			return;
		}
		try
		{
			ModMaster.Import(root, settings, slot, openFileDialog.FileName);
			SelectSlot(this, null);
			Message("Replaced. Save to keep it.");
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
	}

	private void ImportBatch(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "PNG|*.png",
			Multiselect = true
		};
		if (DialogSink.ShowDialog(openFileDialog, Window.GetWindow(this)) != true)
		{
			return;
		}
		try
		{
			(string, MasterSprite)[] array = openFileDialog.FileNames.Select((string f) => (File: f, Slot: ModMaster.EditableSlots.SingleOrDefault((MasterSprite s) => s.Name == Path.GetFileNameWithoutExtension(f)) ?? throw new InvalidDataException("Replacing several needs the exported file names."))).ToArray();
			(string, MasterSprite)[] array2 = array;
			for (int num = 0; num < array2.Length; num++)
			{
				(string, MasterSprite) tuple = array2[num];
				BitmapSource bitmapSource = TextureDocument.LoadPng(tuple.Item1);
				if (bitmapSource.PixelWidth != tuple.Item2.Width || bitmapSource.PixelHeight != tuple.Item2.Height)
				{
					throw new InvalidDataException("The image must be the size of the game's: " + tuple.Item2.Name);
				}
			}
			array2 = array;
			for (int num = 0; num < array2.Length; num++)
			{
				(string, MasterSprite) tuple2 = array2[num];
				ModMaster.Import(root, settings, tuple2.Item2, tuple2.Item1);
			}
			SelectSlot(this, null);
			Message("Replaced. Save to keep it.");
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
	}

	private void Restore(object sender, RoutedEventArgs e)
	{
		if (Slots.SelectedItem is MasterSprite masterSprite)
		{
			settings.Images.Remove(masterSprite.Name);
			SelectSlot(this, null);
			Message("Back to the game's image. Save to keep it.");
		}
	}

	private void ExportTemplates(object sender, RoutedEventArgs e)
	{
		try
		{
			string text = FolderPicker.Show(Window.GetWindow(this), "Export the game's images to");
			if (text != null)
			{
				ModMaster.ExportTemplates(gameRoot, text);
				Message("The game's images are exported.");
			}
		}
		catch (Exception ex)
		{
			Message(ex.Message);
		}
	}

	public async Task SaveCurrent()
	{
		Read();
		base.IsEnabled = false;
		Message("Building...");
		try
		{
			if (settings.Images.Count != 0)
			{
				await Task.Run(delegate
				{
					ModMaster.Build(gameRoot, root, settings);
				});
			}
			else
			{
				settings.Enabled = false;
				Enabled.IsChecked = false;
				ModMaster.Save(root, settings);
			}
			Message("Saved. It applies the next time the game starts.");
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

	private async void Save(object sender, RoutedEventArgs e)
	{
		await SaveCurrent();
	}

	public async Task ExportCurrent()
	{
		Read();
		SaveFileDialog dialog = new SaveFileDialog
		{
			Filter = "ZIP|*.zip",
			FileName = "FGO-MOD-Master.zip"
		};
		if (DialogSink.ShowDialog(dialog, Window.GetWindow(this)) != true)
		{
			return;
		}
		base.IsEnabled = false;
		Message("Building...");
		try
		{
			await Task.Run(delegate
			{
				ModMaster.Export(gameRoot, root, settings, dialog.FileName);
			});
			Message("Package exported: " + dialog.FileName);
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
}
