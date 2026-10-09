using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using FgoArcadeStudio.Core;

namespace Hanhua;

public sealed class TextureDocument
{
	private readonly Dictionary<int, byte[]> edited = new Dictionary<int, byte[]>();

	private byte[] storedTxp;

	private IReadOnlyList<FgoTexture> storedPages;

	public string Source { get; }

	public Archive? Archive { get; }

	public string Entry { get; }

	public byte[] Txp { get; }

	public IReadOnlyList<FgoTexture> Pages { get; }

	public List<Sprite> Sprites { get; } = new List<Sprite>();

	public bool Dirty => edited.Count > 0;

	public TextureDocument(string path, string? entry = null, string? overlay = null)
	{
		TextureDocument textureDocument = this;
		Source = Path.GetFullPath(path);
		byte[] data = File.ReadAllBytes(path);
		if (Path.GetExtension(path).Equals(".farc", StringComparison.OrdinalIgnoreCase))
		{
			Archive = new Archive(data);
			ArchiveEntry archiveEntry = ((entry == null) ? Archive.Entries.FirstOrDefault((ArchiveEntry e) => e.Name == "texture.bin" || e.Name.EndsWith("_tex.bin")) : Archive.Entries.FirstOrDefault((ArchiveEntry e) => e.Name == entry));
			if (archiveEntry == null)
			{
				throw new InvalidDataException("The game archive holds no texture.");
			}
			Entry = archiveEntry.Name;
			Txp = Archive.Read(archiveEntry);
		}
		else
		{
			Entry = "";
			Txp = data;
		}
		string path2 = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_table.bin");
		UiSpriteTable uiSpriteTable = (File.Exists(path2) ? UiSpriteTable.Parse(File.ReadAllBytes(path2)) : null);
		TextureSet textureSet = Decode(Txp, uiSpriteTable);
		Pages = textureSet.OrderedTextures;
		storedTxp = Txp;
		storedPages = Pages;
		if (uiSpriteTable != null)
		{
			uiSpriteTable.ValidateTextureAssignments(Pages);
			foreach (UiSpriteTableEntry entry2 in uiSpriteTable.Entries)
			{
				int num = Array.IndexOf(uiSpriteTable.TextureNames.ToArray(), entry2.TextureName);
				int num2 = Math.Min(entry2.RectangleValue0, entry2.RectangleValue2);
				int rectangleValue = entry2.RectangleValue1;
				int num3 = Math.Abs(entry2.RectangleValue2 - entry2.RectangleValue0);
				int num4 = entry2.RectangleValue3 - entry2.RectangleValue1;
				if (num < 0 || num2 < 0 || rectangleValue < 0 || num3 <= 0 || num4 <= 0 || num2 + num3 > Pages[num].Width || rectangleValue + num4 > Pages[num].Height)
				{
					throw new InvalidDataException("Sprite 矩形无效：" + entry2.SpriteName);
				}
				Sprites.Add(new Sprite(num, $"{entry2.Index:D5} {entry2.SpriteName}", num2, rectangleValue, num3, num4, entry2.RectangleValue2 < entry2.RectangleValue0));
			}
		}
		else
		{
			for (int num5 = 0; num5 < Pages.Count; num5++)
			{
				Sprites.Add(new Sprite(num5, Pages[num5].Name, 0, 0, Pages[num5].Width, Pages[num5].Height, Rotated: false));
			}
		}
		if (overlay == null || !File.Exists(overlay))
		{
			return;
		}
		byte[] array = File.ReadAllBytes(overlay);
		if (Archive != null)
		{
			Archive archive = new Archive(array);
			array = archive.Read(archive.Entries.Single((ArchiveEntry e) => e.Name == textureDocument.Entry));
		}
		IReadOnlyList<FgoTexture> orderedTextures = Decode(array, uiSpriteTable).OrderedTextures;
		if (array.Length != Txp.Length)
		{
			throw new InvalidDataException("中文 TXP 结构长度与原文不一致");
		}
		if (orderedTextures.Count != Pages.Count)
		{
			throw new InvalidDataException("中文纹理页数与原文不一致");
		}
		for (int num6 = 0; num6 < Pages.Count; num6++)
		{
			if (orderedTextures[num6].Width != Pages[num6].Width || orderedTextures[num6].Height != Pages[num6].Height || orderedTextures[num6].FormatId != Pages[num6].FormatId)
			{
				throw new InvalidDataException("中文纹理格式与原文不一致");
			}
			if (!orderedTextures[num6].BgraPixels.SequenceEqual(Pages[num6].BgraPixels))
			{
				edited[num6] = (byte[])orderedTextures[num6].BgraPixels.Clone();
			}
		}
		foreach (int item in PageOffsets())
		{
			List<int> list = new List<int>();
			Records(item, list);
			foreach (int item2 in list)
			{
				if (!array.AsSpan(item2, 24).SequenceEqual(Txp.AsSpan(item2, 24)))
				{
					throw new InvalidDataException("中文 TXP 图像结构与原文不一致");
				}
			}
		}
		storedTxp = array;
		storedPages = orderedTextures;
	}

