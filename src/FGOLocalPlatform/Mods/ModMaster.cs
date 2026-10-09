using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using FgoArcadeStudio.Core;
using Hanhua;

namespace FGOLocalPlatform;

public static class ModMaster
{
	public static readonly MasterSprite[] Slots = new MasterSprite[6]
	{
		new MasterSprite("spr_sel_aimeinfo", "SEL_AIMEINFO_icon_player_male_001", 98, 98, "Male Master · Icon"),
		new MasterSprite("spr_sel_aimeinfo", "SEL_AIMEINFO_icon_player_female_001", 98, 98, "Female Master · Icon"),
		new MasterSprite("spr_sel_gender", "SEL_GENDER_icon_player_male_001", 326, 560, "Male Master · Gender selection"),
		new MasterSprite("spr_sel_gender", "SEL_GENDER_icon_player_female_001", 402, 554, "Female Master · Gender selection"),
		new MasterSprite("spr_gam_result", "GAM_RESULT_player_male_001", 912, 986, "Male Master · Result portrait"),
		new MasterSprite("spr_gam_result", "GAM_RESULT_player_female_001", 912, 986, "Female Master · Result portrait")
	};

	public static readonly MasterSprite[] GenderExtras = new MasterSprite[5]
	{
		new MasterSprite("spr_key_win", "KEY_WIN_win_obj_gender_m", 416, 428, "Male Master · Confirmation"),
		new MasterSprite("spr_key_win", "KEY_WIN_win_obj_gender_f", 416, 422, "Female Master · Confirmation"),
		new MasterSprite("spr_sel_gender", "SEL_GENDER_g_btn_base", 604, 604, "Gender selection · Composition background"),
		new MasterSprite("spr_sel_gender", "SEL_GENDER_g_btn_m_base", 604, 614, "Male Master · Unselected image"),
		new MasterSprite("spr_sel_gender", "SEL_GENDER_g_btn_f_base", 604, 606, "Female Master · Unselected image")
	};

	public static readonly MasterSprite[] EditableSlots = new MasterSprite[11]
	{
		Slots[0],
		Slots[1],
		Slots[2],
		Slots[3],
		GenderExtras[2],
		GenderExtras[3],
		GenderExtras[4],
		GenderExtras[0],
		GenderExtras[1],
		Slots[4],
		Slots[5]
	};

	private static bool HasRuntimeSlot(ModMasterSettings settings, int index)
	{
		bool flag = settings.Images.ContainsKey(Slots[index].Name);
		if (!flag)
		{
			bool flag2 = (uint)(index - 2) <= 1u;
			flag = flag2 && (settings.Images.ContainsKey(GenderExtras[index - 2].Name) || settings.Images.ContainsKey(GenderExtras[index + 1].Name) || settings.Images.ContainsKey(GenderExtras[2].Name));
		}
		return flag;
	}

	public static BitmapSource Preview(string gameRoot, string root, ModMasterSettings settings, MasterSprite slot)
	{
		if (settings.Images.ContainsKey(slot.Name))
		{
			return TextureDocument.LoadPng(ImagePath(root, slot.Name));
		}
		if (slot == GenderExtras[3] || slot == GenderExtras[4])
		{
			MasterSprite slot2 = Slots[(slot == GenderExtras[3]) ? 2 : 3];
			return ComposeGenderBase(slot2, Preview(gameRoot, root, settings, slot2), Preview(gameRoot, root, settings, GenderExtras[2]));
		}
		return Original(gameRoot, slot);
	}

	public static ModMasterSettings Load(string root)
	{
		string path = Path.Combine(root, "Master", "settings.json");
		if (!File.Exists(path))
		{
			return new ModMasterSettings();
		}
		return JsonSerializer.Deserialize<ModMasterSettings>(File.ReadAllText(path), ModStorage.Json) ?? throw new InvalidDataException("The Master portrait settings are empty.");
	}

	public static BitmapSource Original(string gameRoot, MasterSprite slot)
	{
		TextureDocument textureDocument = new TextureDocument(Path.Combine(gameRoot, "rom", "sprite", slot.Bundle + ".farc"));
		return textureDocument.Crop(textureDocument.Sprites.Single((Sprite s) => s.Name.Substring(6) == slot.Name), chinese: false);
	}

