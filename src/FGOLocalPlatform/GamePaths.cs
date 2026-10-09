using System;
using System.IO;
using DeckReaderUI;

namespace FGOLocalPlatform;

public static class GamePaths
{
	public static string GameRoot { get; } = ResolveGameRoot(AppContext.BaseDirectory);

	public static string LogsRoot => Path.GetFullPath(Path.Combine(GameRoot, "..", "logs"));

	public static void ResolveMovedCards(Config config, string gameRoot)
	{
		string latest = Path.GetFullPath(Path.Combine(gameRoot, "..", "DEVICE", "print", "FGO11_51_AllServants"));
		if (Directory.Exists(latest))
		{
			ResolveLatestCards(config, gameRoot, latest);
			return;
		}
		string fullPath = Path.GetFullPath(Path.Combine(gameRoot, "..", "DEVICE", "print", "FGO11_AllServants"));
		if (!Directory.Exists(fullPath))
		{
			return;
		}
		string text = config.CardsPath ?? "";
		if (!Directory.Exists(Path.GetFullPath(Path.Combine(gameRoot, text))) && text.Replace('/', '\\').TrimEnd('\\').EndsWith("\\FGO11_AllServants", StringComparison.OrdinalIgnoreCase))
		{
			config.CardsPath = Path.GetRelativePath(gameRoot, fullPath);
		}
		for (int i = 0; i < config.SelectedCards.Count; i++)
		{
			string text2 = config.SelectedCards[i];
			if (!File.Exists(Path.GetFullPath(Path.Combine(gameRoot, text2))) && text2.Replace('/', '\\').Contains("\\FGO11_AllServants\\", StringComparison.OrdinalIgnoreCase))
			{
				string path = Path.Combine(fullPath, Path.GetFileName(text2));
				if (File.Exists(path))
				{
					config.SelectedCards[i] = Path.GetRelativePath(gameRoot, path);
				}
			}
		}
	}

	/// <summary>A deck naming either card folder lands on the 11.51 one, as Cloud23333's 1.10 GamePaths maps it.</summary>
	private static void ResolveLatestCards(Config config, string gameRoot, string fullPath)
	{
		string fileName = Path.GetFileName((config.CardsPath ?? "").Replace('/', '\\').TrimEnd('\\'));
		if (fileName.Equals("FGO11_AllServants", StringComparison.OrdinalIgnoreCase) || fileName.Equals("FGO11_51_AllServants", StringComparison.OrdinalIgnoreCase))
		{
			config.CardsPath = Path.GetRelativePath(gameRoot, fullPath);
		}
		for (int i = 0; i < config.SelectedCards.Count; i++)
		{
			string text = config.SelectedCards[i];
			string text2 = text.Replace('/', '\\');
			if (text2.Contains("\\FGO11_AllServants\\", StringComparison.OrdinalIgnoreCase) || text2.Contains("\\FGO11_51_AllServants\\", StringComparison.OrdinalIgnoreCase))
			{
				string path = Path.Combine(fullPath, Path.GetFileName(text));
				if (File.Exists(path))
				{
					config.SelectedCards[i] = Path.GetRelativePath(gameRoot, path);
				}
			}
		}
	}

	public static string ResolveGameRoot(string launcherDirectory)
	{
		string fullPath = Path.GetFullPath(launcherDirectory);
		string text = Path.Combine(fullPath, "App");
		if (!File.Exists(Path.Combine(text, "ago.exe")))
		{
			return fullPath;
		}
		return text;
	}
}
