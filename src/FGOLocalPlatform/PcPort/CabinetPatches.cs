using System;
using System.Collections.Generic;

namespace FGOLocalPlatform;

/// <summary>One site of a cabinet patch in the running game: the row, its PC port switch, its address and its bytes before and after.</summary>
public sealed record CabinetPatch(string Row, string Key, long Address, byte[] Original, byte[] Replacement);

/// <summary>A game build: its name and the two header values of its ago.exe that tell it apart.</summary>
public readonly record struct GameBuildHeader(string Name, uint TimeDateStamp, uint SizeOfImage);

/// <summary>
/// One site of a cabinet patch: its address and original bytes in each build of the table, in build order, and its
/// replacement. Signature finds it in a build the table does not know: hex bytes, ".." for a masked one (a rip-relative
/// displacement, a rel32 branch target, or a byte of the site itself); the site is At bytes into its one match.
/// </summary>
public sealed record CabinetSite(string Row, string Key, IReadOnlyList<long> Address, IReadOnlyList<byte[]> Original, byte[] Replacement, string Signature, int At);

/// <summary>Where the game image sits in memory, the builds the table knows and every site in each of them.</summary>
public sealed record CabinetTable(long ImageBase, IReadOnlyList<GameBuildHeader> Builds, IReadOnlyList<CabinetSite> Sites);

/// <summary>The cabinet patches for game builds 11.00 (r47808) and 11.51 (r48215), as read from their ago.exe.</summary>
public static class CabinetPatches
{
	public const string NoContinueTimer = "noContinueTimer";

	public const string NoPopupTimers = "noPopupTimers";

	public const string NoGpPurchase = "noGpPurchase";

	public const string NoMenuTimer = "noMenuTimer";

	/// <summary>Relocations are stripped, so the image always loads at its preferred base.</summary>
	public static CabinetTable Table { get; } = new CabinetTable(0x140000000, new GameBuildHeader[]
	{
		new GameBuildHeader("11.00", 0x643F4ECD, 0x2034000),
		new GameBuildHeader("11.51", 0x65C5AF1E, 0x2039000)
	}, new CabinetSite[]
	{
		// Row 4, the quest continue panel: the countdown and the GP caution line.
		Site("4", NoContinueTimer, 0x14072da71, "ff 0f", 0x140730c41, "ff 0f", "66 90",
			6, "c7 07 ec 04 00 00 .. .."),
		Site("4", NoContinueTimer, 0x14072d766, "75 0e", 0x140730936, "75 0e", "eb 0e",
			2, "84 c0 .. .. 44 88 2d .. .. .. .."),
		// Row 5, one expiry per dialog family: the common menu window, the window timer component, layout-script
		// windows, trial servant and party select, ranking reward and score ranking, summon point, quest failed,
		// present box, QR code, the end-of-play continue window.
		Site("5", NoPopupTimers, 0x1408365e7, "7e 3b", 0x140839827, "7e 3b", "eb 3b",
			0, ".. .. ff c8 89 81 38 09 00 00"),
		Site("5", NoPopupTimers, 0x140deac20, "ff 43 1c", 0x140deeaa0, "ff 43 1c", "0f 1f 00",
			2, "5b c3 .. .. .. 0f 57 c9"),
		Site("5", NoPopupTimers, 0x140920128, "83 41 0c ff", 0x140923968, "83 41 0c ff", "83 41 0c 00",
			0, ".. .. .. .. 8b 41 0c 41 0f 48 c6"),
		Site("5", NoPopupTimers, 0x1404f4caf, "74 08", 0x1404f660f, "74 08", "eb 08",
			7, "80 b9 78 20 00 00 00 .. .."),
		Site("5", NoPopupTimers, 0x1404fc5c0, "74 0c", 0x1404fdf20, "74 0c", "eb 0c",
			5, "48 89 7c 24 30 .. .. 84 c9"),
		Site("5", NoPopupTimers, 0x1405899e3, "74 0c", 0x14058b9b3, "74 0c", "eb 0c",
			7, "40 38 b3 e8 1a 00 00 .. .."),
		Site("5", NoPopupTimers, 0x14058ec64, "74 0c", 0x140590c34, "74 0c", "eb 0c",
			6, "38 93 f0 1a 00 00 .. .."),
		Site("5", NoPopupTimers, 0x14060bfdb, "ff 86 40 8b 00 00", 0x14060f14b, "ff 86 40 8b 00 00", "66 0f 1f 44 00 00",
			0, ".. .. .. .. .. .. 66 0f 6e 86 40 8b 00 00"),
		Site("5", NoPopupTimers, 0x1407307f7, "ff c0", 0x1407339c7, "ff c0", "66 90",
			0, ".. .. 89 43 2c eb 53"),
		Site("5", NoPopupTimers, 0x1407f6a19, "74 08", 0x1407f9c89, "74 08", "eb 08",
			7, "80 be 0c 0e 00 00 00 .. .."),
		Site("5", NoPopupTimers, 0x1408168ca, "ff c0", 0x140819b3a, "ff c0", "66 90",
			2, "73 05 .. .. 89 46 4c"),
		Site("5", NoPopupTimers, 0x1407c56ec, "ff c9", 0x1407c88fc, "ff c9", "66 90",
			4, "85 c9 7e 46 .. .."),
		// Row 3a, card entry: the game's own no-purchase path of entry kind 8. 11.51 jumps one byte shorter.
		Site("3a", NoGpPurchase, 0x1408878eb, "74 40", 0x14088b910, "74 3f", "66 90",
			0, ".. .. 33 c9 e8 .. .. .. .. e8 .. .. .. .. 48 8b c8 e8 .. .. .. .. 48 8b d8 c6 40 18 01 c7 40 10 0c 00 00 00"),
		// Row 3a, main selector: the game's own no-purchase path of entry kind 7 for every entry kind that buys GP there
		// (spend_credit instead of the window, then the kind 7 branches of states 0xb and 0xa).
		Site("3a", NoGpPurchase, 0x140889eac, "74 09", 0x14088d9c4, "74 09", "eb 09",
			10, "75 13 e8 .. .. .. .. 83 f8 07 .. .."),
		Site("3a", NoGpPurchase, 0x14088a101, "75 1a", 0x14088dc23, "75 1a", "66 90",
			3, "83 f8 07 .. .. b8 0a 00 00 00"),
		Site("3a", NoGpPurchase, 0x14088a005, "75 30", 0x14088db28, "75 30", "66 90",
			3, "83 f8 07 .. .. 33 c9"),
		// Row 9, the menus' Time Remaining countdown that returns to the title at 0:00: its one tick, so the time left stays.
		Site("9", NoMenuTimer, 0x1408864d2, "ff 8f 84 00 00 00", 0x14088a502, "ff 8f 84 00 00 00", "66 0f 1f 44 00 00",
			2, "74 06 .. .. .. .. .. .. f3 0f 10 35 .. .. .. ..")
	});

	private static CabinetSite Site(string row, string key, long address1100, string original1100, long address1151, string original1151, string replacement, int at, string signature)
	{
		return new CabinetSite(row, key, new[] { address1100, address1151 }, new[] { Bytes(original1100), Bytes(original1151) }, Bytes(replacement), signature, at);
	}

	private static byte[] Bytes(string hex)
	{
		return Convert.FromHexString(hex.Replace(" ", ""));
	}
}