	public static void ExportTemplates(string gameRoot, string output)
	{
		MasterSprite[] editableSlots = EditableSlots;
		foreach (MasterSprite masterSprite in editableSlots)
		{
			TextureDocument.SavePng(Original(gameRoot, masterSprite), Path.Combine(output, masterSprite.Name + ".png"));
		}
		TextureDocument.SavePng(GenderBackground(gameRoot), Path.Combine(output, "SEL_GENDER_g_btn_base.png"));
		File.WriteAllText(Path.Combine(output, "gender-composition.json"), JsonSerializer.Serialize(new
		{
			coordinateOrigin = "top-left",
			background = "SEL_GENDER_g_btn_base.png",
			male = new
			{
				width = 604,
				height = 614,
				backgroundX = 0,
				backgroundY = 10,
				portrait = Slots[2].Name + ".png",
				portraitX = 107,
				portraitY = 0
			},
			female = new
			{
				width = 604,
				height = 606,
				backgroundX = 0,
				backgroundY = 3,
				portrait = Slots[3].Name + ".png",
				portraitX = 111,
				portraitY = 0
			}
		}, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	public static string ImagePath(string root, string name)
	{
		return Path.Combine(root, "Master", "Sources", name + ".png");
	}

	public static void Import(string root, ModMasterSettings settings, MasterSprite slot, string file)
	{
		BitmapSource bitmapSource = TextureDocument.LoadPng(file);
		if (bitmapSource.PixelWidth != slot.Width || bitmapSource.PixelHeight != slot.Height)
		{
			throw new InvalidDataException($"The image must be {slot.Width}×{slot.Height}, the size of the game's.");
		}
		string text = ImagePath(root, slot.Name);
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		if (Path.GetFullPath(file) != Path.GetFullPath(text))
		{
			File.Copy(file, text, overwrite: true);
		}
		settings.Images[slot.Name] = "Sources/" + slot.Name + ".png";
	}

	public static void Validate(string root)
	{
		ModMasterSettings settings = Load(root);
		if (settings.Images.Count == 0)
		{
			throw new InvalidDataException("Import a Master image first.");
		}
		foreach (KeyValuePair<string, string> entry in settings.Images)
		{
			MasterSprite masterSprite = EditableSlots.SingleOrDefault((MasterSprite s) => s.Name == entry.Key) ?? throw new InvalidDataException("The Master portrait settings name an image the game does not have.");
			if (entry.Value != "Sources/" + masterSprite.Name + ".png")
			{
				throw new InvalidDataException("Invalid Master image path.");
			}
			BitmapSource bitmapSource = TextureDocument.LoadPng(ImagePath(root, masterSprite.Name));
			if (bitmapSource.PixelWidth != masterSprite.Width || bitmapSource.PixelHeight != masterSprite.Height)
			{
				throw new InvalidDataException("Invalid Master image dimensions.");
			}
		}
		if (!Regex.IsMatch(settings.Folder, "\\AMaster/Generated/build-[a-f0-9]{32}\\z") || !Directory.Exists(Path.Combine(root, settings.Folder)))
		{
			throw new InvalidDataException("Save Master portraits first.");
		}
		foreach (MasterSprite item in Slots.Where((MasterSprite s, int i) => HasRuntimeSlot(settings, i)))
		{
			string[] array = new string[2] { ".farc", "_table.bin" };
			foreach (string text in array)
			{
				if (!File.Exists(Path.Combine(root, settings.Folder, "rom", "sprite", IndependentStem(item) + text)))
				{
					throw new InvalidDataException("Save Master portraits first.");
				}
			}
		}
	}

	public static string IndependentStem(MasterSprite slot)
	{
		return "spr_mod_master_" + Array.IndexOf(Slots, slot);
	}

	public static string IndependentName(MasterSprite slot)
	{
		return "MOD_MASTER_" + Array.IndexOf(Slots, slot) + "_image";
	}

	public static void Build(string gameRoot, string root, ModMasterSettings settings)
	{
		string text = "Master/Generated/build-" + Guid.NewGuid().ToString("N");
		string text2 = Path.Combine(root, text);
		Directory.CreateDirectory(text2);
		try
		{
			BuildIndependent(gameRoot, root, text2, settings);
			foreach (MasterSprite item in Slots.Where((MasterSprite s) => s.Bundle == "spr_sel_aimeinfo" && settings.Images.ContainsKey(s.Name)))
			{
				TextureDocument.SavePng(TextureDocument.LoadPng(ImagePath(root, item.Name)), Path.Combine(text2, "Icons", item.Name.Contains("female") ? "female.png" : "male.png"));
			}
			string folder = settings.Folder;
			settings.Folder = text;
			try
			{
				Save(root, settings);
			}
			catch
			{
				settings.Folder = folder;
				throw;
			}
		}
		catch
		{
			Directory.Delete(text2, recursive: true);
			throw;
		}
	}

	public static void Save(string root, ModMasterSettings settings)
	{
		if (settings.Images.Count == 0)
		{
			settings.Enabled = false;
			settings.Folder = "";
		}
		string text = "[master]\r\nenabled=" + (settings.Enabled ? 1 : 0) + "\r\nformat=2\r\nfolder=" + settings.Folder + "\r\n";
		string[] array = new string[2] { "male", "female" };
		foreach (string text2 in array)
		{
			text = text + "player_" + text2 + "=" + (settings.Images.ContainsKey("SEL_AIMEINFO_icon_player_" + text2 + "_001") ? 1 : 0) + "\r\n";
		}
		for (int j = 0; j < Slots.Length; j++)
		{
			text = text + "slot" + j + "=" + (HasRuntimeSlot(settings, j) ? 1 : 0) + "\r\n";
		}
		ModStorage.AtomicWrite(Path.Combine(root, "Master", "settings.json"), JsonSerializer.Serialize(settings, ModStorage.Json));
		ModStorage.AtomicWrite(Path.Combine(root, "Master", "runtime.ini"), text);
		if (File.Exists(Path.Combine(root, "packages.json")))
		{
			ModLibrary.Compose(root);
		}
	}

	private static BitmapSource GenderBackground(string gameRoot)
	{
		TextureDocument textureDocument = new TextureDocument(Path.Combine(gameRoot, "rom", "sprite", "spr_sel_gender.farc"));
		BitmapSource bitmapSource = textureDocument.Crop(textureDocument.Sprites.Single((Sprite s) => s.Name.Substring(6) == "SEL_GENDER_g_btn_base"), chinese: false);
		if (bitmapSource.PixelWidth != 604 || bitmapSource.PixelHeight != 604)
		{
			throw new InvalidDataException("The Master portrait settings name an image the game does not have.");
		}
		return bitmapSource;
	}

	private static BitmapSource GenderBase(string gameRoot, MasterSprite slot, BitmapSource image)
	{
		return ComposeGenderBase(slot, image, GenderBackground(gameRoot));
	}

	private static BitmapSource ComposeGenderBase(MasterSprite slot, BitmapSource image, BitmapSource frame)
	{
		bool num = slot == Slots[2];
		int num2 = 604;
		int num3 = (num ? 614 : 606);
		int num4 = (num ? 107 : 111);
		int num5 = (num ? 10 : 3);
		byte[] array = new byte[num2 * num3 * 4];
		byte[] array2 = new byte[1459264];
		byte[] array3 = new byte[slot.Width * slot.Height * 4];
		frame.CopyPixels(array2, 2416, 0);
		image.CopyPixels(array3, slot.Width * 4, 0);
		for (int i = num5; i < num3 && i - num5 < 604; i++)
		{
			Buffer.BlockCopy(array2, (i - num5) * 604 * 4, array, i * num2 * 4, 2416);
		}
		for (int j = 0; j < slot.Height; j++)
		{
			for (int k = 0; k < slot.Width; k++)
			{
				int num6 = (j * slot.Width + k) * 4;
				int num7 = (j * num2 + k + num4) * 4;
				double num8 = (double)(int)array3[num6 + 3] / 255.0;
				double num9 = (double)(int)array[num7 + 3] / 255.0;
				double num10 = num8 + num9 * (1.0 - num8);
				for (int l = 0; l < 3; l++)
				{
					array[num7 + l] = (byte)Math.Round((num10 == 0.0) ? 0.0 : (((double)(int)array3[num6 + l] * num8 + (double)(int)array[num7 + l] * num9 * (1.0 - num8)) / num10));
				}
				array[num7 + 3] = (byte)Math.Round(num10 * 255.0);
			}
		}
		return TextureDocument.Bitmap(array, num2, num3);
	}

	private static void BuildIndependent(string gameRoot, string root, string output, ModMasterSettings settings)
	{
		if (settings.Images.Count == 0)
		{
			throw new InvalidDataException("Import a Master image first.");
		}
		string text = Path.Combine(output, "rom", "sprite");
		Directory.CreateDirectory(text);
		foreach (KeyValuePair<string, string> pair in settings.Images)
		{
			MasterSprite masterSprite = EditableSlots.SingleOrDefault((MasterSprite s) => s.Name == pair.Key) ?? throw new InvalidDataException("The Master portrait settings name an image the game does not have.");
			if (pair.Value != "Sources/" + masterSprite.Name + ".png")
			{
				throw new InvalidDataException("Invalid Master image path.");
			}
			BitmapSource bitmapSource = TextureDocument.LoadPng(ImagePath(root, masterSprite.Name));
			if (bitmapSource.PixelWidth != masterSprite.Width || bitmapSource.PixelHeight != masterSprite.Height)
			{
				throw new InvalidDataException("Invalid Master image dimensions.");
			}
		}
		for (int num = 0; num < Slots.Length; num++)
		{
			if (!HasRuntimeSlot(settings, num))
			{
				continue;
			}
			MasterSprite masterSprite2 = Slots[num];
			string text2 = IndependentStem(masterSprite2);
			List<(string, BitmapSource)> list = new List<(string, BitmapSource)> { (IndependentName(masterSprite2), Preview(gameRoot, root, settings, masterSprite2)) };
			if ((uint)(num - 2) <= 1u)
			{
				list.Add(($"MOD_MASTER_{num}_gender_base", Preview(gameRoot, root, settings, GenderExtras[num + 1])));
				if (settings.Images.ContainsKey(GenderExtras[num - 2].Name))
				{
					list.Add(($"MOD_MASTER_{num}_confirmation", Preview(gameRoot, root, settings, GenderExtras[num - 2])));
				}
				if (num == 2 && settings.Images.ContainsKey(GenderExtras[2].Name))
				{
					list.Add(("MOD_MASTER_2_background", Preview(gameRoot, root, settings, GenderExtras[2])));
				}
			}
			int num2;
			for (num2 = 1; num2 < ((list.Count == 1) ? masterSprite2.Width : 1024); num2 *= 2)
			{
			}
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			List<(string, BitmapSource, int, int)> list2 = new List<(string, BitmapSource, int, int)>();
			foreach (var item in list)
			{
				if (num3 + item.Item2.PixelWidth > num2)
				{
					num3 = 0;
					num4 = (num4 + num5 + 7) & -4;
					num5 = 0;
				}
				list2.Add((item.Item1, item.Item2, num3, num4));
				num3 = (num3 + item.Item2.PixelWidth + 7) & -4;
				num5 = Math.Max(num5, item.Item2.PixelHeight);
			}
			int num6;
			for (num6 = 1; num6 < num4 + num5; num6 *= 2)
			{
			}
			byte[] array = new byte[num2 * num6 * 4];
			List<UiSpriteTableEntry> list3 = new List<UiSpriteTableEntry>();
			foreach (var item2 in list2)
			{
				int pixelWidth = item2.Item2.PixelWidth;
				int pixelHeight = item2.Item2.PixelHeight;
				byte[] array2 = new byte[pixelWidth * pixelHeight * 4];
				item2.Item2.CopyPixels(array2, pixelWidth * 4, 0);
				for (int num7 = 0; num7 < pixelHeight; num7++)
				{
					Buffer.BlockCopy(array2, num7 * pixelWidth * 4, array, ((num6 - 1 - item2.Item4 - num7) * num2 + item2.Item3) * 4, pixelWidth * 4);
				}
				list3.Add(new UiSpriteTableEntry(list3.Count, text2.Substring(4) + "_nomerge_bc7_000", item2.Item1, num2, num6, item2.Item3, item2.Item4, item2.Item3 + pixelWidth, item2.Item4 + pixelHeight));
			}
			byte[] array3 = TextureDocument.Encode(array, num2, num6, 130);
			using MemoryStream memoryStream = new MemoryStream();
			using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
			binaryWriter.Write(55597140);
			binaryWriter.Write(1);
			binaryWriter.Write(16843009);
			binaryWriter.Write(16);
			binaryWriter.Write(72374356);
			binaryWriter.Write(1);
			binaryWriter.Write(16843009);
			binaryWriter.Write(16);
			binaryWriter.Write(38819924);
			binaryWriter.Write(num2);
			binaryWriter.Write(num6);
			binaryWriter.Write(130);
			binaryWriter.Write(0);
			binaryWriter.Write(array3.Length);
			binaryWriter.Write(array3);
			File.WriteAllBytes(Path.Combine(text, text2 + ".farc"), ModDialogueCompiler.SingleArchive("texture.bin", memoryStream.ToArray()));
			File.WriteAllBytes(Path.Combine(text, text2 + "_table.bin"), Table(list3));
		}
	}

	private static byte[] Table(IEnumerable<UiSpriteTableEntry> entries)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		UiSpriteTableEntry[] array = entries.ToArray();
		binaryWriter.Write(336134422);
		binaryWriter.Write(array.Length);
		UiSpriteTableEntry[] array2 = array;
		foreach (UiSpriteTableEntry uiSpriteTableEntry in array2)
		{
			binaryWriter.Write(uiSpriteTableEntry.Index);
			string[] array3 = new string[2] { uiSpriteTableEntry.TextureName, uiSpriteTableEntry.SpriteName };
			foreach (string s in array3)
			{
				byte[] bytes = Encoding.UTF8.GetBytes(s);
				binaryWriter.Write(bytes.Length);
				binaryWriter.Write(bytes);
			}
			int[] array4 = new int[6] { uiSpriteTableEntry.TextureWidth, uiSpriteTableEntry.TextureHeight, uiSpriteTableEntry.RectangleValue0, uiSpriteTableEntry.RectangleValue1, uiSpriteTableEntry.RectangleValue2, uiSpriteTableEntry.RectangleValue3 };
			foreach (int value in array4)
			{
				binaryWriter.Write(value);
			}
		}
		return memoryStream.ToArray();
	}

	public static void Export(string gameRoot, string root, ModMasterSettings settings, string destination)
	{
		string text = Path.Combine(Path.GetTempPath(), "fgo-master-" + Guid.NewGuid().ToString("N"));
		string text2 = destination + ".tmp-" + Guid.NewGuid().ToString("N");
		try
		{
			string root2 = Path.Combine(text, "App", "1_MOD");
			ModMasterSettings snapshot = JsonSerializer.Deserialize<ModMasterSettings>(JsonSerializer.Serialize(settings, ModStorage.Json));
			foreach (MasterSprite item in EditableSlots.Where((MasterSprite s) => snapshot.Images.ContainsKey(s.Name)))
			{
				string text3 = ImagePath(root2, item.Name);
				Directory.CreateDirectory(Path.GetDirectoryName(text3));
				File.Copy(ImagePath(root, item.Name), text3);
			}
			snapshot.Folder = "";
			Save(root2, snapshot);
			ModStorage.AtomicWrite(Path.Combine(text, "mod-package.json"), JsonSerializer.Serialize(new
			{
				schemaVersion = 3,
				component = "master",
				buildId = Guid.NewGuid().ToString("N"),
				createdUtc = DateTimeOffset.UtcNow,
				gameVersion = "11.51",
				launcherVersion = "1.10",
				Title = snapshot.Title,
				Author = snapshot.Author,
				Version = snapshot.Version,
				Description = snapshot.Description
			}, ModStorage.Json));
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
			ZipFile.CreateFromDirectory(text, text2, CompressionLevel.Optimal, includeBaseDirectory: false);
			File.Move(text2, destination, overwrite: true);
		}
		finally
		{
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
			if (Directory.Exists(text))
			{
				Directory.Delete(text, recursive: true);
			}
		}
	}
}
