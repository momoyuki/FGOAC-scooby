using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FgoArcadeStudio.Core;
using Hanhua;

namespace FGOLocalPlatform;

public static class ModPortraits
{
	public static string CustomDirectory(string root, string? library = null)
	{
		if (!string.IsNullOrWhiteSpace(library))
		{
			return Path.GetFullPath(Path.Combine(root, library));
		}
		return Path.Combine(root, "Dialogue", "Assets", "Portraits");
	}

	public static string CustomIndex(string root, string? library = null)
	{
		return Path.Combine(CustomDirectory(root, library), "portraits.json");
	}

	public static string CatalogFile(string root)
	{
		return Path.Combine(root, "Dialogue", "Catalog", "portraits.json");
	}

	public static bool IsCustom(string reference)
	{
		return Regex.IsMatch(reference, "^MOD_PORTRAIT_[0-9a-f]{32}$");
	}

	private static List<ModPortraitReference> ReadIndex(string file)
	{
		if (!File.Exists(file))
		{
			return new List<ModPortraitReference>();
		}
		List<ModPortraitReference> list = JsonSerializer.Deserialize<List<ModPortraitReference>>(File.ReadAllText(file), ModStorage.Json) ?? throw new InvalidDataException("Custom portrait index is empty.");
		if (list.Any((ModPortraitReference p) => !IsCustom(p.Reference)) || list.Select((ModPortraitReference p) => p.Reference).Distinct().Count() != list.Count)
		{
			throw new InvalidDataException("Custom portrait reference is invalid or duplicated.");
		}
		return list.Select((ModPortraitReference p) => (p.LegacySlot == null || !(p.Name == p.LegacySlot)) ? p : p with
		{
			Name = "Custom_" + p.Reference.Substring(13, 8)
		}).ToList();
	}

	public static List<ModPortraitReference> Custom(string root, string? library = null)
	{
		List<ModPortraitReference> list = ReadIndex(CustomIndex(root));
		if (string.IsNullOrWhiteSpace(library) || string.Equals(CustomDirectory(root), CustomDirectory(root, library), StringComparison.OrdinalIgnoreCase))
		{
			return list;
		}
		return ReadIndex(CustomIndex(root, library)).Concat(list).DistinctBy((ModPortraitReference p) => p.Reference).ToList();
	}

	public static ModPortraitReference[] References(string gameRoot, string root, string? library = null)
	{
		return new ModPortraitReference[1]
		{
			new ModPortraitReference("", "No portrait")
		}.Concat(from p in ModDialogueCompiler.NativePortraits(gameRoot, root)
			select new ModPortraitReference(p, p)).Concat(Custom(root, library)).ToArray();
	}

	public static string PreviewPath(string root, string name, string? library = null)
	{
		if (Path.GetFileName(name) != name)
		{
			return "";
		}
		if (IsCustom(name))
		{
			string text = Path.Combine(CustomDirectory(root, library), name + ".png");
			if (!File.Exists(text))
			{
				return Path.Combine(CustomDirectory(root), name + ".png");
			}
			return text;
		}
		string[] array = new string[2]
		{
			Path.Combine(root, "Dialogue", "Catalog", "Portraits", name + ".png"),
			Path.Combine(root, "Dialogue", "Portraits", name + ".png")
		};
		foreach (string text2 in array)
		{
			if (File.Exists(text2))
			{
				return text2;
			}
		}
		return "";
	}

	public static (string Archive, UiSpriteTableEntry Entry) FindSlot(string gameRoot, string name)
	{
		foreach (string item in Directory.EnumerateFiles(Path.Combine(gameRoot, "rom", "sprite"), "*_table.bin"))
		{
			byte[] array = File.ReadAllBytes(item);
			if (Encoding.UTF8.GetString(array).Contains(name, StringComparison.Ordinal))
			{
				UiSpriteTableEntry uiSpriteTableEntry = UiSpriteTable.Parse(array).Entries.FirstOrDefault((UiSpriteTableEntry e) => e.SpriteName == name);
				if (uiSpriteTableEntry != null)
				{
					return (Archive: item.Replace("_table.bin", ".farc"), Entry: uiSpriteTableEntry);
				}
			}
		}
		throw new InvalidDataException("Native portrait slot not found.");
	}

