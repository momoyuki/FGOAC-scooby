using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

/// <summary>
/// The deck code: <c>FGOAC-</c> and the unpadded base64url of a version
/// byte, the deck's slots in order, the loadout name and a CRC-32 of everything before it.
/// </summary>
public static class DeckCode
{
	/// <summary>One place in the deck: the card id from the card image's trailer, its kind and its copy number.</summary>
	public readonly record struct Slot(int CardId, bool CraftEssence, int Copy);

	/// <summary>A code read back: the slots and the name, or the sentence it was refused with and no slots.</summary>
	public sealed record Reading(IReadOnlyList<Slot> Slots, string Name, string? Refusal);

	public const int NameMax = 48;

	private const string Prefix = "FGOAC-";

	private const byte Version = 1;

	private const int MaxSlots = 30;

	private const int MaxCardId = 16383;

	private const string NotWhole = "That is not a whole deck code. Copy all of it and paste it again.";

	private const string Newer = "A newer launcher made this deck code. Update the launcher to read it.";

	private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	public static Slot SlotOf(Card card)
	{
		return new Slot(card.TrcId, card.CardTypeId == 2, card.CopyNumber);
	}

	/// <summary>
	/// The code of a deck of 1 to 30 distinct slots under <paramref name="name"/>, which loses its control
	/// characters and is cut to 48 bytes at a character boundary.
	/// </summary>
	public static string Encode(IReadOnlyList<Slot> slots, string name)
	{
		if (slots.Count < 1 || slots.Count > MaxSlots)
		{
			throw new ArgumentOutOfRangeException(nameof(slots), "A deck code carries 1 to 30 cards.");
		}
		StringBuilder kept = new StringBuilder();
		int length = 0;
		foreach (Rune rune in name.EnumerateRunes())
		{
			if (Rune.IsControl(rune))
			{
				continue;
			}
			if (length + rune.Utf8SequenceLength > NameMax)
			{
				break;
			}
			kept.Append(rune.ToString());
			length += rune.Utf8SequenceLength;
		}
		byte[] nameBytes = Encoding.UTF8.GetBytes(kept.ToString());
		byte[] bytes = new byte[3 + 3 * slots.Count + nameBytes.Length + 4];
		bytes[0] = Version;
		bytes[1] = (byte)slots.Count;
		HashSet<Slot> seen = new HashSet<Slot>();
		for (int i = 0; i < slots.Count; i++)
		{
			Slot slot = slots[i];
			if (slot.CardId < 1 || slot.CardId > MaxCardId || slot.Copy < 1 || slot.Copy > MaxSlots || !seen.Add(slot))
			{
				throw new ArgumentException($"Slot {i + 1} cannot go in a deck code: {slot}.", nameof(slots));
			}
			int entry = (slot.CardId << 10) | ((slot.CraftEssence ? 1 : 0) << 9) | ((slot.Copy - 1) << 4);
			bytes[2 + 3 * i] = (byte)(entry >> 16);
			bytes[3 + 3 * i] = (byte)(entry >> 8);
			bytes[4 + 3 * i] = (byte)entry;
		}
		int at = 2 + 3 * slots.Count;
		bytes[at] = (byte)nameBytes.Length;
		nameBytes.CopyTo(bytes, at + 1);
		BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(bytes.Length - 4), Crc32(bytes.AsSpan(0, bytes.Length - 4)));
		return Prefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	/// <summary>Reads a pasted code: whitespace anywhere is ignored and the prefix may be in any case.</summary>
	public static Reading Decode(string text)
	{
		StringBuilder compact = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			if (!char.IsWhiteSpace(c))
			{
				compact.Append(c);
			}
		}
		string code = compact.ToString();
		if (!code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
		{
			return Refused(NotWhole);
		}
		byte[]? bytes = FromBase64Url(code.Substring(Prefix.Length));
		if (bytes == null || bytes.Length == 0 || bytes[0] == 0)
		{
			return Refused(NotWhole);
		}
		if (bytes[0] > Version)
		{
			return Refused(Newer);
		}
		return Parse(bytes) ?? Refused(NotWhole);
	}

	/// <summary>The version 1 layout; null for anything that is not a whole code.</summary>
	private static Reading? Parse(byte[] bytes)
	{
		if (bytes.Length < 3 + 3 + 4)
		{
			return null;
		}
		ReadOnlySpan<byte> body = bytes.AsSpan(0, bytes.Length - 4);
		if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(bytes.Length - 4)) != Crc32(body))
		{
			return null;
		}
		int count = body[1];
		if (count < 1 || count > MaxSlots || body.Length < 3 + 3 * count)
		{
			return null;
		}
		int nameLength = body[2 + 3 * count];
		if (nameLength > NameMax || body.Length != 3 + 3 * count + nameLength)
		{
			return null;
		}
		string name;
		try
		{
			name = StrictUtf8.GetString(body.Slice(3 + 3 * count));
		}
		catch (DecoderFallbackException)
		{
			return null;
		}
		foreach (Rune rune in name.EnumerateRunes())
		{
			if (Rune.IsControl(rune))
			{
				return null;
			}
		}
		List<Slot> slots = new List<Slot>(count);
		HashSet<Slot> seen = new HashSet<Slot>();
		for (int i = 0; i < count; i++)
		{
			int entry = (body[2 + 3 * i] << 16) | (body[3 + 3 * i] << 8) | body[4 + 3 * i];
			Slot slot = new Slot(entry >> 10, ((entry >> 9) & 1) == 1, ((entry >> 4) & 0x1F) + 1);
			if ((entry & 0xF) != 0 || slot.CardId < 1 || slot.Copy > MaxSlots || !seen.Add(slot))
			{
				return null;
			}
			slots.Add(slot);
		}
		return new Reading(slots, name, null);
	}

	private static Reading Refused(string sentence)
	{
		return new Reading(Array.Empty<Slot>(), "", sentence);
	}

	/// <summary>Unpadded base64url in its one canonical spelling; null for any other text.</summary>
	private static byte[]? FromBase64Url(string text)
	{
		foreach (char c in text)
		{
			if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
			{
				return null;
			}
		}
		if (text.Length % 4 == 1)
		{
			return null;
		}
		string padded = text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4);
		byte[] bytes = Convert.FromBase64String(padded);
		if (Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != text)
		{
			return null;
		}
		return bytes;
	}

	/// <summary>CRC-32 as in zip: polynomial 0xEDB88320 reflected, start and final XOR 0xFFFFFFFF.</summary>
	private static uint Crc32(ReadOnlySpan<byte> data)
	{
		uint crc = uint.MaxValue;
		foreach (byte b in data)
		{
			crc ^= b;
			for (int bit = 0; bit < 8; bit++)
			{
				crc = ((crc & 1) != 0) ? ((crc >> 1) ^ 0xEDB88320u) : (crc >> 1);
			}
		}
		return ~crc;
	}
}
