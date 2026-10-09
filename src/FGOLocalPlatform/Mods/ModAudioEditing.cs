using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

public static class ModAudioEditing
{
	public static async Task<ModTrack> UpdateLoop(string root, ModTrack track, double begin, double end)
	{
		string text = ModStorage.TrackPath(root, track.File);
		if (!double.IsFinite(begin) || !double.IsFinite(end) || begin < 0.0 || end <= begin || end > ModStorage.TrackDuration(text))
		{
			throw new InvalidDataException("Set valid loop start and end points.");
		}
		checked
		{
			long first = (long)Math.Round(begin * 44100.0);
			long last = (long)Math.Round(end * 44100.0);
			if (last <= first)
			{
				throw new InvalidDataException("Set valid loop start and end points.");
			}
			string id = Guid.NewGuid().ToString("N");
			string output = Path.Combine(root, "BGM", id + ".ogg");
			string encoder = Path.Combine(root, "Tools", "ffmpeg.exe");
			Directory.CreateDirectory(Path.GetDirectoryName(output));
			try
			{
				await ModAudioAnalysis.Run(encoder, new string[14]
				{
					"-nostdin",
					"-v",
					"error",
					"-i",
					text,
					"-map",
					"0:a:0",
					"-c:a",
					"copy",
					"-metadata:s:a:0",
					"LOOP_BEGIN=" + first.ToString(CultureInfo.InvariantCulture),
					"-metadata:s:a:0",
					"LOOP_END=" + last.ToString(CultureInfo.InvariantCulture),
					output
				});
				(string Output, string Error) metadata = await ModAudioAnalysis.Run(encoder, new string[10] { "-nostdin", "-v", "error", "-i", output, "-map_metadata", "0:s:0", "-f", "ffmetadata", "-" });
				if (!Matches("LOOP_BEGIN", first) || !Matches("LOOP_END", last))
				{
					throw new InvalidDataException("Could not write the audio loop tags; these loop settings were not saved.");
				}
				bool Matches(string key, long expected)
				{
					MatchCollection matchCollection = Regex.Matches(metadata.Output, "(?m)^" + key + "=([0-9]+)\\r?$");
					if (matchCollection.Count == 1 && long.TryParse(matchCollection[0].Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var result))
					{
						return result == expected;
					}
					return false;
				}
			}
			catch
			{
				if (File.Exists(output))
				{
					File.Delete(output);
				}
				throw;
			}
			return new ModTrack
			{
				Pool = track.Pool,
				Title = track.Title,
				File = "1_MOD/BGM/" + id + ".ogg",
				LoopBegin = (double)first / 44100.0,
				LoopEnd = (double)last / 44100.0,
				AutoLoudness = track.AutoLoudness,
				GainDb = track.GainDb,
				LoopAnalysis = "Chosen on the waveform"
			};
		}
	}
}
