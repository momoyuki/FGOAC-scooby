using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FGOLocalPlatform;

/// <summary>
/// A folder of named .json files shown in a dropdown: deck loadouts and draw-rate presets. The
/// name is the file name. Export copies a file out (Desktop first) and Import copies one in.
/// </summary>
internal static class PresetFolder
{
	public static string Ensure(string folder)
	{
		Directory.CreateDirectory(folder);
		return folder;
	}

	public static string PathFor(string folder, string name)
	{
		return Path.Combine(folder, name + ".json");
	}

	/// <summary>A name for a new file in the folder: characters a file name cannot hold become dashes, and a taken name gets (2), (3) and on.</summary>
	public static string FreeName(string folder, string name)
	{
		foreach (char c in Path.GetInvalidFileNameChars())
		{
			name = name.Replace(c, '-');
		}
		string free = name;
		for (int n = 2; File.Exists(PathFor(folder, free)); n++)
		{
			free = $"{name} ({n})";
		}
		return free;
	}

	public static void Fill(ComboBox box, string folder, string? select)
	{
		string? current = select ?? box.SelectedItem as string;
		List<string> names = new List<string>();
		if (Directory.Exists(folder))
		{
			names.AddRange(Directory.GetFiles(folder, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy((string n) => n, StringComparer.OrdinalIgnoreCase));
		}
		box.ItemsSource = names;
		box.SelectedItem = ((current != null && names.Contains(current)) ? current : names.FirstOrDefault());
	}

	public static bool Export(Window owner, string source, string suggestedName)
	{
		SaveFileDialog dialog = new SaveFileDialog
		{
			Title = "Export",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			FileName = suggestedName + ".json",
			Filter = "FGOAC scooby file (*.json)|*.json",
			DefaultExt = ".json",
			AddExtension = true
		};
		if (DialogSink.ShowDialog(dialog, owner) != true)
		{
			return false;
		}
		File.Copy(source, dialog.FileName, overwrite: true);
		return true;
	}

	public static string? Import(Window owner, string folder, Func<string, bool> looksRight, string refusal)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "Import",
			Filter = "FGOAC scooby file (*.json)|*.json"
		};
		if (DialogSink.ShowDialog(dialog, owner) != true)
		{
			return null;
		}
		if (!looksRight(File.ReadAllText(dialog.FileName)))
		{
			throw new InvalidDataException(refusal);
		}
		// The file name is the name, so importing twice asks before replacing what is there.
		string name = Path.GetFileNameWithoutExtension(dialog.FileName);
		string target = PathFor(Ensure(folder), name);
		if (File.Exists(target) && ThemedMessageBox.Show(owner, "You already have \"" + name + "\". Replace it with the imported file?", "Import", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return null;
		}
		AtomicFile.WriteAllBytes(target, File.ReadAllBytes(dialog.FileName));
		return name;
	}

	public static void OpenFolder(string folder)
	{
		DialogSink.Open("explorer.exe", "\"" + Ensure(folder) + "\"");
	}
}
