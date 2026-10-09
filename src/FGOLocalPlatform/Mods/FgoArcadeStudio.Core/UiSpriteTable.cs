using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FgoArcadeStudio.Core;

public sealed class UiSpriteTable
{
	private const uint SupportedSignature = 336134422u;

	private static readonly (string Marker, int FormatId)[] TextureFormats = new(string, int)[8]
	{
		("_bc1_srgb_", 105),
		("_bc1_", 104),
		("_bc3_srgb_", 110),
		("_bc3_", 109),
		("_bc4_", 112),
		("_bc5_", 115),
		("_bc7_srgb_", 131),
		("_bc7_", 130)
	};

	public IReadOnlyList<UiSpriteTableEntry> Entries { get; }

	public IReadOnlyList<string> TextureNames { get; }

	public UiSpriteTable(IReadOnlyList<UiSpriteTableEntry> entries)
	{
		Entries = entries;
		TextureNames = (from name in entries.Select((UiSpriteTableEntry entry) => entry.TextureName).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			orderby name.Contains("_nomerge_", StringComparison.OrdinalIgnoreCase) ? 1 : 0, ParseTextureFormatRank(name), ParseTrailingIndex(name)
			select name).ThenBy<string, string>((string name) => name, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public void ValidateTextureAssignments(IReadOnlyList<FgoTexture> textures)
	{
		if (textures.Count != TextureNames.Count)
		{
			throw new FgoFormatException($"Sprite table 声明 {TextureNames.Count:N0} 张纹理，但 TXP3 包含 {textures.Count:N0} 张。");
		}
		Dictionary<string, (int, int)> dictionary = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
		foreach (UiSpriteTableEntry entry in Entries)
		{
			(int, int) tuple = (entry.TextureWidth, entry.TextureHeight);
			if (dictionary.TryGetValue(entry.TextureName, out var value))
			{
				(int, int) tuple2 = value;
				(int, int) tuple3 = tuple;
				if (tuple2.Item1 != tuple3.Item1 || tuple2.Item2 != tuple3.Item2)
				{
					throw new FgoFormatException($"Sprite table 对纹理 {entry.TextureName} 声明了冲突尺寸：{value.Item1}x{value.Item2} / {tuple.Item1}x{tuple.Item2}。");
				}
			}
			dictionary[entry.TextureName] = tuple;
		}
		for (int i = 0; i < TextureNames.Count; i++)
		{
			string text = TextureNames[i];
			FgoTexture fgoTexture = textures[i];
			if (!fgoTexture.Name.Equals(text, StringComparison.OrdinalIgnoreCase))
			{
				throw new FgoFormatException($"TXP3 第 {i:N0} 页名称映射错误：{fgoTexture.Name} / {text}。");
			}
			(int, int) tuple4 = dictionary[text];
			if (fgoTexture.Width != tuple4.Item1 || fgoTexture.Height != tuple4.Item2)
			{
				throw new FgoFormatException($"纹理 {text} 尺寸映射错误：TXP3 为 {fgoTexture.Width}x{fgoTexture.Height}，Sprite table 为 {tuple4.Item1}x{tuple4.Item2}。");
			}
			if (TryGetExpectedTextureFormatId(text, out var formatId) && fgoTexture.FormatId != formatId)
			{
				throw new FgoFormatException($"纹理 {text} 格式映射错误：TXP3 为 {fgoTexture.FormatId}，名称声明为 {formatId}。");
			}
		}
	}

	public static UiSpriteTable? TryLoadForArchive(string archivePath)
	{
		string path = Path.Combine(Path.GetDirectoryName(archivePath) ?? string.Empty, Path.GetFileNameWithoutExtension(archivePath) + "_table.bin");
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			return Parse(File.ReadAllBytes(path));
		}
		catch (Exception ex) when (((ex is IOException || ex is FgoFormatException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	public static UiSpriteTable Parse(byte[] data)
	{
		BinarySpanReader binarySpanReader = new BinarySpanReader(data);
		uint num = binarySpanReader.ReadUInt32();
		if (num != 336134422)
		{
			throw new FgoFormatException($"Sprite table 魔数无效：0x{num:X8}");
		}
		int num2 = binarySpanReader.ReadInt32();
		if ((num2 < 0 || num2 > 100000) ? true : false)
		{
			throw new FgoFormatException($"Sprite table 条目数量异常：{num2}");
		}
		List<UiSpriteTableEntry> list = new List<UiSpriteTableEntry>(num2);
		for (int i = 0; i < num2; i++)
		{
			int index = binarySpanReader.ReadInt32();
			string textureName = ReadString(binarySpanReader, "纹理名");
			string spriteName = ReadString(binarySpanReader, "Sprite 名");
			list.Add(new UiSpriteTableEntry(index, textureName, spriteName, binarySpanReader.ReadInt32(), binarySpanReader.ReadInt32(), binarySpanReader.ReadInt32(), binarySpanReader.ReadInt32(), binarySpanReader.ReadInt32(), binarySpanReader.ReadInt32()));
		}
		if (binarySpanReader.Position != binarySpanReader.Length)
		{
			throw new FgoFormatException($"Sprite table 尾部尺寸不匹配：0x{binarySpanReader.Position:X}/0x{binarySpanReader.Length:X}");
		}
		return new UiSpriteTable(list);
	}

	private static string ReadString(BinarySpanReader reader, string label)
	{
		int num = reader.ReadInt32();
		if ((num < 0 || num > 1048576) ? true : false)
		{
			throw new FgoFormatException($"Sprite table {label}长度异常：{num}");
		}
		return reader.ReadSizedString(num, Encoding.UTF8);
	}

	private static int ParseTrailingIndex(string value)
	{
		int num = value.LastIndexOf('_');
		if (num < 0 || !int.TryParse(value.AsSpan(num + 1), out var result))
		{
			return int.MaxValue;
		}
		return result;
	}

	private static int ParseTextureFormatRank(string value)
	{
		if (!TryGetExpectedTextureFormatId(value, out var formatId))
		{
			return int.MaxValue;
		}
		return formatId;
	}

	private static bool TryGetExpectedTextureFormatId(string value, out int formatId)
	{
		(string, int)[] textureFormats = TextureFormats;
		for (int i = 0; i < textureFormats.Length; i++)
		{
			var (value2, num) = textureFormats[i];
			if (value.Contains(value2, StringComparison.OrdinalIgnoreCase))
			{
				formatId = num;
				return true;
			}
		}
		formatId = -1;
		return false;
	}
}