	private TextureSet Decode(byte[] b, UiSpriteTable? t)
	{
		if (b.Length < 4 || b[0] != 84 || b[1] != 88 || b[2] != 80)
		{
			return FgoTextureDecoder.Parse(b);
		}
		if (b[3] != 3)
		{
			return Single(b);
		}
		return FgoTextureDecoder.ParseTxpTextureSet(b, Path.GetFileNameWithoutExtension(Source), t?.TextureNames);
	}

	private TextureSet Single(byte[] b)
	{
		TextureSet textureSet = new TextureSet();
		textureSet.SetOrderedTextures(new List<FgoTexture>(1) { FgoTextureDecoder.ParseSingleTxp(b, Path.GetFileName(Source)) });
		return textureSet;
	}

	public static BitmapSource Bitmap(byte[] p, int w, int h)
	{
		BitmapSource bitmapSource = BitmapSource.Create(w, h, 96.0, 96.0, PixelFormats.Bgra32, null, p, w * 4);
		bitmapSource.Freeze();
		return bitmapSource;
	}

	public BitmapSource Crop(Sprite s, bool chinese)
	{
		FgoTexture fgoTexture = Pages[s.Page];
		byte[] value;
		BitmapSource bitmapSource = new CroppedBitmap(new TransformedBitmap(Bitmap((chinese && edited.TryGetValue(s.Page, out value)) ? value : fgoTexture.BgraPixels, fgoTexture.Width, fgoTexture.Height), new ScaleTransform(1.0, -1.0)), new Int32Rect(s.X, s.Y, s.W, s.H));
		if (s.Rotated)
		{
			bitmapSource = new TransformedBitmap(bitmapSource, new RotateTransform(-90.0));
		}
		bitmapSource.Freeze();
		return bitmapSource;
	}

