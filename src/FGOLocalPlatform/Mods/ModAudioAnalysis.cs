using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

public static class ModAudioAnalysis
{
	private const int Rate = 11025;

	public static async Task<(string Output, string Error)> Run(string encoder, IEnumerable<string> args)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(encoder)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		foreach (string arg in args)
		{
			processStartInfo.ArgumentList.Add(arg);
		}
		using Process p = Process.Start(processStartInfo) ?? throw new IOException("Could not start the audio converter.");
		Task<string> stdout = p.StandardOutput.ReadToEndAsync();
		Task<string> stderr = p.StandardError.ReadToEndAsync();
		await p.WaitForExitAsync();
		string output = await stdout;
		string text = await stderr;
		if (p.ExitCode != 0)
		{
			throw new InvalidDataException("Audio analysis failed: " + text);
		}
		return (Output: output, Error: text);
	}

	public static async Task<double> Gain(string encoder, string file, double targetLufs = -18.0, double targetPeak = -1.5, double targetRange = 11.0)
	{
		return (await Loudness(encoder, file, targetLufs, targetPeak, targetRange)).GainDb;
	}

	public static async Task<ModLoudnessAnalysis> Loudness(string encoder, string file, double targetLufs = -18.0, double targetPeak = -1.5, double targetRange = 11.0)
	{
		string target = "loudnorm=I=" + Number(targetLufs) + ":TP=" + Number(targetPeak) + ":LRA=" + Number(targetRange);
		using (JsonDocument json = LoudnessJson((await Run(encoder, new string[11]
		{
			"-nostdin",
			"-hide_banner",
			"-i",
			file,
			"-map",
			"0:a:0",
			"-af",
			"aformat=sample_rates=44100:channel_layouts=stereo," + target + ":print_format=json",
			"-f",
			"null",
			"-"
		})).Item2))
		{
			double integrated = Read(json.RootElement, "input_i");
			double num = Read(json.RootElement, "input_tp");
			if (!double.IsFinite(integrated) || !double.IsFinite(num))
			{
				return new ModLoudnessAnalysis(0.0, "aformat=sample_rates=44100:channel_layouts=stereo");
			}
			string filter = "aformat=sample_rates=44100:channel_layouts=stereo," + MeasuredFilter(json.RootElement);
			if (targetLufs - integrated + num > targetPeak || Read(json.RootElement, "input_lra") > targetRange)
			{
				using JsonDocument jsonDocument = LoudnessJson((await Run(encoder, new string[11]
				{
					"-nostdin",
					"-hide_banner",
					"-i",
					file,
					"-map",
					"0:a:0",
					"-af",
					filter + "," + target + ":print_format=json",
					"-f",
					"null",
					"-"
				})).Item2);
				double num2 = Read(jsonDocument.RootElement, "input_i");
				if (double.IsFinite(num2) && Math.Abs(num2 - targetLufs) > 0.3)
				{
					filter = filter + "," + MeasuredFilter(jsonDocument.RootElement);
				}
			}
			return new ModLoudnessAnalysis(targetLufs - integrated, filter);
		}
		string MeasuredFilter(JsonElement measured)
		{
			double num3 = Read(measured, "input_i");
			double num4 = Read(measured, "input_tp");
			double num5 = Read(measured, "input_lra");
			double num6 = Read(measured, "input_thresh");
			double num7 = Read(measured, "target_offset");
			if (!double.IsFinite(num3) || !double.IsFinite(num4) || !double.IsFinite(num5) || !double.IsFinite(num6) || !double.IsFinite(num7))
			{
				throw new InvalidDataException("Invalid loudness measurement parameters.");
			}
			return target + ":measured_I=" + Number(num3) + ":measured_TP=" + Number(num4) + ":measured_LRA=" + Number(num5) + ":measured_thresh=" + Number(num6) + ":offset=" + Number(num7) + ":linear=true,aresample=44100";
		}
		static string Number(double value)
		{
			return value.ToString("R", CultureInfo.InvariantCulture);
		}
		static double Read(JsonElement measured, string key)
		{
			if (!double.TryParse(measured.GetProperty(key).GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				return double.NaN;
			}
			return result;
		}
	}

	private static JsonDocument LoudnessJson(string error)
	{
		int num = error.LastIndexOf('{');
		int num2 = error.LastIndexOf('}');
		if (num < 0 || num2 < num)
		{
			throw new InvalidDataException("No loudness analysis result obtained.");
		}
		int num3 = num;
		return JsonDocument.Parse(error.Substring(num3, num2 + 1 - num3));
	}

	/// <summary>A track's LoopAnalysis note in English; Cloud23333's launcher writes his own in Chinese.</summary>
	public static string Describe(string method)
	{
		return method switch
		{
			"音频自带循环标记" => "Loop tags of the file",
			"未找到可靠重复段，保留整曲循环" => "No clear repeat found; the whole track loops",
			"静音音频，保留整曲循环" => "Silent track; the whole track loops",
			"重复波形匹配" => "Found by matching the waveform",
			"手动波形选点" => "Chosen on the waveform",
			_ => method
		};
	}

	public static async Task<ModLoopAnalysis> Loop(string encoder, string file)
	{
		(string Output, string Error) meta = await Run(encoder, new string[9] { "-nostdin", "-hide_banner", "-i", file, "-map_metadata", "0:s:0", "-f", "ffmetadata", "-" });
		ProcessStartInfo processStartInfo = new ProcessStartInfo(encoder)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		string[] array = new string[14]
		{
			"-nostdin",
			"-v",
			"error",
			"-i",
			file,
			"-map",
			"0:a:0",
			"-ar",
			11025.ToString(CultureInfo.InvariantCulture),
			"-ac",
			"1",
			"-f",
			"f32le",
			"-"
		};
		foreach (string item in array)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		using Process p = Process.Start(processStartInfo) ?? throw new IOException("Could not start the audio converter.");
		using MemoryStream pcm = new MemoryStream();
		Task task = p.StandardOutput.BaseStream.CopyToAsync(pcm);
		Task<string> errors = p.StandardError.ReadToEndAsync();
		await Task.WhenAll(task, p.WaitForExitAsync());
		string text = await errors;
		if (p.ExitCode != 0)
		{
			throw new InvalidDataException("Audio analysis failed: " + text);
		}
		float[] samples = new float[pcm.Length / 4];
		Buffer.BlockCopy(pcm.GetBuffer(), 0, samples, 0, samples.Length * 4);
		double num = (double)samples.Length / 11025.0;
		Match match = Regex.Match(meta.Error, "Audio:.*?([0-9]+) Hz");
		if (match.Success && int.TryParse(match.Groups[1].Value, out var result) && result > 0)
		{
			Dictionary<string, double> dictionary = Regex.Matches(meta.Output, "(?im)^(LOOP_BEGIN|LOOP_START|LOOPSTART|LOOP_END|LOOPEND|LOOPLENGTH)=([0-9]+)\\r?$").Cast<Match>().ToDictionary((Match m) => m.Groups[1].Value.ToUpperInvariant(), (Match m) => double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
			double valueOrDefault = dictionary.GetValueOrDefault("LOOP_BEGIN", dictionary.GetValueOrDefault("LOOP_START", dictionary.GetValueOrDefault("LOOPSTART", double.NaN)));
			double valueOrDefault2 = dictionary.GetValueOrDefault("LOOP_END", dictionary.GetValueOrDefault("LOOPEND", valueOrDefault + dictionary.GetValueOrDefault("LOOPLENGTH", double.NaN)));
			if (double.IsFinite(valueOrDefault) && double.IsFinite(valueOrDefault2) && valueOrDefault >= 0.0 && valueOrDefault2 > valueOrDefault && valueOrDefault2 / (double)result <= num + 0.02)
			{
				return new ModLoopAnalysis(valueOrDefault / (double)result, Math.Min(valueOrDefault2 / (double)result, num), "Loop tags of the file");
			}
		}
		return await Task.Run(() => Detect(samples));
	}

	public static ModLoopAnalysis Detect(float[] samples)
	{
		int num = 551;
		int num2 = 11025;
		int num3 = 88200;
		if (samples.Length < num3 + 2 * num2)
		{
			return new ModLoopAnalysis(null, null, "No clear repeat found; the whole track loops");
		}
		double num4 = 0.0;
		foreach (float value in samples)
		{
			num4 = Math.Max(num4, Math.Abs(value));
		}
		if (num4 < 1E-05)
		{
			return new ModLoopAnalysis(null, null, "Silent track; the whole track loops");
		}
		int num5 = samples.Length - 1;
		while (num5 > num2 && (double)Math.Abs(samples[num5]) < num4 * 0.003)
		{
			num5--;
		}
		int num6 = samples.Length / num;
		double[] array = new double[num6];
		for (int j = 0; j < num6; j++)
		{
			double num7 = 0.0;
			for (int k = 0; k < num; k++)
			{
				double num8 = samples[j * num + k];
				num7 += num8 * num8;
			}
			array[j] = num7 / (double)num;
		}
		List<(double, int, int)> list = new List<(double, int, int)>();
		int num9 = Math.Min(num6 - 1, num5 / num);
		int num10 = Math.Max(num2 / num + num3 / num, num9 - 200);
		int num11 = num2 / num;
		for (int num12 = num9; num12 >= num10; num12 -= 2)
		{
			for (int l = num11; l <= num12 - num3 / num; l++)
			{
				double num13 = 0.0;
				double num14 = 0.0;
				for (int m = 0; m < num11; m++)
				{
					double num15 = array[num12 - num11 + m];
					double num16 = array[l - num11 + m];
					num13 += (num15 - num16) * (num15 - num16);
					num14 += num15 * num15 + num16 * num16;
				}
				double num17 = num13 / (num14 + 1E-20);
				if (!(num17 > 0.08) && !(num14 < 1E-12) && (list.Count != 24 || !(num17 >= list[0].Item1)))
				{
					if (list.Count == 24)
					{
						list.RemoveAt(0);
					}
					list.Add((num17, l * num, num12 * num));
					list.Sort(((double Score, int Start, int End) x, (double Score, int Start, int End) y) => y.Score.CompareTo(x.Score));
				}
			}
		}
		foreach (var item in list.OrderBy<(double, int, int), double>(((double Score, int Start, int End) c) => c.Score).Take(24))
		{
			int num18 = item.Item2;
			double num19 = double.PositiveInfinity;
			for (int num20 = item.Item2 - num; num20 <= item.Item2 + num; num20 += 3)
			{
				if (num20 >= num2 && num20 < item.Item3 - num3)
				{
					double num21 = Difference(samples, num20, item.Item3, num2, 11);
					if (num21 < num19)
					{
						num19 = num21;
						num18 = num20;
					}
				}
			}
			int num22 = num18;
			num19 = double.PositiveInfinity;
			for (int num23 = Math.Max(num2, num22 - 3); num23 <= num22 + 3; num23++)
			{
				double num24 = Difference(samples, num23, item.Item3, num2, 1);
				if (num24 < num19)
				{
					num19 = num24;
					num18 = num23;
				}
			}
			if (num18 < 2 * num2 || num19 > 0.025 || Difference(samples, num18 - num2, item.Item3 - num2, num2, 3) > 0.04)
			{
				continue;
			}
			int num25 = 0;
			double num26 = double.PositiveInfinity;
			for (int num27 = -100; num27 <= 100; num27++)
			{
				int num28 = num18 + num27;
				int num29 = item.Item3 + num27;
				if (num29 < samples.Length && num28 >= 1)
				{
					double num30 = Math.Abs(samples[num29] - samples[num28]) + Math.Abs(samples[num29]) + Math.Abs(samples[num28]);
					if (num30 < num26)
					{
						num26 = num30;
						num25 = num27;
					}
				}
			}
			return new ModLoopAnalysis((double)(num18 + num25) / 11025.0, (double)(item.Item3 + num25) / 11025.0, "Found by matching the waveform");
		}
		return new ModLoopAnalysis(null, null, "No clear repeat found; the whole track loops");
	}

	private static double Difference(float[] samples, int a, int b, int length, int stride)
	{
		double num = 0.0;
		double num2 = 0.0;
		for (int i = 1; i <= length; i += stride)
		{
			double num3 = samples[a - i];
			double num4 = samples[b - i];
			num += (num3 - num4) * (num3 - num4);
			num2 += num3 * num3 + num4 * num4;
		}
		if (!(num2 < 1E-12))
		{
			return num / num2;
		}
		return double.PositiveInfinity;
	}
}