	public static ModPortraitReference Import(string root, string source, string? name = null, string? legacySlot = null, string? library = null)
	{
		BitmapSource b = TextureDocument.LoadPng(source);
		List<ModPortraitReference> list = ReadIndex(CustomIndex(root, library));
		string text = "MOD_PORTRAIT_" + Guid.NewGuid().ToString("N");
		string label = (string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(source) : name.Trim());
		if (label == legacySlot || Regex.IsMatch(label, "^EXP_[A-Z0-9_]+_talk_", RegexOptions.IgnoreCase))
		{
			label = "Custom_" + text.Substring(13, 8);
		}
		string text2 = label;
		int num = 2;
		List<ModPortraitReference> source2 = Custom(root, library);
		while (source2.Any((ModPortraitReference p) => string.Equals(p.Name, label, StringComparison.OrdinalIgnoreCase)))
		{
			label = text2 + " (" + num++ + ")";
		}
		ModPortraitReference modPortraitReference = new ModPortraitReference(text, label, legacySlot);
		string text3 = Path.Combine(CustomDirectory(root, library), modPortraitReference.Reference + ".png");
		Directory.CreateDirectory(CustomDirectory(root, library));
		string text4 = text3 + ".tmp";
		TextureDocument.SavePng(b, text4);
		File.Move(text4, text3);
		list.Add(modPortraitReference);
		ModStorage.AtomicWrite(CustomIndex(root, library), JsonSerializer.Serialize(list, ModStorage.Json));
		return modPortraitReference;
	}

	public static (List<ModPortraitReference> Added, List<string> Errors) ImportBatch(string root, IEnumerable<string> sources, string? library = null)
	{
		List<ModPortraitReference> list = new List<ModPortraitReference>();
		List<string> list2 = new List<string>();
		foreach (string source in sources)
		{
			try
			{
				list.Add(Import(root, source, null, null, library));
			}
			catch (Exception ex)
			{
				list2.Add(Path.GetFileName(source) + ": " + ex.Message);
			}
		}
		return (Added: list, Errors: list2);
	}

	private static string OwnerIndex(string root, string reference, string? library)
	{
		if (!ReadIndex(CustomIndex(root, library)).Any((ModPortraitReference p) => p.Reference == reference))
		{
			return CustomIndex(root);
		}
		return CustomIndex(root, library);
	}

	public static void Rename(string root, string reference, string name, string? library = null)
	{
		if (!IsCustom(reference) || string.IsNullOrWhiteSpace(name))
		{
			throw new InvalidDataException("Enter a name for the custom portrait.");
		}
		string text = OwnerIndex(root, reference, library);
		List<ModPortraitReference> list = ReadIndex(text);
		int num = list.FindIndex((ModPortraitReference p) => p.Reference == reference);
		if (num < 0)
		{
			throw new InvalidDataException("Custom portrait reference is invalid or duplicated.");
		}
		list[num] = list[num]with
		{
			Name = name.Trim()
		};
		ModStorage.AtomicWrite(text, JsonSerializer.Serialize(list, ModStorage.Json));
	}

