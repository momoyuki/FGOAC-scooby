using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// Export all and Import of the local accounts, through the transfer commands of Cloud23333's 1.10 account tool, so the
/// .fgoaccounts.json file is the one his launcher writes and reads: every account's card, full save and pending gifts.
/// An import is previewed first; the preview's token makes the tool refuse when the file or the accounts changed since.
/// </summary>
internal static class AccountTransfer
{
	/// <summary>What an import would do: how many accounts the file holds, the Masters in it whose card is already here, and its token.</summary>
	internal sealed record Preview(int Count, string[] Overwrites, string Token);

	internal const string FileFilter = "FGO account backup (*.fgoaccounts.json)|*.fgoaccounts.json";

	/// <summary>His tool's messages for the refusals a player can meet, with the sentence shown for each.</summary>
	private static readonly Dictionary<string, string> Sentences = new Dictionary<string, string>
	{
		// The export file already exists.
		["导出文件已存在，请选择新的文件名"] = "That file already exists. Export all again and give the file a new name.",
		// The file or the local accounts changed since the preview.
		["导入文件或本机账号已改变，请重新预览并确认"] = "The file or the accounts on this PC changed since the preview. Press Import again to see a new one.",
		// Not an account export his tool supports.
		["不是受支持的 11.51 账号导出文件"] = "That file is not an account export of Cloud23333's latest update.",
		// The export file holds no accounts.
		["导出文件没有账号"] = "That file holds no accounts.",
		// One imported account's cards belong to several local accounts.
		["一个导入账号的卡码对应多个本机账号，已取消"] = "Nothing was imported: one account in the file has cards of several accounts on this PC.",
		// Several imported accounts point at one local account.
		["多个导入账号指向同一本机账号，已取消"] = "Nothing was imported: several accounts in the file match the same account on this PC.",
		// An account to export has no database identity or no card in the server's database.
		["账号缺少数据库身份或卡码"] = "Nothing was exported: an account on this PC has no card in the local server's database.",
		// Two accounts to export share one Aime save.
		["重复 Aime 存档"] = "Nothing was exported: two accounts on this PC share one card's save."
	};

	/// <summary>True where his account tool has the transfer commands: Server\tools\fgo_account_transfer.py of his 1.10 update.</summary>
	internal static bool Offered(string serverRoot)
	{
		return File.Exists(Path.Combine(serverRoot, "tools", "fgo_account_transfer.py"));
	}

	internal static string DefaultFileName(DateTime now)
	{
		return "fgo-accounts-" + now.ToString("yyyyMMdd-HHmmss") + ".fgoaccounts.json";
	}

	internal static string[] ExportArguments(string file)
	{
		return new string[4] { "export-accounts", "--file", file, "--json" };
	}

	internal static string[] PreviewArguments(string file)
	{
		return new string[4] { "preview-accounts", "--file", file, "--json" };
	}

	internal static string[] ImportArguments(string file, string token)
	{
		return new string[6] { "import-accounts", "--file", file, "--confirm-token", token, "--json" };
	}

	internal static Preview ReadPreview(JsonObject root)
	{
		string[] overwrites = (root["overwrites"] as JsonArray)?.Select((JsonNode? name) => name?.ToString() ?? "").ToArray() ?? Array.Empty<string>();
		return new Preview(root["count"]?.GetValue<int>() ?? 0, overwrites, root["token"]?.ToString() ?? "");
	}

	/// <summary>The question asked before an import: the count, the Masters whose card is already here (twenty at most), and what stays.</summary>
	internal static string ConfirmText(Preview preview)
	{
		string replaced = (preview.Overwrites.Length == 0) ? "No account on this PC has one of their cards, so they are all added." : ("These Masters have a card an account on this PC already has, and replace that account's save:\n" + string.Join("\n", preview.Overwrites.Take(20)) + ((preview.Overwrites.Length > 20) ? "\n..." : ""));
		return $"Import {Accounts(preview.Count)}?\n\n{replaced}\n\nEverything is backed up first and the other accounts stay. An imported account whose number another card has here gets a new number.";
	}

	internal static string ImportedText(JsonObject root)
	{
		return $"Imported {Accounts(root["count"]?.GetValue<int>() ?? 0)}. The account in use and the deck stay as they were. Backup: {root["backup"]}";
	}

	/// <summary>True for his tool's answer while the local server runs, which carries a message and no error.</summary>
	internal static bool ServerRunning(JsonObject root)
	{
		return root["ok"]?.GetValue<bool>() != true && root["error"] == null;
	}

	/// <summary>The sentence for one of his tool's refusals; one it does not know is passed on as he wrote it.</summary>
	internal static string Explain(string message)
	{
		return Sentences.TryGetValue(message, out string? sentence) ? sentence : ("Cloud23333's account tool refused: " + message);
	}

	private static string Accounts(int count)
	{
		return (count == 1) ? "1 account" : $"{count} accounts";
	}
}
