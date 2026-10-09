using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ZstdSharp;

namespace Hanhua;

public sealed class Archive
{
	private readonly byte[] directory;

	private readonly bool encrypted;

	private static readonly byte[] Key = Convert.FromHexString("62EC7CD79141695E53592ACC10CDC04C");

	public byte[] Data { get; }

	public List<ArchiveEntry> Entries { get; } = new List<ArchiveEntry>();

	public static int BE(byte[] b, int p)
	{
		return checked((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p, 4)));
	}

	public static int LE(byte[] b, int p)
	{
		return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4)));
	}

	public static void Put(byte[] b, int p, int n)
	{
		BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(p, 4), n);
	}

	public Archive(byte[] data)
	{
		Data = data;
		bool flag = data.Length < 32;
		if (!flag)
		{
			string text = Encoding.ASCII.GetString(data, 0, 4);
			flag = (!(text == "FARc") && !(text == "FARC")) || 1 == 0;
		}
		if (flag)
		{
			throw new InvalidDataException("不是支持的 FARC 档案");
		}
		encrypted = (BE(data, 8) & 4) != 0;
		directory = data;
		if (encrypted)
		{
			int num = checked(BE(data, 4) + 8);
			byte[] array = Decrypt(data.AsSpan(16, num - 16).ToArray());
			directory = new byte[16 + array.Length];
			data.AsSpan(0, 16).CopyTo(directory);
			array.CopyTo(directory, 16);
		}
		int num2 = BE(directory, 24);
		int num3 = 32;
		if (num2 > 100000)
		{
			throw new InvalidDataException("FARC 条目数量无效");
		}
		for (int i = 0; i < num2; i++)
		{
			int num4 = Array.IndexOf(directory, (byte)0, num3);
			if (num4 < 0)
			{
				throw new InvalidDataException("FARC 名称截断");
			}
			string name = Encoding.UTF8.GetString(directory, num3, num4 - num3);
			num3 = num4 + 1;
			ArchiveEntry archiveEntry = new ArchiveEntry(name, BE(directory, num3), BE(directory, num3 + 4), BE(directory, num3 + 8), BE(directory, num3 + 12), num3);
			if (archiveEntry.Offset < 0 || (long)archiveEntry.Offset + (long)archiveEntry.Stored > data.Length)
			{
				throw new InvalidDataException("FARC 数据越界");
			}
			Entries.Add(archiveEntry);
			num3 += 16;
		}
	}

	public byte[] Read(ArchiveEntry e)
	{
		byte[] array = Data.AsSpan(e.Offset, e.Stored).ToArray();
		if (e.Flags == 0)
		{
			return array;
		}
		if ((e.Flags & 4) != 0)
		{
			if ((e.Flags & 0x10) != 0)
			{
				throw new NotSupportedException("分段加密档案尚未验证");
			}
			array = Decrypt(array);
			if ((e.Flags & 0x22) == 0)
			{
				if (array.Length != e.Size)
				{
					throw new InvalidDataException("解密长度不符");
				}
				return array;
			}
		}
		List<byte[]> list = new List<byte[]>();
		if ((e.Flags & 0x10) != 0)
		{
			int num = LE(array, 0);
			if (num <= 0)
			{
				throw new InvalidDataException("压缩块大小无效");
			}
			int num2 = checked(e.Size + num - 1) / num;
			int num3 = checked(4 + num2 * 4);
			for (int i = 0; i < num2; i++)
			{
				int num4 = LE(array, 4 + i * 4);
				list.Add(array.AsSpan(num3, num4).ToArray());
				num3 += num4;
			}
		}
		else
		{
			list.Add(array);
		}
		using MemoryStream memoryStream = new MemoryStream();
		foreach (byte[] item in list)
		{
			if ((e.Flags & 0x20) != 0)
			{
				using (Decompressor decompressor = new Decompressor())
				{
					memoryStream.Write(decompressor.Unwrap(item));
				}
				continue;
			}
			if ((e.Flags & 2) != 0)
			{
				using (GZipStream gZipStream = new GZipStream(new MemoryStream(item), CompressionMode.Decompress))
				{
					gZipStream.CopyTo(memoryStream);
				}
				continue;
			}
			throw new NotSupportedException($"未知压缩标记 {e.Flags:X}");
		}
		if (memoryStream.Length != e.Size)
		{
			throw new InvalidDataException("FARC 解压长度不匹配");
		}
		return memoryStream.ToArray();
	}

	private byte[] Encode(ArchiveEntry e, byte[] content)
	{
		if (e.Flags == 0)
		{
			return content;
		}
		if ((e.Flags & 4) != 0)
		{
			if ((e.Flags & 0x10) != 0)
			{
				throw new NotSupportedException("分段加密档案尚未验证");
			}
			return Encrypt(Encode(e with
			{
				Flags = (e.Flags & -5)
			}, content), Data.AsSpan(e.Offset, 16).ToArray());
		}
		int num = (((e.Flags & 0x10) != 0) ? LE(Data, e.Offset) : Math.Max(1, content.Length));
		List<byte[]> list = new List<byte[]>();
		for (int i = 0; i < content.Length; i += num)
		{
			byte[] array = content.AsSpan(i, Math.Min(num, content.Length - i)).ToArray();
			if ((e.Flags & 0x20) != 0)
			{
				using (Compressor compressor = new Compressor(9))
				{
					list.Add(compressor.Wrap(array).ToArray());
				}
				continue;
			}
			if ((e.Flags & 2) != 0)
			{
				using (MemoryStream memoryStream = new MemoryStream())
				{
					using (GZipStream gZipStream = new GZipStream(memoryStream, CompressionLevel.SmallestSize, leaveOpen: true))
					{
						gZipStream.Write(array);
					}
					list.Add(memoryStream.ToArray());
				}
				continue;
			}
			throw new NotSupportedException("不支持的编码标记");
		}
		using MemoryStream memoryStream2 = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream2, Encoding.UTF8, leaveOpen: true);
		if ((e.Flags & 0x10) != 0)
		{
			binaryWriter.Write(num);
			foreach (byte[] item in list)
			{
				binaryWriter.Write(item.Length);
			}
		}
		foreach (byte[] item2 in list)
		{
			binaryWriter.Write(item2);
		}
		return memoryStream2.ToArray();
	}

	public byte[] Repack(IReadOnlyDictionary<string, byte[]> replacements)
	{
		if (replacements.Count == 0)
		{
			return (byte[])Data.Clone();
		}
		foreach (string key in replacements.Keys)
		{
			if (!Entries.Any((ArchiveEntry e) => e.Name == key))
			{
				throw new InvalidDataException("未知 FARC 条目 " + key);
			}
		}
		if (replacements.All<KeyValuePair<string, byte[]>>((KeyValuePair<string, byte[]> r) => Read(Entries.Single((ArchiveEntry e) => e.Name == r.Key)).SequenceEqual(r.Value)))
		{
			return (byte[])Data.Clone();
		}
		int count = Entries.Min((ArchiveEntry e) => e.Offset);
		int num = BE(directory, 16);
		if (num < 1 || num > 65536)
		{
			throw new InvalidDataException("FARC 对齐参数异常");
		}
		using MemoryStream memoryStream = new MemoryStream();
		memoryStream.Write(Data, 0, count);
		List<(ArchiveEntry, int, int, int)> list = new List<(ArchiveEntry, int, int, int)>();
		foreach (ArchiveEntry entry in Entries)
		{
			byte[] value;
			bool flag = replacements.TryGetValue(entry.Name, out value);
			byte[] array = (flag ? Encode(entry, value) : Data.AsSpan(entry.Offset, entry.Stored).ToArray());
			list.Add((entry, checked((int)memoryStream.Position), array.Length, flag ? value.Length : entry.Size));
			memoryStream.Write(array);
			while (memoryStream.Position % num != 0L)
			{
				memoryStream.WriteByte(0);
			}
		}
		byte[] array2 = memoryStream.ToArray();
		byte[] array3 = (encrypted ? ((byte[])directory.Clone()) : array2);
		foreach (var (archiveEntry, n, n2, n3) in list)
		{
			Put(array3, archiveEntry.DirectoryOffset, n);
			Put(array3, archiveEntry.DirectoryOffset + 4, n2);
			Put(array3, archiveEntry.DirectoryOffset + 8, n3);
		}
		if (encrypted)
		{
			byte[] array4 = Encrypt(array3.AsSpan(16).ToArray(), Data.AsSpan(16, 16).ToArray());
			if (array4.Length != BE(Data, 4) + 8 - 16)
			{
				throw new InvalidDataException("加密头长度变化");
			}
			array4.CopyTo(array2, 16);
		}
		Archive archive = new Archive(array2);
		foreach (ArchiveEntry entry2 in archive.Entries)
		{
			if (replacements.TryGetValue(entry2.Name, out byte[] value2) && !archive.Read(entry2).SequenceEqual(value2))
			{
				throw new InvalidDataException("回包校验失败");
			}
		}
		return array2;
	}

	private static byte[] Decrypt(byte[] raw)
	{
		if (raw.Length < 32 || (raw.Length - 16) % 16 != 0)
		{
			throw new InvalidDataException("The encrypted game archive has an invalid length.");
		}
		using Aes aes = Aes.Create();
		aes.Key = Key;
		try
		{
			return aes.DecryptCbc(raw.AsSpan(16), raw.AsSpan(0, 16));
		}
		catch (CryptographicException innerException)
		{
			throw new InvalidDataException("FARC 解密失败：文件版本或密钥不匹配", innerException);
		}
	}

	private static byte[] Encrypt(byte[] raw, byte[] iv)
	{
		using Aes aes = Aes.Create();
		aes.Key = Key;
		byte[] array = aes.EncryptCbc(raw, iv);
		byte[] array2 = new byte[16 + array.Length];
		iv.CopyTo(array2, 0);
		array.CopyTo(array2, 16);
		return array2;
	}
}