	public static void Remove(string root, string reference, IEnumerable<ModDialogue> dialogues, string? library = null)
	{
		if (IsCustom(reference))
		{
			if (dialogues.SelectMany((ModDialogue d) => d.Lines).Any((ModLine l) => l.Portrait == reference))
			{
				throw new InvalidDataException("This portrait is still referenced by dialogue. Update those lines first.");
			}
			string text = OwnerIndex(root, reference, library);
			List<ModPortraitReference> list = ReadIndex(text);
			list.RemoveAll((ModPortraitReference p) => p.Reference == reference);
			ModStorage.AtomicWrite(text, JsonSerializer.Serialize(list, ModStorage.Json));
			string path = Path.Combine(Path.GetDirectoryName(text), reference + ".png");
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
	}

	public static void MigrateLegacy(string gameRoot, string root, ModSettings settings)
	{
		if (settings.PortraitMigrationHandled)
		{
			return;
		}
		List<ModPortraitReference> list = Custom(root);
		string[] array = ModDialogueCompiler.NativePortraits(gameRoot, root);
		foreach (string slot in array)
		{
			string text = Path.Combine(CustomDirectory(root), slot + ".png");
			if (!File.Exists(text))
			{
				continue;
			}
			ModPortraitReference modPortraitReference = list.FirstOrDefault((ModPortraitReference p) => p.LegacySlot == slot);
			if (modPortraitReference == null)
			{
				modPortraitReference = Import(root, text, slot, slot);
				list.Add(modPortraitReference);
			}
			foreach (ModLine item in from l in settings.Dialogues.SelectMany((ModDialogue d) => d.Lines)
				where l.Portrait == slot
				select l)
			{
				item.Portrait = modPortraitReference.Reference;
			}
		}
		settings.PortraitMigrationHandled = true;
	}

	public static void RetireLegacyImports(string root, ModSettings settings)
	{
		if (!settings.PortraitMigrationHandled)
		{
			return;
		}
		foreach (ModPortraitReference item in from p in Custom(root)
			where p.LegacySlot != null
			select p)
		{
			string legacySlot = item.LegacySlot;
			if (!(Path.GetFileName(legacySlot) != legacySlot))
			{
				string text = Path.Combine(CustomDirectory(root), legacySlot + ".png");
				if (File.Exists(text))
				{
					string text2 = Path.Combine(CustomDirectory(root), "Legacy", legacySlot + "." + Guid.NewGuid().ToString("N") + ".png");
					Directory.CreateDirectory(Path.GetDirectoryName(text2));
					File.Move(text, text2);
				}
			}
		}
	}

	public static void Build(string gameRoot, string root, string output, IEnumerable<ModDialogue> dialogues, string? library = null)
	{
		string[] array = (from l in dialogues.SelectMany((ModDialogue d) => d.Lines)
			select l.Portrait).Where(IsCustom).Distinct().OrderBy<string, string>((string p) => p, StringComparer.Ordinal)
			.ToArray();
		if (array.Length == 0)
		{
			return;
		}
		string text = Path.Combine(output, "rom", "sprite");
		Directory.CreateDirectory(text);
		string[] array2 = array;
		foreach (string text2 in array2)
		{
			string text3 = text2.Substring(13);
			string text4 = "spr_mod_portrait_" + text3;
			byte[] buffer = EncodePortrait(TextureDocument.LoadPng(PreviewPath(root, text2, library)));
			using MemoryStream memoryStream = new MemoryStream();
			using (BinaryWriter binaryWriter = new BinaryWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
			{
				binaryWriter.Write(55597140);
				binaryWriter.Write(1);
				binaryWriter.Write(16843009);
				binaryWriter.Write(16);
				binaryWriter.Write(buffer);
			}
			using MemoryStream memoryStream2 = new MemoryStream();
			using (BinaryWriter binaryWriter2 = new BinaryWriter(memoryStream2, Encoding.UTF8, leaveOpen: true))
			{
				binaryWriter2.Write(336134422);
				binaryWriter2.Write(1);
				binaryWriter2.Write(0);
				WriteString(binaryWriter2, "mod_portrait_" + text3 + "_nomerge_bc7_000");
				WriteString(binaryWriter2, text2);
				int[] array3 = new int[6] { 512, 1024, 0, 0, 466, 634 };
				foreach (int value in array3)
				{
					binaryWriter2.Write(value);
				}
			}
			File.WriteAllBytes(Path.Combine(text, text4 + "_table.bin"), memoryStream2.ToArray());
			File.WriteAllBytes(Path.Combine(text, text4 + ".farc"), ModDialogueCompiler.SingleArchive("texture.bin", memoryStream.ToArray()));
		}
	}

	private static void WriteString(BinaryWriter writer, string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		writer.Write(bytes.Length);
		writer.Write(bytes);
	}

	private static byte[] EncodePortrait(BitmapSource image)
	{
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			double num = Math.Min(466.0 / (double)image.PixelWidth, 634.0 / (double)image.PixelHeight);
			double num2 = (double)image.PixelWidth * num;
			double num3 = (double)image.PixelHeight * num;
			drawingContext.DrawImage(image, new Rect((466.0 - num2) / 2.0, 634.0 - num3, num2, num3));
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(512, 1024, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(renderTargetBitmap, PixelFormats.Bgra32, null, 0.0);
		byte[] array = new byte[2097152];
		formatConvertedBitmap.CopyPixels(array, 2048, 0);
		byte[] array2 = new byte[array.Length];
		for (int i = 0; i < 1024; i++)
		{
			Buffer.BlockCopy(array, i * 512 * 4, array2, (1023 - i) * 512 * 4, 2048);
		}
		byte[] array3 = TextureDocument.Encode(array2, 512, 1024, 130);
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write(72374356);
		binaryWriter.Write(1);
		binaryWriter.Write(16843009);
		binaryWriter.Write(16);
		binaryWriter.Write(38819924);
		binaryWriter.Write(512);
		binaryWriter.Write(1024);
		binaryWriter.Write(130);
		binaryWriter.Write(0);
		binaryWriter.Write(array3.Length);
		binaryWriter.Write(array3);
		return memoryStream.ToArray();
	}

	public static void OrganizeCatalog(string root)
	{
		string text = Path.Combine(root, "Dialogue", "Catalog");
		Directory.CreateDirectory(text);
		string[] array = new string[2] { "portraits.json", "Portraits" };
		foreach (string text2 in array)
		{
			string text3 = Path.Combine(root, "Dialogue", text2);
			string text4 = Path.Combine(text, text2);
			if (File.Exists(text3) && !File.Exists(text4))
			{
				File.Move(text3, text4);
			}
			else if (Directory.Exists(text3) && !Directory.Exists(text4))
			{
				Directory.Move(text3, text4);
			}
		}
	}
}
