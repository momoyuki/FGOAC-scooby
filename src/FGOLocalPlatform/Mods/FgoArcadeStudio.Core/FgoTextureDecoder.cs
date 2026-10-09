using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace FgoArcadeStudio.Core;

public static class FgoTextureDecoder
{
	public static TextureSet ParseTxpTextureSet(byte[] data, string fallbackName, IReadOnlyList<string>? textureNames = null, bool decodeImages = true)
	{
		BinarySpanReader binarySpanReader = new BinarySpanReader(data);
		uint num = binarySpanReader.ReadUInt32();
		if ((num & 0xFFFFFF) != 5265492 || num >> 24 != 3)
		{
			throw new FgoFormatException($"{fallbackName} 不是 TXP3 纹理集合：0x{num:X8}");
		}
		int num2 = checked((int)binarySpanReader.ReadUInt32());
		binarySpanReader.ReadUInt32();
		if ((num2 < 0 || num2 > 50000) ? true : false)
		{
			throw new FgoFormatException($"{fallbackName} 的 TXP3 纹理数量异常：{num2}");
		}
		int[] array = new int[num2];
		for (int i = 0; i < num2; i++)
		{
			array[i] = checked((int)binarySpanReader.ReadUInt32());
			binarySpanReader.CheckRange(array[i], 16L, $"TXP3 纹理 {i}");
		}
		List<FgoTexture> list = new List<FgoTexture>(num2);
		for (int j = 0; j < num2; j++)
		{
			string name = ((textureNames != null && j < textureNames.Count && !string.IsNullOrWhiteSpace(textureNames[j])) ? textureNames[j] : $"{fallbackName}_{j:D3}");
			list.Add(ParseTxp(data, array[j], name, decodeImages));
		}
		TextureSet textureSet = new TextureSet();
		textureSet.SetOrderedTextures(list);
		return textureSet;
	}

	public static TextureSet Parse(byte[] data, bool decodeImages = true)
	{
		BinarySpanReader binarySpanReader = new BinarySpanReader(data);
		int num = binarySpanReader.ReadUInt16();
		if (num > 50000)
		{
			throw new FgoFormatException($"纹理数量异常：{num}");
		}
		List<(string, int)> list = new List<(string, int)>(num);
		for (int i = 0; i < num; i++)
		{
			int count = binarySpanReader.ReadByte();
			string text = binarySpanReader.ReadSizedString(count);
			int num2 = checked((int)binarySpanReader.ReadUInt32());
			binarySpanReader.CheckRange(num2, 12L, "纹理 " + text);
			list.Add((text, num2));
		}
		List<FgoTexture> list2 = new List<FgoTexture>(num);
		foreach (var (name, offset) in list)
		{
			try
			{
				list2.Add(ParseTxp(data, offset, name, decodeImages));
			}
			catch (Exception ex) when (((ex is FgoFormatException || ex is ArgumentException || ex is OverflowException) ? 1 : 0) != 0)
			{
				list2.Add(new FgoTexture
				{
					Name = name,
					Width = 0,
					Height = 0,
					FormatId = -1,
					BgraPixels = CreatePlaceholder()
				});
			}
		}
		TextureSet textureSet = new TextureSet();
		textureSet.SetOrderedTextures(list2);
		return textureSet;
	}

	public static FgoTexture ParseSingleTxp(byte[] data, string name, bool decodeImages = true, int offset = 0)
	{
		return ParseTxp(data, offset, name, decodeImages);
	}

