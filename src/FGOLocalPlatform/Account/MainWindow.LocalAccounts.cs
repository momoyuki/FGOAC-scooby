using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private sealed class AccountEntry
	{
		public int AimeId { get; init; }

		public string MasterName { get; init; } = "";

		public string AccountMode { get; init; } = "";

		public string AccessCode { get; init; } = "";

		public bool IsCurrent { get; init; }

		public int ServantCount { get; init; }

		public int SummonResultCount { get; init; }

		public int OwnedCardCount { get; init; }

		public int OwnedCardCopyCount { get; init; }

		public int PendingPrintCount { get; init; }

		public HashSet<int> OwnedTradingCardIds { get; init; } = new HashSet<int>();

		public Dictionary<int, int> OwnedCardCounts { get; init; } = new Dictionary<int, int>();

		public List<SummonHistoryEntry> SummonHistory { get; init; } = new List<SummonHistoryEntry>();

		public int MasterLevel { get; init; } = 1;

		public int MasterExp { get; init; }

		public int MasterLevelExp { get; init; }

		public int MasterNextLevelExp { get; init; }

		public int MasterExpToNext { get; init; }

		public int QpAmount { get; init; }

		public int FriendPointAmount { get; init; }

		public int ManaPrismAmount { get; init; }

		public int SummonPointAmount { get; init; }

		public int ClearedQuestCount { get; init; }

		public int TrackedQuestCount { get; init; }

		public string ModeLabel
		{
			get
			{
				string accountMode = AccountMode;
				if (!(accountMode == "test_full"))
				{
					if (accountMode == "normal")
					{
						return "Normal";
					}
					return AccountMode;
				}
				return "All Servants";
			}
		}

		public string DisplayText => $"{MasterName} (ID {AimeId}) - Lv.{MasterLevel} - {ModeLabel}{(IsCurrent ? " (current)" : "")}";

		public override string ToString()
		{
			return DisplayText;
		}
	}

	private DateTime lastAccountProfileWriteUtc = DateTime.MinValue;

	private bool accountAutoRefreshRunning;

	private bool localToolMissing;

	/// <summary>True once the account list has been read, whatever it held.</summary>
	private bool accountsRead;

	private async Task RefreshAccountsAsync(bool showErrors)
	{
		int? previouslySelectedAimeId = SelectedAccount?.AimeId;
		AccountToolResult accountToolResult = await RunAccountToolAsync("list", "--json");
		accountsRead = true;
		if (!accountToolResult.Ok || accountToolResult.Root == null)
		{
			lastKnownServerRunning = false;
			localToolMissing = accountToolResult.Error == "tool_missing";
			AccountComboBox.ItemsSource = null;
			ApplyOwnedCardFilter();
			CurrentAccountText.Text = "Account tool unavailable";
			CurrentAccessCodeText.Text = "—";
			string text = (string.IsNullOrWhiteSpace(accountToolResult.Message) ? accountToolResult.Error : accountToolResult.Message);
			AppendAccountLog("list failed: " + text);
			if (showErrors)
			{
				ThemedMessageBox.Show("Could not read the local account list:\n" + text, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			return;
		}
		localToolMissing = false;
		lastKnownServerRunning = accountToolResult.Root["server_running"]?.GetValue<bool>() ?? false;
		string text2 = accountToolResult.Root["current_access_code"]?.GetValue<string>() ?? "";
		List<AccountEntry> list = new List<AccountEntry>();
		if (accountToolResult.Root["accounts"] is JsonArray jsonArray)
		{
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonObject jsonObject)
				{
					list.Add(new AccountEntry
					{
						AimeId = (jsonObject["aime_id"]?.GetValue<int>() ?? 0),
						MasterName = (jsonObject["master_name"]?.GetValue<string>() ?? ""),
						AccountMode = (jsonObject["account_mode"]?.GetValue<string>() ?? ""),
						AccessCode = (jsonObject["access_code"]?.GetValue<string>() ?? ""),
						IsCurrent = (jsonObject["is_current"]?.GetValue<bool>() ?? false),
						ServantCount = (jsonObject["servant_count"]?.GetValue<int>() ?? 0),
						SummonResultCount = (jsonObject["summon_result_count"]?.GetValue<int>() ?? 0),
						SummonHistory = ParseSummonHistory(jsonObject["summon_history"]),
						OwnedCardCount = (jsonObject["owned_card_count"]?.GetValue<int>() ?? 0),
						OwnedCardCopyCount = (jsonObject["owned_card_copy_count"]?.GetValue<int>() ?? 0),
						PendingPrintCount = (jsonObject["pending_print_count"]?.GetValue<int>() ?? 0),
						OwnedTradingCardIds = ParseOwnedCardIds(jsonObject["owned_cards"]),
						OwnedCardCounts = ParseOwnedCardCounts(jsonObject["owned_cards"]),
						MasterLevel = (jsonObject["master_level"]?.GetValue<int>() ?? 1),
						MasterExp = (jsonObject["master_exp"]?.GetValue<int>() ?? 0),
						MasterLevelExp = (jsonObject["master_level_exp"]?.GetValue<int>() ?? 0),
						MasterNextLevelExp = (jsonObject["master_next_level_exp"]?.GetValue<int>() ?? 0),
						MasterExpToNext = (jsonObject["master_exp_to_next"]?.GetValue<int>() ?? 0),
						QpAmount = (jsonObject["qp_amnt"]?.GetValue<int>() ?? 0),
						FriendPointAmount = (jsonObject["fp_amnt"]?.GetValue<int>() ?? 0),
						ManaPrismAmount = (jsonObject["mana_prism_amnt"]?.GetValue<int>() ?? 0),
						SummonPointAmount = (jsonObject["summon_point_amnt"]?.GetValue<int>() ?? 0),
						ClearedQuestCount = (jsonObject["cleared_quest_count"]?.GetValue<int>() ?? 0),
						TrackedQuestCount = (jsonObject["tracked_quest_count"]?.GetValue<int>() ?? 0)
					});
				}
			}
		}
		AccountComboBox.ItemsSource = list;
		AccountEntry accountEntry = list.FirstOrDefault((AccountEntry account) => account.IsCurrent);
		AccountEntry accountEntry2 = (previouslySelectedAimeId.HasValue ? list.FirstOrDefault((AccountEntry account) => account.AimeId == previouslySelectedAimeId.Value) : null);
		AccountComboBox.SelectedItem = accountEntry2 ?? accountEntry ?? ((list.Count > 0) ? list[0] : null);
		CurrentAccountText.Text = ((accountEntry != null) ? $"{accountEntry.MasterName} (ID {accountEntry.AimeId})" : ((list.Count == 0) ? "(no accounts yet - click New Account)" : "(not set)"));
		CurrentAccessCodeText.Text = ((text2.Length > 0) ? text2.Substring(Math.Max(0, text2.Length - 4)) : "—");
		AppendAccountLog($"Loaded {list.Count} local accounts; server {(lastKnownServerRunning ? "running" : "not running")}");
		string accountProfilesPath = GetAccountProfilesPath();
		if (File.Exists(accountProfilesPath))
		{
			lastAccountProfileWriteUtc = File.GetLastWriteTimeUtc(accountProfilesPath);
		}
	}

	private static string GetAccountProfilesPath()
	{
		return Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "state", "fgo-players.json"));
	}

	private async Task RefreshAccountsIfChangedAsync()
	{
		if (accountToolRunning || accountAutoRefreshRunning || windowClosing)
		{
			return;
		}
		string accountProfilesPath = GetAccountProfilesPath();
		if (!File.Exists(accountProfilesPath) || File.GetLastWriteTimeUtc(accountProfilesPath) == lastAccountProfileWriteUtc)
		{
			return;
		}
		accountAutoRefreshRunning = true;
		try
		{
			await RefreshAccountsAsync(showErrors: false);
		}
		finally
		{
			accountAutoRefreshRunning = false;
		}
	}

	private static HashSet<int> ParseOwnedCardIds(JsonNode? node)
	{
		HashSet<int> hashSet = new HashSet<int>();
		if (!(node is JsonArray jsonArray))
		{
			return hashSet;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonObject jsonObject)
			{
				int num = jsonObject["tc_id"]?.GetValue<int>() ?? 0;
				if (num > 0)
				{
					hashSet.Add(num);
				}
			}
		}
		return hashSet;
	}

	private static Dictionary<int, int> ParseOwnedCardCounts(JsonNode? node)
	{
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		if (node is JsonArray source)
		{
			foreach (JsonObject item in source.OfType<JsonObject>())
			{
				int num = item["tc_id"]?.GetValue<int>() ?? 0;
				int num2 = item["count"]?.GetValue<int>() ?? 0;
				if (num > 0 && num2 > 0)
				{
					dictionary[num] = num2;
				}
			}
		}
		return dictionary;
	}
}