	public static void SavePng(BitmapSource b, string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(b));
		using FileStream stream = File.Create(path);
		pngBitmapEncoder.Save(stream);
	}

	public static BitmapSource LoadPng(string path)
	{
		using FileStream bitmapStream = File.OpenRead(path);
		FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(new PngBitmapDecoder(bitmapStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0.0);
		formatConvertedBitmap.Freeze();
		return formatConvertedBitmap;
	}

	public void Import(Sprite s, BitmapSource image)
	{
		if (image.PixelWidth != s.Width || image.PixelHeight != s.Height)
		{
			throw new InvalidDataException($"The image must be exactly {s.Width}×{s.Height}; it is {image.PixelWidth}×{image.PixelHeight}. Nothing was scaled or written.");
		}
		if (s.Rotated)
		{
			image = new TransformedBitmap(image, new RotateTransform(90.0));
		}
		image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0.0);
		byte[] array = new byte[s.W * s.H * 4];
		image.CopyPixels(array, s.W * 4, 0);
		if (!edited.TryGetValue(s.Page, out byte[] value))
		{
			value = (edited[s.Page] = (byte[])Pages[s.Page].BgraPixels.Clone());
		}
		for (int i = 0; i < s.H; i++)
		{
			Buffer.BlockCopy(array, i * s.W * 4, value, ((Pages[s.Page].Height - 1 - s.Y - i) * Pages[s.Page].Width + s.X) * 4, s.W * 4);
		}
	}

	public void Reset(Sprite s)
	{
		Import(s, Crop(s, chinese: false));
	}

	public BitmapSource EditedPage(int page)
	{
		byte[] value;
		return Bitmap(edited.TryGetValue(page, out value) ? value : Pages[page].BgraPixels, Pages[page].Width, Pages[page].Height);
	}

	public void ImportPage(int page, BitmapSource image)
	{
		if (image.PixelWidth != Pages[page].Width || image.PixelHeight != Pages[page].Height)
		{
			throw new InvalidDataException("草稿页尺寸与原图不符");
		}
		image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0.0);
		byte[] array = new byte[image.PixelWidth * image.PixelHeight * 4];
		image.CopyPixels(array, image.PixelWidth * 4, 0);
		edited[page] = array;
	}

	private List<int> PageOffsets()
	{
		if (Txp[0] == 84 && Txp[1] == 88 && Txp[2] == 80)
		{
			if (Txp[3] != 3)
			{
				return new List<int>(1) { 0 };
			}
			return (from i in Enumerable.Range(0, Hanhua.Archive.LE(Txp, 4))
				select Hanhua.Archive.LE(Txp, 12 + i * 4)).ToList();
		}
		int num = Txp[0] | (Txp[1] << 8);
		int num2 = 2;
		List<int> list = new List<int>();
		for (int num3 = 0; num3 < num; num3++)
		{
			int num4 = Txp[num2++];
			num2 += num4;
			list.Add(Hanhua.Archive.LE(Txp, num2));
			num2 += 4;
		}
		return list;
	}

	private void Records(int o, List<int> result, int depth = 0)
	{
		if (depth > 8 || Txp[o] != 84 || Txp[o + 1] != 88 || Txp[o + 2] != 80)
		{
			throw new InvalidDataException("TXP 结构无效");
		}
		if (Txp[o + 3] == 2)
		{
			result.Add(o);
			return;
		}
		if (Txp[o + 9] > 1)
		{
			throw new NotSupportedException("此编辑器不回写立方体纹理");
		}
		int num = Hanhua.Archive.LE(Txp, o + 4);
		for (int i = 0; i < num; i++)
		{
			Records(o + Hanhua.Archive.LE(Txp, o + 12 + i * 4), result, depth + 1);
		}
	}

	public byte[] BuildTxp()
	{
		byte[] array = (byte[])storedTxp.Clone();
		List<int> list = PageOffsets();
		foreach (KeyValuePair<int, byte[]> item in edited)
		{
			item.Deconstruct(out var key, out var value);
			int index = key;
			byte[] p = value;
			List<int> list2 = new List<int>();
			Records(list[index], list2);
			foreach (int item2 in list2)
			{
				int num = Hanhua.Archive.LE(Txp, item2 + 4);
				int num2 = Hanhua.Archive.LE(Txp, item2 + 8);
				int num3 = Hanhua.Archive.LE(Txp, item2 + 12);
				int num4 = Hanhua.Archive.LE(Txp, item2 + 20);
				byte[] array2 = Resize(p, Pages[index].Width, Pages[index].Height, num, num2);
				byte[] array3 = Resize(storedPages[index].BgraPixels, Pages[index].Width, Pages[index].Height, num, num2);
				byte[] array4 = Resize(Pages[index].BgraPixels, Pages[index].Width, Pages[index].Height, num, num2);
				if (array2.SequenceEqual(array3))
				{
					continue;
				}
				byte[] array5 = Encode(array2, num, num2, num3);
				if (array5.Length != num4)
				{
					throw new InvalidDataException("编码尺寸与原 TXP 不符，未保存");
				}
				bool flag = num3 >= 104;
				if (flag)
				{
					bool flag2 = (((uint)(num3 - 104) <= 1u || num3 == 112) ? true : false);
					key = (flag2 ? 8 : 16);
				}
				else
				{
					key = num3 switch
					{
						100 => 4, 
						99 => 3, 
						98 => 1, 
						_ => 2, 
					};
				}
				int num5 = key;
				int num6 = ((!flag) ? 1 : 4);
				int num7 = (num + num6 - 1) / num6;
				for (int i = 0; i < num2; i += num6)
				{
					for (int j = 0; j < num; j += num6)
					{
						bool flag3 = false;
						bool flag4 = true;
						for (int k = i; k < Math.Min(i + num6, num2); k++)
						{
							for (int l = j; l < Math.Min(j + num6, num); l++)
							{
								flag3 |= !array2.AsSpan((k * num + l) * 4, 4).SequenceEqual(array3.AsSpan((k * num + l) * 4, 4));
								flag4 &= array2.AsSpan((k * num + l) * 4, 4).SequenceEqual(array4.AsSpan((k * num + l) * 4, 4));
							}
						}
						if (flag3)
						{
							int num8 = (i / num6 * num7 + j / num6) * num5;
							if (flag4)
							{
								Txp.AsSpan(item2 + 24 + num8, num5).CopyTo(array.AsSpan(item2 + 24 + num8, num5));
							}
							else
							{
								array5.AsSpan(num8, num5).CopyTo(array.AsSpan(item2 + 24 + num8, num5));
							}
						}
					}
				}
			}
		}
		return array;
	}

	public byte[] Build()
	{
		if (Archive != null)
		{
			return Archive.Repack(new Dictionary<string, byte[]> { 
			{
				Entry,
				BuildTxp()
			} });
		}
		return BuildTxp();
	}

	private static byte[] Resize(byte[] p, int w, int h, int nw, int nh)
	{
		if (w == nw && h == nh)
		{
			return p;
		}
		byte[] array = new byte[nw * nh * 4];
		for (int i = 0; i < nh; i++)
		{
			for (int j = 0; j < nw; j++)
			{
				int num = j * w / nw;
				int num2 = Math.Max(num + 1, (j + 1) * w / nw);
				int num3 = i * h / nh;
				int num4 = Math.Max(num3 + 1, (i + 1) * h / nh);
				long num5 = 0L;
				long num6 = 0L;
				long num7 = 0L;
				long num8 = 0L;
				int num9 = (num2 - num) * (num4 - num3);
				for (int k = num3; k < num4; k++)
				{
					for (int l = num; l < num2; l++)
					{
						int num10 = (k * w + l) * 4;
						int num11 = p[num10 + 3];
						num5 += num11;
						num6 += p[num10] * num11;
						num7 += p[num10 + 1] * num11;
						num8 += p[num10 + 2] * num11;
					}
				}
				int num12 = (i * nw + j) * 4;
				array[num12] = (byte)((num5 == 0L) ? 0 : (num6 / num5));
				array[num12 + 1] = (byte)((num5 == 0L) ? 0 : (num7 / num5));
				array[num12 + 2] = (byte)((num5 == 0L) ? 0 : (num8 / num5));
				array[num12 + 3] = (byte)(num5 / num9);
			}
		}
		return array;
	}

	public static byte[] Encode(byte[] p, int w, int h, int fmt)
	{
		if (fmt >= 104)
		{
			CompressionFormat format;
			switch (fmt)
			{
			case 104:
			case 105:
				format = CompressionFormat.Bc1;
				break;
			case 107:
				format = CompressionFormat.Bc2;
				break;
			case 109:
			case 110:
				format = CompressionFormat.Bc3;
				break;
			case 112:
				format = CompressionFormat.Bc4;
				break;
			case 115:
				format = CompressionFormat.Bc5;
				break;
			case 130:
			case 131:
				format = CompressionFormat.Bc7;
				break;
			default:
				throw new NotSupportedException($"格式 {fmt}");
			}
			BcEncoder bcEncoder = new BcEncoder(format);
			bcEncoder.OutputOptions.GenerateMipMaps = false;
			bcEncoder.OutputOptions.Quality = CompressionQuality.Balanced;
			return bcEncoder.EncodeToRawBytes(p, w, h, BCnEncoder.Encoder.PixelFormat.Bgra32)[0];
		}
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		for (int i = 0; i < p.Length; i += 4)
		{
			int num = p[i];
			int num2 = p[i + 1];
			int num3 = p[i + 2];
			int num4 = p[i + 3];
			switch (fmt)
			{
			case 98:
				binaryWriter.Write((byte)num4);
				break;
			case 99:
				binaryWriter.Write(new byte[3]
				{
					(byte)num3,
					(byte)num2,
					(byte)num
				});
				break;
			case 100:
				binaryWriter.Write(new byte[4]
				{
					(byte)num3,
					(byte)num2,
					(byte)num,
					(byte)num4
				});
				break;
			case 101:
			case 102:
				binaryWriter.Write((ushort)((num3 * 31 / 255) | (num2 * 31 / 255 << 5) | (num * 31 / 255 << 10) | ((fmt == 102 && num4 >= 128) ? 32768 : 0)));
				break;
			case 103:
				binaryWriter.Write((ushort)((num3 / 17) | (num2 / 17 << 4) | (num / 17 << 8) | (num4 / 17 << 12)));
				break;
			default:
				throw new NotSupportedException($"格式 {fmt}");
			}
		}
		return memoryStream.ToArray();
	}
}
