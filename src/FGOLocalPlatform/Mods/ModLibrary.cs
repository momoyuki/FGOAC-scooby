using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FGOLocalPlatform;

public static class ModLibrary
{
	public static void MigrateLegacy(string root)
	{
		if (File.Exists(Path.Combine(root, "packages.json")))
		{
			return;
		}
		string[] array = new string[3] { "audio", "dialogue", "master" };
		foreach (string component in array)
		{
			ModPackageInfo modPackageInfo = ModInstall.Current(root, component);
			if (modPackageInfo == null)
			{
				continue;
			}
			string text = ModInstall.DirectoryName(component);
			string text2 = Path.Combine(root, "InstallTemp", Guid.NewGuid().ToString("N") + ".zip");
			Directory.CreateDirectory(Path.GetDirectoryName(text2));
			try
			{
				using (ZipArchive zipArchive = ZipFile.Open(text2, ZipArchiveMode.Create))
				{
					using (StreamWriter streamWriter = new StreamWriter(zipArchive.CreateEntry("mod-package.json").Open()))
					{
						streamWriter.Write(JsonSerializer.Serialize(modPackageInfo, ModStorage.Json));
					}
					string text3 = Path.Combine(root, text);
					foreach (string item in Directory.EnumerateFiles(text3, "*", SearchOption.AllDirectories))
					{
						zipArchive.CreateEntryFromFile(item, "App/1_MOD/" + text + "/" + Path.GetRelativePath(text3, item).Replace('\\', '/'));
					}
				}
				ModInstall.Install(root, text2, ModInstall.Enabled(root, component));
			}
			finally
			{
				if (File.Exists(text2))
				{
					File.Delete(text2);
				}
			}
		}
		if (!File.Exists(Path.Combine(root, "packages.json")))
		{
			ModStorage.AtomicWrite(Path.Combine(root, "packages.json"), JsonSerializer.Serialize(new ModLibraryState(), ModStorage.Json));
		}
	}

	public static string PackageRoot(string root, string id)
	{
		if (!Regex.IsMatch(id, "\\A[a-f0-9]{32}\\z"))
		{
			throw new InvalidDataException("The package id is not valid.");
		}
		return Path.Combine(root, "Packages", id);
	}

	public static ModLibraryState Load(string root)
	{
		string path = Path.Combine(root, "packages.json");
		ModLibraryState modLibraryState = (File.Exists(path) ? (JsonSerializer.Deserialize<ModLibraryState>(File.ReadAllText(path), ModStorage.Json) ?? throw new InvalidDataException("The list of packages in use is empty.")) : new ModLibraryState());
		if (modLibraryState.SchemaVersion != 1)
		{
			throw new InvalidDataException("The list of packages in use has a version this launcher does not read.");
		}
		foreach (KeyValuePair<string, string> item in modLibraryState.Selected)
		{
			ModInstall.DirectoryName(item.Key);
			PackageRoot(root, item.Value);
		}
		return modLibraryState;
	}

	public static List<ImportedMod> List(string root)
	{
		string path = Path.Combine(root, "Packages");
		List<ImportedMod> list = new List<ImportedMod>();
		if (!Directory.Exists(path))
		{
			return list;
		}
		foreach (string item in Directory.EnumerateDirectories(path).OrderBy<string, string>((string d) => d, StringComparer.Ordinal))
		{
			string fileName = Path.GetFileName(item);
			PackageRoot(root, fileName);
			string[] array = new string[3] { "audio", "dialogue", "master" };
			foreach (string text in array)
			{
				ModPackageInfo modPackageInfo = ModInstall.Current(item, text);
				if (modPackageInfo != null)
				{
					if (modPackageInfo.Component != text)
					{
						throw new InvalidDataException("The package holds another kind of content.");
					}
					list.Add(new ImportedMod(fileName, modPackageInfo));
				}
			}
		}
		return list;
	}

