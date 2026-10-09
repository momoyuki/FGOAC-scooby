using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private static double ReadLayoutNumber(JsonObject layout, string name, double fallback, double minimum, double maximum)
	{
		if (!double.TryParse(layout[name]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return fallback;
		}
		return Math.Clamp(result, minimum, maximum);
	}

	private void LoadLayoutSettings()
	{
		base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		string path = Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");
		try
		{
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject jsonObject && jsonObject["uiLayout"] is JsonObject jsonObject2)
			{
				base.Width = ReadLayoutNumber(jsonObject2, "windowWidth", base.Width, base.MinWidth, 7680.0);
				base.Height = ReadLayoutNumber(jsonObject2, "windowHeight", base.Height, base.MinHeight, 4320.0);
				LogPanelColumn.Width = new GridLength(ReadLayoutNumber(jsonObject2, "logPanelWidth", 440.0, 310.0, 1600.0));
				expandedLogWidth = LogPanelColumn.Width.Value;
				SetLogsCollapsed(!(jsonObject2["logsDrawerOpen"]?.GetValue<bool>() ?? false));
				SettingsPanelRow.Height = new GridLength(52.0);
				DeckPanelRow.Height = new GridLength(ReadLayoutNumber(jsonObject2, "deckHeight", 312.0, 312.0, 700.0));
				ShowLogSource(jsonObject2["logsSource"]?.ToString() switch
				{
					"launcher" => LogSource.Launcher,
					"server" => LogSource.Server,
					_ => LogSource.Game
				});
				double num = ReadLayoutNumber(jsonObject2, "windowLeft", double.NaN, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth);
				double num2 = ReadLayoutNumber(jsonObject2, "windowTop", double.NaN, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight);
				if (!double.IsNaN(num) && !double.IsNaN(num2))
				{
					base.Left = num;
					base.Top = num2;
					base.WindowStartupLocation = WindowStartupLocation.Manual;
				}
				if (string.Equals(jsonObject2["windowState"]?.ToString(), "maximized", StringComparison.OrdinalIgnoreCase))
				{
					base.WindowState = WindowState.Maximized;
				}
			}
		}
		catch
		{
		}
	}

	private void SaveLayoutSettings()
	{
		string text = Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");
		try
		{
			JsonObject jsonObject = ((File.Exists(text) && JsonNode.Parse(File.ReadAllText(text)) is JsonObject jsonObject2) ? jsonObject2 : new JsonObject());
			Rect val = (Rect)((base.WindowState == WindowState.Normal) ? new Rect(base.Left, base.Top, base.ActualWidth, base.ActualHeight) : base.RestoreBounds);
			jsonObject["uiLayout"] = new JsonObject
			{
				["windowWidth"] = Math.Round(Math.Max(base.MinWidth, val.Width), 1),
				["windowHeight"] = Math.Round(Math.Max(base.MinHeight, val.Height), 1),
				["windowLeft"] = Math.Round(val.Left, 1),
				["windowTop"] = Math.Round(val.Top, 1),
				["windowState"] = ((base.WindowState == WindowState.Maximized) ? "maximized" : "normal"),
				["logPanelWidth"] = Math.Round(logsCollapsed ? expandedLogWidth : LogPanelColumn.ActualWidth, 1),
				["logsDrawerOpen"] = !logsCollapsed,
				["settingsHeight"] = Math.Round(SettingsPanelRow.ActualHeight, 1),
				["deckHeight"] = Math.Round(DeckPanelRow.ActualHeight, 1),
				["logsSource"] = logSource.ToString().ToLowerInvariant()
			};
			AtomicFile.WriteAllText(text, jsonObject.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}) + Environment.NewLine);
		}
		catch
		{
		}
	}
}
