using System;
using System.Buffers.Binary;
using System.Text;

namespace FgoArcadeStudio.Core;

internal sealed class BinarySpanReader
{
	public byte[] Data { get; }

	public int Position { get; private set; }

	public int Length => Data.Length;

	public BinarySpanReader(byte[] data, int offset = 0)
	{
		Data = data ?? throw new ArgumentNullException("data");
		Position = offset;
		Ensure(0, "起始位置");
	}

	public void Seek(long offset)
	{
		if ((offset < 0 || offset > int.MaxValue) ? true : false)
		{
			throw new FgoFormatException($"无效文件偏移：0x{offset:X}");
		}
		Position = (int)offset;
		Ensure(0, "文件偏移");
	}

	public void Skip(int count)
	{
		Seek((long)Position + (long)count);
	}

	public void CheckRange(long offset, long length, string label)
	{
		if (offset < 0 || length < 0 || offset > Data.LongLength || length > Data.LongLength - offset)
		{
			throw new FgoFormatException($"{label} 超出文件范围：offset=0x{offset:X}, size=0x{length:X}, file=0x{Data.LongLength:X}");
		}
	}

	public byte ReadByte()
	{
		Ensure(1, "byte");
		return Data[Position++];
	}

	public short ReadInt16()
	{
		Ensure(2, "int16");
		short result = BinaryPrimitives.ReadInt16LittleEndian(Data.AsSpan(Position, 2));
		Position += 2;
		return result;
	}

	public ushort ReadUInt16()
	{
		Ensure(2, "uint16");
		ushort result = BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(Position, 2));
		Position += 2;
		return result;
	}

	public int ReadInt32()
	{
		Ensure(4, "int32");
		int result = BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(Position, 4));
		Position += 4;
		return result;
	}

	public uint ReadUInt32()
	{
		Ensure(4, "uint32");
		uint result = BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(Position, 4));
		Position += 4;
		return result;
	}

	public uint ReadUInt32BigEndian()
	{
		Ensure(4, "big-endian uint32");
		uint result = BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(Position, 4));
		Position += 4;
		return result;
	}

	public long ReadInt64()
	{
		Ensure(8, "int64");
		long result = BinaryPrimitives.ReadInt64LittleEndian(Data.AsSpan(Position, 8));
		Position += 8;
		return result;
	}

	public ulong ReadUInt64()
	{
		Ensure(8, "uint64");
		ulong result = BinaryPrimitives.ReadUInt64LittleEndian(Data.AsSpan(Position, 8));
		Position += 8;
		return result;
	}

	public float ReadSingle()
	{
		Ensure(4, "float32");
		int value = BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(Position, 4));
		Position += 4;
		return BitConverter.Int32BitsToSingle(value);
	}

	public byte[] ReadBytes(int count)
	{
		Ensure(count, "byte array");
		byte[] result = Data.AsSpan(Position, count).ToArray();
		Position += count;
		return result;
	}

	public string ReadSizedString(int count, Encoding? encoding = null)
	{
		Ensure(count, "string");
		string result = (encoding ?? Encoding.UTF8).GetString(Data, Position, count);
		Position += count;
		return result;
	}

	public string ReadCString(Encoding? encoding = null)
	{
		int num = Array.IndexOf(Data, (byte)0, Position);
		if (num < 0)
		{
			throw new FgoFormatException("字符串缺少结尾的空字符。");
		}
		string result = (encoding ?? Encoding.UTF8).GetString(Data, Position, num - Position);
		Position = num + 1;
		return result;
	}

	private void Ensure(int count, string label)
	{
		CheckRange(Position, count, label);
	}
}
