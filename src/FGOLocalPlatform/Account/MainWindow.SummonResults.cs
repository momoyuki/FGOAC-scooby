using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private sealed class SummonHistoryEntry
	{
		public string ConfirmedAt { get; init; } = "";

		public int LotteryType { get; init; }

		public int LineupId { get; init; }

		public int DrawIndex { get; init; }

		public int DrawCount { get; init; }

		public int TradingCardId { get; init; }

		public int ServantId { get; init; }

		public int CraftEssenceId { get; init; }

		public int CardTypeId { get; init; } = 1;

		public string DisplayName { get; init; } = "";

		public string Sid { get; init; } = "";

		public string CardTypeLabel
		{
			get
			{
				if (CardTypeId != 2)
				{
					return "Servant";
				}
				return "Craft Essence";
			}
		}

		public int EntityId
		{
			get
			{
				if (CardTypeId != 2)
				{
					return ServantId;
				}
				return CraftEssenceId;
			}
		}

		public string DrawPosition
		{
			get
			{
				if (DrawCount <= 1)
				{
					return "Single";
				}
				return $"{DrawIndex}/{DrawCount}";
			}
		}

		public string PoolLabel => LotteryType switch
		{
			2 => "Friend Point", 
			3 => "Battle", 
			5 => $"Pickup {LineupId}", 
			6 => "Summon Point x1", 
			7 => "Summon Point x10", 
			_ => (LineupId > 0) ? $"Pool {LineupId}" : "History", 
		};
	}

	private static List<SummonHistoryEntry> ParseSummonHistory(JsonNode? node)
	{
		List<SummonHistoryEntry> list = new List<SummonHistoryEntry>();
		if (!(node is JsonArray jsonArray))
		{
			return list;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonObject jsonObject)
			{
				list.Add(new SummonHistoryEntry
				{
					ConfirmedAt = (jsonObject["confirmed_at"]?.GetValue<string>() ?? ""),
					LotteryType = (jsonObject["tc_lottery_type"]?.GetValue<int>() ?? 0),
					LineupId = (jsonObject["lineup_id"]?.GetValue<int>() ?? 0),
					DrawIndex = (jsonObject["draw_index"]?.GetValue<int>() ?? 1),
					DrawCount = (jsonObject["draw_count"]?.GetValue<int>() ?? 1),
					TradingCardId = (jsonObject["tc_id"]?.GetValue<int>() ?? 0),
					ServantId = (jsonObject["servant_id"]?.GetValue<int>() ?? 0),
					CraftEssenceId = (jsonObject["craft_essence_id"]?.GetValue<int>() ?? 0),
					CardTypeId = (jsonObject["card_type_id"]?.GetValue<int>() ?? 1),
					DisplayName = (jsonObject["display_name"]?.GetValue<string>() ?? ""),
					Sid = (jsonObject["sid"]?.GetValue<string>() ?? "")
				});
			}
		}
		return list;
	}

	private void SummonHistoryButton_OnClick(object sender, RoutedEventArgs e)
	{
		AccountEntry selectedAccount = SelectedAccount;
		if (selectedAccount != null)
		{
			DataGrid dataGrid = new DataGrid
			{
				IsReadOnly = true,
				AutoGenerateColumns = false,
				CanUserAddRows = false,
				CanUserDeleteRows = false,
				HeadersVisibility = DataGridHeadersVisibility.Column,
				GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
				ItemsSource = selectedAccount.SummonHistory,
				Margin = new Thickness(10.0)
			};
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Time",
				Binding = new Binding("ConfirmedAt"),
				Width = 190.0
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Pool",
				Binding = new Binding("PoolLabel"),
				Width = 150.0
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Position",
				Binding = new Binding("DrawPosition"),
				Width = 80.0
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Result",
				Binding = new Binding("DisplayName"),
				Width = new DataGridLength(1.0, DataGridLengthUnitType.Star)
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Card number",
				Binding = new Binding("TradingCardId"),
				Width = 125.0
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Type",
				Binding = new Binding("CardTypeLabel"),
				Width = 115.0
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Internal ID",
				Binding = new Binding("EntityId"),
				Width = 115.0
			});
			Window obj = new Window
			{
				Owner = this,
				Title = $"Summon results - {selectedAccount.MasterName} (ID {selectedAccount.AimeId})",
				Width = 1050.0,
				Height = 580.0,
				MinWidth = 760.0,
				MinHeight = 400.0,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				Background = new SolidColorBrush(Color.FromRgb(12, 10, 14)),
				Foreground = Brushes.White,
				Content = dataGrid
			};
			WindowTheme.Apply(obj);
			DialogSink.ShowDialog(obj);
		}
	}
}