	public static void Select(string root, string component, string? id)
	{
		ModInstall.DirectoryName(component);
		ModLibraryState modLibraryState = Load(root);
		if (id == null)
		{
			modLibraryState.Selected.Remove(component);
		}
		else
		{
			if (ModInstall.Current(PackageRoot(root, id), component)?.Component != component)
			{
				throw new InvalidDataException("The package is not there.");
			}
			modLibraryState.Selected[component] = id;
		}
		string path = Path.Combine(root, "packages.json");
		string text = (File.Exists(path) ? File.ReadAllText(path) : null);
		ModStorage.AtomicWrite(path, JsonSerializer.Serialize(modLibraryState, ModStorage.Json));
		try
		{
			Compose(root);
		}
		catch
		{
			if (text == null)
			{
				File.Delete(path);
			}
			else
			{
				ModStorage.AtomicWrite(path, text);
			}
			throw;
		}
	}

	public static void Compose(string root)
	{
		ModLibraryState modLibraryState = Load(root);
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = new StringBuilder();
		string path = Path.Combine(root, "runtime.ini");
		string text = (File.Exists(path) ? File.ReadAllText(path) : "");
		string[] array = new string[3] { "audio", "dialogue", "master" };
		foreach (string text2 in array)
		{
			string path2 = ModInstall.DirectoryName(text2);
			string section = ((text2 == "audio") ? "bgm" : text2);
			string allowed = ((text2 == "audio") ? "\\A(bgm|track\\d+)\\z" : ((text2 == "dialogue") ? "\\A(dialogue|dialogue\\d+)\\z" : "\\Amaster\\z"));
			string path3 = Path.Combine(root, path2, "runtime.ini");
			string text3 = (File.Exists(path3) ? File.ReadAllText(path3) : string.Concat(from Match m in Regex.Matches(text, "(?ms)^\\[([^\\]\\r\\n]+)\\][^\\r\\n]*\\r?\\n.*?(?=^\\[|\\z)")
				where Regex.IsMatch(m.Groups[1].Value, allowed)
				select m.Value));
			bool flag = Regex.IsMatch(text3, "(?ms)^\\[" + section + "\\]\\r?\\n(?:(?!^\\[).)*?^enabled=1\\r?$");
			if (modLibraryState.Selected.TryGetValue(text2, out string value))
			{
				string text4 = PackageRoot(root, value);
				if (ModInstall.Current(text4, text2)?.Component != text2)
				{
					throw new InvalidDataException("The chosen package is not there.");
				}
				string input = File.ReadAllText(Path.Combine(text4, path2, "runtime.ini"));
				flag = true;
				text3 = Regex.Replace(input, "(?m)^enabled=[01]\\r?$", "enabled=" + (flag ? 1 : 0), RegexOptions.None);
				stringBuilder2.Append(text2 + "=Packages/" + value + "\r\n");
			}
			if (string.IsNullOrWhiteSpace(text3))
			{
				text3 = "[" + section + "]\r\nenabled=0\r\ncount=0\r\n";
			}
			MatchCollection source = Regex.Matches(text3, "(?m)^\\[([^\\]\\r\\n]+)\\]\\r?$");
			if (!source.Cast<Match>().Any((Match m) => m.Groups[1].Value == section) || source.Cast<Match>().Any((Match m) => !Regex.IsMatch(m.Groups[1].Value, allowed)))
			{
				throw new InvalidDataException("The mod settings file App\\1_MOD\\runtime.ini is not valid.");
			}
			stringBuilder.Append(text3.TrimEnd('\r', '\n')).Append("\r\n");
		}
		if (stringBuilder2.Length > 0)
		{
			stringBuilder.Append("[mod_sources]\r\n").Append(stringBuilder2);
		}
		if (stringBuilder.ToString() != text)
		{
			ModStorage.AtomicWrite(path, stringBuilder.ToString());
		}
	}
}