	public static byte[] Decode(byte[] payload, int width, int height, int formatId)
	{
		int num;
		byte[] array;
		checked
		{
			num = width * height;
			array = new byte[num * 4];
		}
		CompressionFormat compressionFormat;
		switch (formatId)
		{
		case 98:
		{
			EnsurePayload(payload, num, formatId);
			for (int k = 0; k < num; k++)
			{
				int num5 = k * 4;
				array[num5] = byte.MaxValue;
				array[num5 + 1] = byte.MaxValue;
				array[num5 + 2] = byte.MaxValue;
				array[num5 + 3] = payload[k];
			}
			return array;
		}
		case 99:
		{
			EnsurePayload(payload, checked(num * 3), formatId);
			for (int j = 0; j < num; j++)
			{
				int num3 = j * 3;
				int num4 = j * 4;
				array[num4] = payload[num3 + 2];
				array[num4 + 1] = payload[num3 + 1];
				array[num4 + 2] = payload[num3];
				array[num4 + 3] = byte.MaxValue;
			}
			return array;
		}
		case 100:
		{
			EnsurePayload(payload, checked(num * 4), formatId);
			for (int i = 0; i < num; i++)
			{
				int num2 = i * 4;
				array[num2] = payload[num2 + 2];
				array[num2 + 1] = payload[num2 + 1];
				array[num2 + 2] = payload[num2];
				array[num2 + 3] = payload[num2 + 3];
			}
			return array;
		}
		case 101:
		case 102:
		case 103:
			return Decode16Bit(payload, width, height, formatId);
		case 104:
		case 105:
			compressionFormat = CompressionFormat.Bc1;
			break;
		case 107:
			compressionFormat = CompressionFormat.Bc2;
			break;
		case 109:
		case 110:
			compressionFormat = CompressionFormat.Bc3;
			break;
		case 112:
			compressionFormat = CompressionFormat.Bc4;
			break;
		case 115:
			compressionFormat = CompressionFormat.Bc5;
			break;
		case 130:
		case 131:
			compressionFormat = CompressionFormat.Bc7;
			break;
		default:
			throw new FgoFormatException($"尚不支持纹理格式 {formatId}。");
		}
		CompressionFormat format = compressionFormat;
		ColorRgba32[] array2 = new BcDecoder().DecodeRaw(payload, width, height, format);
		if (array2.Length < num)
		{
			throw new FgoFormatException($"纹理格式 {formatId} 解码结果被截断。");
		}
		for (int l = 0; l < num; l++)
		{
			ColorRgba32 colorRgba = array2[l];
			int num6 = l * 4;
			array[num6] = colorRgba.b;
			array[num6 + 1] = colorRgba.g;
			array[num6 + 2] = colorRgba.r;
			array[num6 + 3] = colorRgba.a;
		}
		return array;
	}

	private static FgoTexture ParseTxp(byte[] container, int offset, string name, bool decodeImages, int depth = 0)
	{
		if (depth > 8)
		{
			throw new FgoFormatException(name + " 的 TXP 嵌套层级异常。");
		}
		BinarySpanReader binarySpanReader = new BinarySpanReader(container, offset);
		uint num = binarySpanReader.ReadUInt32();
		uint num2 = num >> 24;
		if ((num & 0xFFFFFF) != 5265492)
		{
			throw new FgoFormatException($"{name} 的 TXP 魔数无效：0x{num:X8}");
		}
		if (num2 == 2)
		{
			return ParseImageRecord(container, offset, name, decodeImages, isCubeMap: false);
		}
		int num3 = checked((int)binarySpanReader.ReadUInt32());
		int val = binarySpanReader.ReadByte();
		int val2 = binarySpanReader.ReadByte();
		binarySpanReader.Skip(2);
		if ((num3 <= 0 || num3 > 4096) ? true : false)
		{
			throw new FgoFormatException($"{name} 的 mip 数量异常：{num3}");
		}
		int[] array = new int[num3];
		for (int i = 0; i < num3; i++)
		{
			array[i] = checked(offset + (int)binarySpanReader.ReadUInt32());
		}
		val2 = Math.Max(val2, 1);
		val = Math.Max(val, 1);
		List<int> list = new List<int>();
		for (int j = 0; j < val2; j++)
		{
			int num4 = j * val;
			if (num4 < array.Length)
			{
				list.Add(array[num4]);
			}
		}
		if (list.Count == 0)
		{
			throw new FgoFormatException(name + " 不含图像切片。");
		}
		uint num5 = new BinarySpanReader(container, list[0]).ReadUInt32();
		if ((num5 & 0xFFFFFF) == 5265492 && num5 >> 24 != 2)
		{
			FgoTexture fgoTexture = ParseTxp(container, list[0], name, decodeImages, depth + 1);
			return new FgoTexture
			{
				Name = fgoTexture.Name,
				Width = fgoTexture.Width,
				Height = fgoTexture.Height,
				FormatId = fgoTexture.FormatId,
				BgraPixels = fgoTexture.BgraPixels,
				IsCubeMap = (fgoTexture.IsCubeMap || list.Count > 1)
			};
		}
		return ParseImageRecord(container, list[0], name, decodeImages, list.Count > 1);
	}

