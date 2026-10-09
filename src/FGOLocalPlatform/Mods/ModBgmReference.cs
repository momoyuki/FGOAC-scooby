using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hanhua;

namespace FGOLocalPlatform;

public static class ModBgmReference
{
	private sealed record Measurement(string Sha256, double Integrated, double TruePeak, double Range);

	private static readonly Lazy<Dictionary<string, Measurement>> NativeMeasurements = new Lazy<Dictionary<string, Measurement>>(delegate
	{
		using Stream utf8Json = typeof(ModBgmReference).Assembly.GetManifestResourceStream("FGOLocalPlatform.NativeBgmLoudness.json");
		return JsonSerializer.Deserialize<Dictionary<string, Measurement>>(utf8Json);
	});

	private static readonly Dictionary<string, Measurement> Measured = new Dictionary<string, Measurement>(StringComparer.OrdinalIgnoreCase);

	public static async Task<ModBgmLoudnessReference> ForPool(string root, string pool)
	{
		string fullPath = Path.GetFullPath(Path.Combine(root, ".."));
		string directory = Path.Combine(fullPath, "rom", "sound", "bgm");
		string[] names = ReferenceNames(fullPath, pool);
		if (names.Length == 0)
		{
			throw new InvalidDataException("Original BGM for this pool was not found; the loudness reference cannot be determined.");
		}
		List<Measurement> measurements = new List<Measurement>();
		string[] array = names;
		foreach (string text in array)
		{
			string text2 = Path.Combine(directory, text);
			if (!File.Exists(text2))
			{
				throw new FileNotFoundException("Original BGM is missing; the loudness reference cannot be determined.", text);
			}
			string hash;
			using (FileStream inputStream = File.OpenRead(text2))
			{
				using SHA256 sHA = SHA256.Create();
				hash = Convert.ToHexString(sHA.ComputeHash(inputStream));
			}
			string key = text2 + "|" + hash;
			JsonDocument json;
			if (!Measured.TryGetValue(key, out Measurement value))
			{
				if (!NativeMeasurements.Value.TryGetValue(text, out value) || value.Sha256 != hash)
				{
					(string, string) tuple = await ModAudioAnalysis.Run(Path.Combine(root, "Tools", "ffmpeg.exe"), new string[11]
					{
						"-nostdin", "-hide_banner", "-i", text2, "-map", "0:a:0", "-af", "aformat=sample_rates=44100:channel_layouts=stereo,loudnorm=print_format=json", "-f", "null",
						"-"
					});
					int num = tuple.Item2.LastIndexOf('{');
					int num2 = tuple.Item2.LastIndexOf('}');
					if (num < 0 || num2 < num)
					{
						throw new InvalidDataException("No loudness analysis result was obtained for the original BGM.");
					}
					string item = tuple.Item2;
					int num3 = num;
					json = JsonDocument.Parse(item.Substring(num3, num2 + 1 - num3));
					try
					{
						value = new Measurement(hash, Read("input_i"), Read("input_tp"), Read("input_lra"));
					}
					finally
					{
						if (json != null)
						{
							((IDisposable)json).Dispose();
						}
					}
				}
				if (!double.IsFinite(value.Integrated) || !double.IsFinite(value.TruePeak) || !double.IsFinite(value.Range))
				{
					throw new InvalidDataException("The original BGM has no valid loudness reference.");
				}
				Measured[key] = value;
			}
			measurements.Add(value);
			hash = null;
			double Read(string field)
			{
				return double.Parse(json.RootElement.GetProperty(field).GetString(), CultureInfo.InvariantCulture);
			}
		}
		return new ModBgmLoudnessReference(Math.Clamp(Median(measurements.Select((Measurement m) => m.Integrated)), -70.0, -5.0), Math.Clamp(measurements.Max((Measurement m) => m.TruePeak), -9.0, 0.0), Math.Clamp(Median(measurements.Select((Measurement m) => m.Range)), 1.0, 50.0), names);
		static double Median(IEnumerable<double> values)
		{
			double[] array2 = values.OrderBy((double v) => v).ToArray();
			int num4 = array2.Length / 2;
			if (array2.Length % 2 != 1)
			{
				return (array2[num4 - 1] + array2[num4]) / 2.0;
			}
			return array2[num4];
		}
	}

	public static string[] ReferenceNames(string gameRoot, string pool)
	{
		switch (pool)
		{
		case "menu":
			return new string[1] { "arms_bgm04.ogg" };
		case "room":
			return new string[1] { "arms_bgm05.ogg" };
		case "menu_room_shared":
			return new string[2] { "arms_bgm04.ogg", "arms_bgm05.ogg" };
		default:
		{
			if (Regex.IsMatch(pool, "\\Aarms_bgm[a-zA-Z0-9_]+\\.ogg\\z"))
			{
				return new string[1] { pool };
			}
			if (pool != "battle_all")
			{
				throw new InvalidDataException("Could not determine the original BGM for this pool.");
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string path = Path.Combine(gameRoot, "rom", "single");
			if (Directory.Exists(path))
			{
				foreach (string item in Directory.EnumerateFiles(path, "quest_prop_*.txt"))
				{
					foreach (Match item2 in Regex.Matches(File.ReadAllText(item), "(?m)^battle\\.\\d+\\.battle_bgm=(arms_bgm[a-zA-Z0-9_]+)\\r?$"))
					{
						hashSet.Add(item2.Groups[1].Value + ".ogg");
					}
				}
			}
			string path2 = Path.Combine(gameRoot, "rom", "mst_data", "arms_mst_stage_asset.farc");
			if (File.Exists(path2))
			{
				Archive archive = new Archive(File.ReadAllBytes(path2));
				foreach (Match item3 in Regex.Matches(Encoding.UTF8.GetString(archive.Read(archive.Entries.Single((ArchiveEntry e) => e.Name == "arms_mst_stage_asset.bin"))), "(?m)^stage_asset\\.\\d+\\.(?:normal_bgm|fatal_bgm)=(arms_bgm[a-zA-Z0-9_]+)\\r?$"))
				{
					hashSet.Add(item3.Groups[1].Value + ".ogg");
				}
			}
			return hashSet.OrderBy<string, string>((string n) => n, StringComparer.OrdinalIgnoreCase).ToArray();
		}
		}
	}
}
