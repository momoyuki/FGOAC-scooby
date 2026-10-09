using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace FGOLocalPlatform;

public enum SiteOutcome
{
	/// <summary>Its switch is off: the site was not read.</summary>
	Off,
	/// <summary>Not written: the process or its image header could not be read, or its switch was refused.</summary>
	Skipped,
	Patched,
	AlreadyPatched,
	/// <summary>The site held other bytes, could not be read or written, or its signature did not match exactly once, and refused its switch.</summary>
	Refused
}

/// <summary>A site of the table, its address in this game (0 when not looked up or not found) and what was done there.</summary>
public sealed record SiteResult(CabinetSite Site, long Address, SiteOutcome Outcome);

/// <summary>What one run did to each site, in table order, and the lines it logs.</summary>
public sealed record CabinetPatchRun(IReadOnlyList<SiteResult> Sites, IReadOnlyList<string> Lines);

/// <summary>Writes cabinet patches into a running game through its process handle, never into the file.</summary>
public static class CabinetPatcher
{
	private const uint VmOperation = 0x8;

	private const uint VmRead = 0x10;

	private const uint VmWrite = 0x20;

	private const uint QueryLimitedInformation = 0x1000;

	private const uint ExecuteReadWrite = 0x40;

	private const uint SectionExecute = 0x20000000;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr handle);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr written);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool VirtualProtectEx(IntPtr process, IntPtr address, IntPtr size, uint protect, out uint oldProtect);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, IntPtr size);

	/// <summary>
	/// Writes the sites of each switch that is on into the process, at their addresses in the build that the image
	/// header at the table's base names. A switch is written whole or not at all: every site is read first, one
	/// already holding its replacement is kept, and one holding anything else refuses its switch, logged, while the
	/// other switches are still written. A write that does not hold puts back the sites of its switch written before it.
	/// In a build the table does not know, each site of a switch that is on is found by its signature; a switch with a
	/// site whose signature does not match exactly once in the executable sections is left off, in one line.
	/// </summary>
	public static CabinetPatchRun Apply(int processId, CabinetTable table, PcPortSwitches switches)
	{
		if (!table.Sites.Any((CabinetSite site) => switches.IsOn(site.Key)))
		{
			return Unread(table.Sites, SiteOutcome.Off, "The cabinet patch switches are all off; nothing was written.");
		}
		IntPtr process = OpenProcess(VmOperation | VmRead | VmWrite | QueryLimitedInformation, inheritHandle: false, processId);
		if (process == IntPtr.Zero)
		{
			return Unread(table.Sites, SiteOutcome.Skipped, $"Could not open ago.exe (process {processId}) to patch it: {new Win32Exception().Message}; nothing was written.");
		}
		try
		{
			return Apply(table, switches, (long address, int count) => Read(process, address, count), (long address, byte[] bytes) => Write(process, address, bytes));
		}
		finally
		{
			CloseHandle(process);
		}
	}

	/// <summary>
	/// The same run through read, which gives count bytes at an address or null, and write, which puts bytes at an
	/// address and gives null or why it could not.
	/// </summary>
	public static CabinetPatchRun Apply(CabinetTable table, PcPortSwitches switches, Func<long, int, byte[]?> read, Func<long, byte[], string?> write)
	{
		ImageHeader? header = ReadHeader(read, table.ImageBase);
		if (header == null)
		{
			return Unread(table.Sites, SiteOutcome.Skipped, $"Could not read an image header at 0x{table.ImageBase:X} in ago.exe; nothing was written.");
		}
		uint timeDateStamp = header.Value.TimeDateStamp;
		uint sizeOfImage = header.Value.SizeOfImage;
		int index = Enumerable.Range(0, table.Builds.Count).FirstOrDefault((int b) => table.Builds[b].TimeDateStamp == timeDateStamp && table.Builds[b].SizeOfImage == sizeOfImage, -1);
		List<string> lines = new List<string>();
		CabinetPatch?[] sites;
		if (index >= 0)
		{
			lines.Add($"ago.exe is build {table.Builds[index].Name} (0x{timeDateStamp:X}, image size 0x{sizeOfImage:X}): writing the cabinet patches the PC play switches turn on.");
			sites = table.Sites.Select((CabinetSite site) => new CabinetPatch(site.Row, site.Key, site.Address[index], site.Original[index], site.Replacement)).ToArray();
		}
		else
		{
			lines.Add($"ago.exe is not a build the launcher knows (its header says 0x{timeDateStamp:X}, 0x{sizeOfImage:X}): finding the cabinet patch sites by their signatures.");
			long[] found = Find(table, header.Value, switches, read);
			sites = table.Sites.Select((CabinetSite site, int i) => found[i] == 0 ? null : new CabinetPatch(site.Row, site.Key, found[i], OriginalAt(site, found[i], read), site.Replacement)).ToArray();
		}
		SiteResult[] results = new SiteResult[sites.Length];
		foreach (string key in table.Sites.Select((CabinetSite site) => site.Key).Distinct())
		{
			int[] indexes = Enumerable.Range(0, sites.Length).Where((int i) => table.Sites[i].Key == key).ToArray();
			string row = table.Sites[indexes[0]].Row;
			bool on = switches.IsOn(key);
			int missing = indexes.Count((int i) => sites[i] == null);
			SiteOutcome[] outcomes = !on ? indexes.Select((int i) => SiteOutcome.Off).ToArray() : (missing > 0) ? indexes.Select((int i) => (sites[i] == null) ? SiteOutcome.Refused : SiteOutcome.Skipped).ToArray() : PatchSwitch(indexes.Select((int i) => sites[i]!).ToArray(), read, write, lines);
			for (int n = 0; n < indexes.Length; n++)
			{
				results[indexes[n]] = new SiteResult(table.Sites[indexes[n]], sites[indexes[n]]?.Address ?? 0, outcomes[n]);
			}
			int patched = outcomes.Count((SiteOutcome outcome) => outcome == SiteOutcome.Patched);
			int refused = outcomes.Count((SiteOutcome outcome) => outcome == SiteOutcome.Refused);
			lines.Add(!on ? $"{key} (row {row}): off, nothing written." : (missing > 0) ? $"{key} (row {row}): off, {missing} of {indexes.Length} sites not found exactly once in this ago.exe; nothing written." : (refused > 0 && patched == 0) ? $"{key} (row {row}): {refused} of {indexes.Length} refused, none written." : $"{key} (row {row}): {patched} patched, {outcomes.Count((SiteOutcome outcome) => outcome == SiteOutcome.AlreadyPatched)} already patched, {refused} refused.");
		}
		return new CabinetPatchRun(results, lines);
	}

	/// <summary>The build values of an image header and where its section table is.</summary>
	private readonly record struct ImageHeader(uint TimeDateStamp, uint SizeOfImage, long SectionTable, int SectionCount);

	/// <summary>The image header at imageBase; null when it cannot be read.</summary>
	private static ImageHeader? ReadHeader(Func<long, int, byte[]?> read, long imageBase)
	{
		byte[]? dos = read(imageBase, 0x40);
		int ntOffset = (dos != null && dos[0] == 'M' && dos[1] == 'Z') ? BitConverter.ToInt32(dos, 0x3C) : 0;
		// The NT headers: the signature, the 20-byte file header, then the optional header up to SizeOfImage.
		byte[]? nt = (ntOffset > 0 && ntOffset < 0x1000) ? read(imageBase + ntOffset, 24 + 60) : null;
		if (nt == null || nt[0] != 'P' || nt[1] != 'E' || nt[2] != 0 || nt[3] != 0)
		{
			return null;
		}
		return new ImageHeader(BitConverter.ToUInt32(nt, 8), BitConverter.ToUInt32(nt, 24 + 56), imageBase + ntOffset + 24 + BitConverter.ToUInt16(nt, 20), BitConverter.ToUInt16(nt, 6));
	}

	/// <summary>
	/// The address of each site whose switch is on: its signature's one match in the image's executable sections, plus
	/// At; 0 when the signature matches nowhere or more than once, or its switch is off.
	/// </summary>
	private static long[] Find(CabinetTable table, ImageHeader header, PcPortSwitches switches, Func<long, int, byte[]?> read)
	{
		long[] found = new long[table.Sites.Count];
		int[] matches = new int[table.Sites.Count];
		byte[]? sections = read(header.SectionTable, 40 * header.SectionCount);
		for (int s = 0; sections != null && s < header.SectionCount; s++)
		{
			uint virtualSize = BitConverter.ToUInt32(sections, 40 * s + 8);
			uint virtualAddress = BitConverter.ToUInt32(sections, 40 * s + 12);
			byte[]? code = ((BitConverter.ToUInt32(sections, 40 * s + 36) & SectionExecute) != 0 && virtualAddress + (long)virtualSize <= header.SizeOfImage) ? read(table.ImageBase + virtualAddress, (int)virtualSize) : null;
			for (int i = 0; code != null && i < table.Sites.Count; i++)
			{
				if (!switches.IsOn(table.Sites[i].Key))
				{
					continue;
				}
				foreach (int start in Matches(table.Sites[i].Signature, code, 2 - matches[i]))
				{
					if (matches[i]++ == 0)
					{
						found[i] = table.ImageBase + virtualAddress + start + table.Sites[i].At;
					}
				}
			}
		}
		return found.Select((long address, int i) => (matches[i] == 1) ? address : 0).ToArray();
	}

	/// <summary>The first offsets in code, at most limit, where signature matches: hex bytes compared, ".." bytes any.</summary>
	private static List<int> Matches(string signature, byte[] code, int limit)
	{
		string[] tokens = signature.Split(' ');
		byte[] bytes = tokens.Select((string token) => (token == "..") ? (byte)0 : Convert.ToByte(token, 16)).ToArray();
		// The first two known bytes in a row: searched for, then the rest compared around them.
		int key = Enumerable.Range(0, tokens.Length - 1).First((int k) => tokens[k] != ".." && tokens[k + 1] != "..");
		List<int> starts = new List<int>();
		for (int from = 0; starts.Count < limit; )
		{
			int hit = code.AsSpan(from).IndexOf(bytes.AsSpan(key, 2));
			if (hit < 0)
			{
				break;
			}
			int start = from + hit - key;
			from += hit + 1;
			if (start >= 0 && start + tokens.Length <= code.Length && Enumerable.Range(0, tokens.Length).All((int k) => tokens[k] == ".." || code[start + k] == bytes[k]))
			{
				starts.Add(start);
			}
		}
		return starts;
	}

	/// <summary>The original bytes of the table's builds that the site holds at address; else those of the last build.</summary>
	private static byte[] OriginalAt(CabinetSite site, long address, Func<long, int, byte[]?> read)
	{
		return site.Original.FirstOrDefault((byte[] original) => read(address, original.Length)?.AsSpan().SequenceEqual(original) ?? false) ?? site.Original[site.Original.Count - 1];
	}

	/// <summary>The outcome of each site of one switch: all read first, then written only if none is refused.</summary>
	private static SiteOutcome[] PatchSwitch(CabinetPatch[] sites, Func<long, int, byte[]?> read, Func<long, byte[], string?> write, List<string> lines)
	{
		// Skipped until written: a site holding its original bytes.
		SiteOutcome[] outcomes = sites.Select((CabinetPatch site) => Check(site, read, lines)).ToArray();
		if (outcomes.Contains(SiteOutcome.Refused))
		{
			return outcomes;
		}
		for (int n = 0; n < sites.Length; n++)
		{
			if (outcomes[n] == SiteOutcome.AlreadyPatched)
			{
				continue;
			}
			if (Put(sites[n], sites[n].Replacement, "written", read, write, lines))
			{
				outcomes[n] = SiteOutcome.Patched;
				continue;
			}
			outcomes[n] = SiteOutcome.Refused;
			for (int w = 0; w < n; w++)
			{
				if (outcomes[w] == SiteOutcome.Patched && Put(sites[w], sites[w].Original, "put back", read, write, lines))
				{
					outcomes[w] = SiteOutcome.Skipped;
				}
			}
			break;
		}
		return outcomes;
	}

	/// <summary>AlreadyPatched or Skipped for a site holding its replacement or its original bytes; else Refused, logged.</summary>
	private static SiteOutcome Check(CabinetPatch site, Func<long, int, byte[]?> read, List<string> lines)
	{
		byte[]? found = read(site.Address, site.Original.Length);
		if (found == null)
		{
			lines.Add($"{Where(site)} could not be read ({new Win32Exception().Message}); left as it is.");
			return SiteOutcome.Refused;
		}
		if (found.AsSpan().SequenceEqual(site.Replacement))
		{
			return SiteOutcome.AlreadyPatched;
		}
		if (!found.AsSpan().SequenceEqual(site.Original))
		{
			lines.Add($"{Where(site)} holds {Hex(found)}, not {Hex(site.Original)}; left as it is.");
			return SiteOutcome.Refused;
		}
		return SiteOutcome.Skipped;
	}

	/// <summary>Whether the site holds bytes once they are written; if not, a line says what it holds.</summary>
	private static bool Put(CabinetPatch site, byte[] bytes, string action, Func<long, int, byte[]?> read, Func<long, byte[], string?> write, List<string> lines)
	{
		string? error = write(site.Address, bytes);
		byte[]? now = read(site.Address, bytes.Length);
		if (error == null && now != null && now.AsSpan().SequenceEqual(bytes))
		{
			return true;
		}
		lines.Add($"{Where(site)} could not be {action} ({error ?? "the new bytes did not hold"}); it now holds {(now == null ? "unreadable bytes" : Hex(now))}.");
		return false;
	}

	private static string Where(CabinetPatch site)
	{
		return $"{site.Key} (row {site.Row}): 0x{site.Address:x}";
	}

	private static byte[]? Read(IntPtr process, long address, int count)
	{
		byte[] buffer = new byte[count];
		return (ReadProcessMemory(process, new IntPtr(address), buffer, new IntPtr(count), out IntPtr read) && read.ToInt64() == count) ? buffer : null;
	}

	/// <summary>Null once bytes are written at address, with the page's protection put back and the instruction cache flushed; else why not.</summary>
	private static string? Write(IntPtr process, long address, byte[] bytes)
	{
		IntPtr at = new IntPtr(address);
		IntPtr size = new IntPtr(bytes.Length);
		if (!VirtualProtectEx(process, at, size, ExecuteReadWrite, out uint oldProtect))
		{
			return "the page could not be made writable: " + new Win32Exception().Message;
		}
		string? error = WriteProcessMemory(process, at, bytes, size, out IntPtr _) ? null : new Win32Exception().Message;
		VirtualProtectEx(process, at, size, oldProtect, out uint _);
		FlushInstructionCache(process, at, size);
		return error;
	}

	private static CabinetPatchRun Unread(IReadOnlyList<CabinetSite> sites, SiteOutcome outcome, string line)
	{
		return new CabinetPatchRun(sites.Select((CabinetSite site) => new SiteResult(site, 0, outcome)).ToArray(), new[] { line });
	}

	private static string Hex(byte[] bytes)
	{
		return string.Join(" ", bytes.Select((byte b) => b.ToString("x2")));
	}
}