	private static FgoTexture ParseImageRecord(byte[] container, int offset, string name, bool decodeImages, bool isCubeMap)
	{
		BinarySpanReader binarySpanReader = new BinarySpanReader(container, offset);
		binarySpanReader.ReadUInt32();
		checked
		{
			int width = Math.Max((int)binarySpanReader.ReadUInt32(), 1);
			int height = Math.Max((int)binarySpanReader.ReadUInt32(), 1);
			int formatId = (int)binarySpanReader.ReadUInt32();
			binarySpanReader.ReadUInt32();
			int count = (int)binarySpanReader.ReadUInt32();
			byte[] payload = binarySpanReader.ReadBytes(count);
			return new FgoTexture
			{
				Name = name,
				Width = width,
				Height = height,
				FormatId = formatId,
				BgraPixels = (decodeImages ? Decode(payload, width, height, formatId) : new byte[4] { 255, 255, 255, 255 }),
				IsCubeMap = isCubeMap
			};
		}
	}

	private static byte[] Decode16Bit(byte[] payload, int width, int height, int formatId)
	{
		int num;
		byte[] array;
		checked
		{
			num = width * height;
			EnsurePayload(payload, num * 2, formatId);
			array = new byte[num * 4];
		}
		for (int i = 0; i < num; i++)
		{
			ushort num2 = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(i * 2, 2));
			int num3 = i * 4;
			switch (formatId)
			{
			case 101:
				array[num3 + 2] = (byte)((num2 & 0x1F) * 255 / 31);
				array[num3 + 1] = (byte)(((num2 >> 5) & 0x1F) * 255 / 31);
				array[num3] = (byte)(((num2 >> 10) & 0x1F) * 255 / 31);
				array[num3 + 3] = byte.MaxValue;
				break;
			case 102:
				array[num3 + 2] = (byte)((num2 & 0x1F) * 255 / 31);
				array[num3 + 1] = (byte)(((num2 >> 5) & 0x1F) * 255 / 31);
				array[num3] = (byte)(((num2 >> 10) & 0x1F) * 255 / 31);
				array[num3 + 3] = (byte)(((num2 & 0x8000) != 0) ? byte.MaxValue : 0);
				break;
			default:
				array[num3 + 2] = (byte)((num2 & 0xF) * 17);
				array[num3 + 1] = (byte)(((num2 >> 4) & 0xF) * 17);
				array[num3] = (byte)(((num2 >> 8) & 0xF) * 17);
				array[num3 + 3] = (byte)(((num2 >> 12) & 0xF) * 17);
				break;
			}
		}
		return array;
	}

	private static void EnsurePayload(byte[] payload, int expected, int formatId)
	{
		if (payload.Length < expected)
		{
			throw new FgoFormatException($"纹理格式 {formatId} 数据被截断：实际 {payload.Length}，至少需要 {expected}");
		}
	}

	private static byte[] CreatePlaceholder()
	{
		byte[] array = new byte[256];
		for (int i = 0; i < 8; i++)
		{
			for (int j = 0; j < 8; j++)
			{
				int num = (i * 8 + j) * 4;
				bool flag = (j / 2 + i / 2) % 2 == 0;
				array[num] = (byte)(flag ? 90 : 35);
				array[num + 1] = (byte)(flag ? 55 : 25);
				array[num + 2] = (byte)(flag ? 20 : 10);
				array[num + 3] = byte.MaxValue;
			}
		}
		return array;
	}
}
