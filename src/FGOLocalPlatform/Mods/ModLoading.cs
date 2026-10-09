using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FgoArcadeStudio.Core;
using Hanhua;

namespace FGOLocalPlatform;

public static class ModLoading
{
	public static ModLoadingSettings Load(string root)
	{
		string path = Path.Combine(root, "Loading/settings.json");
		if (!File.Exists(path))
		{
			return new ModLoadingSettings();
		}
		return JsonSerializer.Deserialize<ModLoadingSettings>(File.ReadAllText(path), ModStorage.Json) ?? throw new InvalidDataException("Invalid loading image settings.");
	}

	public static string Source(string root, int quest)
	{
		if (quest < 0)
		{
			throw new InvalidDataException("Invalid quest ID.");
		}
		return Path.Combine(root, "Loading/Sources", $"quest-{quest}.png");
	}

	public static void Import(string root, int quest, string file)
	{
		using FileStream bitmapStream = File.OpenRead(file);
		BitmapFrame bitmapFrame = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
		if ((long)bitmapFrame.PixelWidth * (long)bitmapFrame.PixelHeight > 67108864)
		{
			throw new InvalidDataException("The image is too large. Use an image with no more than 64 million pixels.");
		}
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			drawingContext.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, 1920.0, 1080.0));
			double num = Math.Min(1920.0 / (double)bitmapFrame.PixelWidth, 1080.0 / (double)bitmapFrame.PixelHeight);
			double num2 = (double)bitmapFrame.PixelWidth * num;
			double num3 = (double)bitmapFrame.PixelHeight * num;
			drawingContext.DrawImage(bitmapFrame, new Rect((1920.0 - num2) / 2.0, (1080.0 - num3) / 2.0, num2, num3));
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(1920, 1080, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		string text = Source(root, quest);
		string text2 = text + ".tmp";
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		try
		{
			TextureDocument.SavePng(renderTargetBitmap, text2);
			File.Move(text2, text, overwrite: true);
		}
		finally
		{
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
		}
	}

	public static void Build(string gameRoot, string root, ModLoadingSettings settings)
	{
		if (settings.Entries.Select((ModLoadingEntry e) => e.Quest).Distinct().Count() != settings.Entries.Count || settings.Entries.Count > 900)
		{
			throw new InvalidDataException("Invalid loading image settings.");
		}
		string text = "Loading/Generated/build-" + Guid.NewGuid().ToString("N");
		string text2 = Path.Combine(root, text);
		Directory.CreateDirectory(text2);
		Archive archive = new Archive(File.ReadAllBytes(Path.Combine(gameRoot, "rom/aet/aet_qst_sing_007.farc")));
		byte[] array = archive.Read(archive.Entries.Single());
		UiSpriteTable uiSpriteTable = UiSpriteTable.Parse(File.ReadAllBytes(Path.Combine(gameRoot, "rom/sprite/spr_qst_sing_007_table.bin")));
		if (!uiSpriteTable.Entries.Any((UiSpriteTableEntry e) => e.SpriteName == "QST_SING_007_load_story_bg_01") || !Encoding.ASCII.GetString(array).Contains("load_story_01_loop"))
		{
			throw new InvalidDataException("The original loading screen resources do not match.");
		}
		int num = 100;
		foreach (ModLoadingEntry item in from e in settings.Entries
			where e.Enabled
			orderby e.Quest
			select e)
		{
			if (!File.Exists(Path.Combine(gameRoot, "rom/single", $"quest_prop_{item.Quest:000000}.txt")))
			{
				throw new InvalidDataException("The game files for this quest were not found.");
			}
			for (; num <= 999; num++)
			{
				if (!File.Exists(Path.Combine(gameRoot, "rom/aet", $"aet_qst_sing_{num:000}.farc")) && !File.Exists(Path.Combine(gameRoot, "rom/sprite", $"spr_qst_sing_{num:000}.farc")))
				{
					break;
				}
			}
			if (num > 999)
			{
				throw new InvalidDataException("No loading image resource IDs are available.");
			}
			item.Set = num++;
			string text3 = item.Set.ToString("000");
			string text4 = "qst_sing_" + text3;
			string text5 = Path.Combine(text2, "rom/sprite");
			string text6 = Path.Combine(text2, "rom/aet");
			Directory.CreateDirectory(text5);
			Directory.CreateDirectory(text6);
			byte[] array2 = (byte[])array.Clone();
			ReplaceAscii(array2, "qst_sing_007", "qst_sing_" + text3);
			ReplaceAscii(array2, "QST_SING_007", "QST_SING_" + text3);
			File.WriteAllBytes(Path.Combine(text6, "aet_" + text4 + ".farc"), ModDialogueCompiler.SingleArchive(text4 + ".bin", array2));
			BitmapSource bitmapSource = TextureDocument.LoadPng(Source(root, item.Quest));
			if (bitmapSource.PixelWidth != 1920 || bitmapSource.PixelHeight != 1080)
			{
				throw new InvalidDataException("Invalid loading image dimensions.");
			}
			byte[] array3 = new byte[16777216];
			byte[] array4 = new byte[8294400];
			bitmapSource.CopyPixels(array4, 7680, 0);
			for (int num2 = 0; num2 < 1080; num2++)
			{
				Buffer.BlockCopy(array4, num2 * 1920 * 4, array3, (2047 - num2) * 2048 * 4, 7680);
			}
			byte[] array5 = TextureDocument.Encode(array3, 2048, 2048, 130);
			using (MemoryStream memoryStream = new MemoryStream())
			{
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
				binaryWriter.Write(2048);
				binaryWriter.Write(2048);
				binaryWriter.Write(130);
				binaryWriter.Write(0);
				binaryWriter.Write(array5.Length);
				binaryWriter.Write(array5);
				File.WriteAllBytes(Path.Combine(text5, "spr_" + text4 + ".farc"), ModDialogueCompiler.SingleArchive("texture.bin", memoryStream.ToArray()));
			}
			using MemoryStream memoryStream2 = new MemoryStream();
			using BinaryWriter binaryWriter2 = new BinaryWriter(memoryStream2);
			binaryWriter2.Write(336134422);
			binaryWriter2.Write(uiSpriteTable.Entries.Count);
			foreach (UiSpriteTableEntry entry in uiSpriteTable.Entries)
			{
				bool flag = entry.SpriteName == "QST_SING_007_load_story_bg_01";
				binaryWriter2.Write(entry.Index);
				string[] array6 = new string[2]
				{
					"QST_SING_" + text3 + "_bc7_0",
					entry.SpriteName.Replace("QST_SING_007", "QST_SING_" + text3)
				};
				foreach (string s in array6)
				{
					byte[] bytes = Encoding.UTF8.GetBytes(s);
					binaryWriter2.Write(bytes.Length);
					binaryWriter2.Write(bytes);
				}
				binaryWriter2.Write(2048);
				binaryWriter2.Write(2048);
				binaryWriter2.Write((!flag) ? 2047 : 0);
				binaryWriter2.Write((!flag) ? 2047 : 0);
				binaryWriter2.Write(flag ? 1920 : 2048);
				binaryWriter2.Write(flag ? 1080 : 2048);
			}
			File.WriteAllBytes(Path.Combine(text5, "spr_" + text4 + "_table.bin"), memoryStream2.ToArray());
		}
		settings.Folder = text;
		ModLoadingEntry[] array7 = settings.Entries.Where((ModLoadingEntry e) => e.Enabled).ToArray();
		StringBuilder stringBuilder = new StringBuilder($"[loading]\r\ncount={array7.Length}\r\nfolder={text}\r\n");
		for (int num4 = 0; num4 < array7.Length; num4++)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(25, 3, stringBuilder2);
			handler.AppendLiteral("[loading");
			handler.AppendFormatted(num4);
			handler.AppendLiteral("]\r\nquest=");
			handler.AppendFormatted(array7[num4].Quest);
			handler.AppendLiteral("\r\nset=");
			handler.AppendFormatted(array7[num4].Set);
			handler.AppendLiteral("\r\n");
			stringBuilder2.Append(ref handler);
		}
		Write(Path.Combine(root, "Loading/settings.json"), JsonSerializer.Serialize(settings, ModStorage.Json));
		Write(Path.Combine(root, "Loading/runtime.ini"), stringBuilder.ToString());
	}

	private static void Write(string path, string content)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string text = path + ".tmp";
		File.WriteAllText(text, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.Move(text, path, overwrite: true);
	}

	private static void ReplaceAscii(byte[] data, string from, string to)
	{
		if (from.Length != to.Length)
		{
			throw new InvalidDataException();
		}
		byte[] bytes = Encoding.ASCII.GetBytes(from);
		byte[] bytes2 = Encoding.ASCII.GetBytes(to);
		for (int i = 0; i <= data.Length - bytes.Length; i++)
		{
			if (data.AsSpan(i, bytes.Length).SequenceEqual(bytes))
			{
				bytes2.CopyTo(data, i);
				i += bytes.Length - 1;
			}
		}
	}
}
