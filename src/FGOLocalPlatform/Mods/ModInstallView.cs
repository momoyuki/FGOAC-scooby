using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace FGOLocalPlatform;

public partial class ModInstallView : UserControl, IComponentConnector
{
	private string root = "";

	public ModInstallView()
	{
		InitializeComponent();
		base.Loaded += delegate
		{
			if (root.Length > 0)
			{
				Refresh();
			}
		};
	}

	public void Configure(string root)
	{
		this.root = root;
		try
		{
			if (!GameRunning())
			{
				ModLibrary.MigrateLegacy(root);
			}
		}
		catch (Exception ex)
		{
			Status.Text = ex.Message;
		}
		Refresh();
	}

	private static string Kind(string component)
	{
		return (component == "audio") ? "Music" : ((!(component == "dialogue")) ? "Master portraits" : "Dialogue");
	}

	public void Refresh()
	{
		try
		{
			string text = (Packages.SelectedItem as TabItem)?.Uid;
			List<ImportedMod> list = ModLibrary.List(root);
			ModLibraryState modLibraryState = ModLibrary.Load(root);
			Packages.Items.Clear();
			Empty.Visibility = ((list.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
			Packages.Visibility = ((list.Count == 0) ? Visibility.Collapsed : Visibility.Visible);
			List<string> list2 = new List<string>();
			string[] array = new string[3] { "audio", "dialogue", "master" };
			foreach (string component in array)
			{
				string id;
				ImportedMod importedMod = (modLibraryState.Selected.TryGetValue(component, out id) ? list.SingleOrDefault((ImportedMod p) => p.Id == id && p.Info.Component == component) : null);
				if (id != null && importedMod == null)
				{
					throw new InvalidDataException("The chosen package is not there.");
				}
				bool flag = importedMod != null || ModInstall.Enabled(root, component);
				list2.Add(Kind(component) + " · " + (importedMod?.Info.Title ?? "your own") + (flag ? "" : (" · " + "switched off")));
			}
			Sources.Text = string.Join("\n", list2);
			foreach (ImportedMod package in list)
			{
				StackPanel stackPanel = new StackPanel
				{
					Margin = new Thickness(16.0)
				};
				stackPanel.Children.Add(new TextBlock
				{
					Text = package.Info.Title,
					FontSize = 16.0,
					FontWeight = FontWeights.SemiBold,
					TextWrapping = TextWrapping.Wrap
				});
				stackPanel.Children.Add(new TextBlock
				{
					Text = Kind(package.Info.Component) + " · " + package.Info.Version + " · " + package.Info.Author,
					Margin = new Thickness(0.0, 8.0, 0.0, 16.0),
					TextWrapping = TextWrapping.Wrap
				});
				string value;
				CheckBox use = new CheckBox
				{
					Content = "Use this package instead of my own",
					HorizontalAlignment = HorizontalAlignment.Left,
					IsChecked = (modLibraryState.Selected.TryGetValue(package.Info.Component, out value) && value == package.Id)
				};
				use.Click += delegate
				{
					Select(package, use.IsChecked == true);
				};
				stackPanel.Children.Add(use);
				stackPanel.Children.Add(new TextBlock
				{
					Text = "One package per kind. Opening a tab changes nothing; Use does.",
					TextWrapping = TextWrapping.Wrap,
					Foreground = (Brush)FindResource("TextSoftBrush"),
					Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
				});
				TabItem tabItem = new TabItem
				{
					Header = package.Info.Title + " · " + Kind(package.Info.Component),
					Content = stackPanel,
					Uid = package.Id
				};
				Packages.Items.Add(tabItem);
				if (package.Id == text)
				{
					Packages.SelectedItem = tabItem;
				}
			}
		}
		catch (Exception ex)
		{
			Status.Text = ex.Message;
		}
	}

	private static bool GameRunning()
	{
		string[] array = new string[2] { "ago", "amdaemon" };
		for (int i = 0; i < array.Length; i++)
		{
			Process[] processesByName = Process.GetProcessesByName(array[i]);
			bool flag = processesByName.Length != 0;
			Process[] array2 = processesByName;
			for (int j = 0; j < array2.Length; j++)
			{
				array2[j].Dispose();
			}
			if (flag)
			{
				return true;
			}
		}
		return false;
	}

	private async void Install(object sender, RoutedEventArgs e)
	{
		bool load = LoadOnInstall.IsChecked == true;
		if (load && GameRunning())
		{
			Status.Text = "The game is running: untick Use it once imported to only import, or quit the game first.";
			return;
		}
		OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = "Mod package|*.zip"
		};
		if (DialogSink.ShowDialog(dialog, Window.GetWindow(this)) != true)
		{
			return;
		}
		base.IsEnabled = false;
		try
		{
			string id = await Task.Run(() => ModInstall.Install(root, dialog.FileName, load));
			Refresh();
			Packages.SelectedItem = Packages.Items.Cast<TabItem>().FirstOrDefault((TabItem t) => t.Uid == id);
			Status.Text = "Package imported; your own edits are kept.";
		}
		catch (Exception ex)
		{
			Status.Text = "Import failed: " + ex.Message;
		}
		finally
		{
			base.IsEnabled = true;
		}
	}

	private void Select(ImportedMod package, bool use)
	{
		if (GameRunning())
		{
			Status.Text = "Quit the game before changing which package is used.";
			Refresh();
			return;
		}
		try
		{
			ModLibrary.Select(root, package.Info.Component, use ? package.Id : null);
			Status.Text = "Saved; it applies the next time the game starts.";
		}
		catch (Exception ex)
		{
			Status.Text = ex.Message;
		}
		Refresh();
	}
}
