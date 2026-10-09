using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FGOLocalPlatform;

/// <summary>
/// logs\server-control.log, the launcher log: redacted, timestamped lines. A file last written on an earlier day
/// moves to server-control-YYYY-MM-DD.log; those older than fourteen days are deleted, no other file.
/// </summary>
public static class LauncherLog
{
	public const string FileName = "server-control.log";

	private const string ArchivePrefix = "server-control-";

	private const int KeptDays = 14;

	private static readonly object Gate = new object();

	public static void Append(string logsRoot, string line)
	{
		Append(logsRoot, line, DateTime.Now);
	}

	public static void Append(string logsRoot, string line, DateTime now)
	{
		lock (Gate)
		{
			Directory.CreateDirectory(logsRoot);
			string current = Path.Combine(logsRoot, FileName);
			if (File.Exists(current) && File.GetLastWriteTime(current).Date < now.Date)
			{
				Rotate(logsRoot, now);
			}
			File.AppendAllText(current, $"[{now:yyyy-MM-dd HH:mm:ss}] {Redaction.Apply(line)}\n", Encoding.UTF8);
		}
	}

	/// <summary>Archives a file last written before today and deletes the archives older than fourteen days; never throws.</summary>
	public static void Rotate(string logsRoot, DateTime now)
	{
		lock (Gate)
		{
			if (!Directory.Exists(logsRoot))
			{
				return;
			}
			string current = Path.Combine(logsRoot, FileName);
			try
			{
				DateTime written = File.GetLastWriteTime(current);
				if (File.Exists(current) && written.Date < now.Date)
				{
					File.Move(current, Path.Combine(logsRoot, ArchivePrefix + written.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log"));
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				// Another writer holds the file; the next line tries again.
			}
			DateTime oldestKept = now.Date.AddDays(1 - KeptDays);
			try
			{
				foreach (string archive in Directory.EnumerateFiles(logsRoot, ArchivePrefix + "*.log").Where((string path) => ArchiveDate(path) < oldestKept).ToList())
				{
					File.Delete(archive);
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				// Left for the next start.
			}
		}
	}

	/// <summary>The newest archived launcher log, or null when there is none.</summary>
	public static string? Previous(string logsRoot)
	{
		if (!Directory.Exists(logsRoot))
		{
			return null;
		}
		return Directory.EnumerateFiles(logsRoot, ArchivePrefix + "*.log")
			.Where((string path) => ArchiveDate(path) != null)
			.OrderByDescending((string path) => ArchiveDate(path))
			.FirstOrDefault();
	}

	private static DateTime? ArchiveDate(string path)
	{
		string name = Path.GetFileNameWithoutExtension(path);
		return name.Length == ArchivePrefix.Length + 10 && DateTime.TryParseExact(name.Substring(ArchivePrefix.Length), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day) ? day : null;
	}
}
