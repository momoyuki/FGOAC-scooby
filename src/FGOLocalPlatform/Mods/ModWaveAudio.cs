using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

public sealed class ModWaveAudio : IDisposable
{
	internal readonly record struct PlaybackPlan(int StartFrame, int EndFrame, int LoopStartFrame, int LoopEndFrame, bool Loop)
	{
		internal long PositionFrame(ulong framesPlayed)
		{
			ulong num = (uint)(EndFrame - StartFrame);
			if (!Loop || framesPlayed < num)
			{
				return (long)StartFrame + (long)Math.Min(framesPlayed, num);
			}
			return (long)LoopStartFrame + (long)((framesPlayed - num) % (uint)(LoopEndFrame - LoopStartFrame));
		}
	}

	[StructLayout(LayoutKind.Sequential, Pack = 2)]
	private struct WaveFormat
	{
		public ushort FormatTag;

		public ushort Channels;

		public uint SamplesPerSecond;

		public uint AverageBytesPerSecond;

		public ushort BlockAlign;

		public ushort BitsPerSample;

		public ushort ExtraSize;
	}

#pragma warning disable CS0649 // waveOut fills these fields in.
	private struct WaveHeader
	{
		public IntPtr Data;

		public uint BufferLength;

		public uint BytesRecorded;

		public UIntPtr User;

		public uint Flags;

		public uint Loops;

		public IntPtr Next;

		public UIntPtr Reserved;
	}
#pragma warning restore CS0649

	[StructLayout(LayoutKind.Explicit, Size = 12)]
	private struct MultimediaTime
	{
		[FieldOffset(0)]
		public uint Type;

		[FieldOffset(4)]
		public uint Value;
	}

	internal const int SampleRate = 44100;

	internal const int FrameBytes = 4;

	private const int PeakBlockFrames = 128;

	private readonly byte[] pcm;

	private readonly ModWavePeak[] blocks;

	private readonly object playbackLock = new object();

	private readonly List<IntPtr> headers = new List<IntPtr>();

	private IntPtr device;

	private GCHandle pinnedPcm;

	private PlaybackPlan plan;

	private uint lastDevicePosition;

	private ulong devicePositionWrap;

	private bool playing;

	private bool paused;

	private bool ended;

	private bool disposed;

	private double stoppedPosition;

	internal int FrameCount => pcm.Length / 4;

	public double DurationSeconds => (double)FrameCount / 44100.0;

	public double PositionSeconds
	{
		get
		{
			lock (playbackLock)
			{
				return CurrentPosition();
			}
		}
	}

	public bool IsPlaying
	{
		get
		{
			lock (playbackLock)
			{
				CurrentPosition();
				return playing;
			}
		}
	}

	public bool IsPaused
	{
		get
		{
			lock (playbackLock)
			{
				return paused;
			}
		}
	}

	public bool Ended
	{
		get
		{
			lock (playbackLock)
			{
				CurrentPosition();
				return ended;
			}
		}
	}

	private ModWaveAudio(byte[] data)
	{
		if (data.Length == 0 || data.Length % 4 != 0)
		{
			throw new InvalidDataException("The audio contains no complete PCM frames.");
		}
		pcm = data;
		blocks = new ModWavePeak[(FrameCount + 128 - 1) / 128];
		for (int i = 0; i < blocks.Length; i++)
		{
			blocks[i] = Scan(i * 128, Math.Min(FrameCount, (i + 1) * 128));
		}
	}

	internal static ModWaveAudio FromPcm(byte[] data)
	{
		return new ModWaveAudio(data);
	}

	public static async Task<ModWaveAudio> LoadAsync(string encoder, string file, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		ProcessStartInfo processStartInfo = new ProcessStartInfo(encoder)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		string[] array = new string[16]
		{
			"-nostdin", "-v", "error", "-i", file, "-map", "0:a:0", "-ar", "44100", "-ac",
			"2", "-c:a", "pcm_s16le", "-f", "s16le", "-"
		};
		foreach (string item in array)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		Process process = Process.Start(processStartInfo) ?? throw new IOException("Could not start the audio converter.");
		try
		{
			using (cancellationToken.Register(delegate
			{
				try
				{
					if (!process.HasExited)
					{
						process.Kill(entireProcessTree: true);
					}
				}
				catch (InvalidOperationException)
				{
				}
			}))
			{
				using MemoryStream output = new MemoryStream();
				Task<string> errorText = process.StandardError.ReadToEndAsync();
				try
				{
					await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken), process.WaitForExitAsync(cancellationToken)).ConfigureAwait(continueOnCapturedContext: false);
					string text = await errorText.ConfigureAwait(continueOnCapturedContext: false);
					cancellationToken.ThrowIfCancellationRequested();
					if (process.ExitCode != 0)
					{
						throw new InvalidDataException("Audio decoding failed: " + text);
					}
					byte[] data = output.ToArray();
					return await Task.Run(() => new ModWaveAudio(data), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch
				{
					try
					{
						if (!process.HasExited)
						{
							process.Kill(entireProcessTree: true);
						}
					}
					catch (InvalidOperationException)
					{
					}
					throw;
				}
			}
		}
		finally
		{
			if (process != null)
			{
				((IDisposable)process).Dispose();
			}
		}
	}

	public ModWavePeak[] GetPeaks(double startSeconds, double endSeconds, int bins)
	{
		if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || bins <= 0)
		{
			throw new ArgumentOutOfRangeException("startSeconds");
		}
		int num = ToFrame(startSeconds);
		int num2 = ToFrame(endSeconds);
		if (num2 <= num)
		{
			return new ModWavePeak[bins];
		}
		ModWavePeak[] array = new ModWavePeak[bins];
		long num3 = num2 - num;
		for (int i = 0; i < bins; i++)
		{
			int num4 = num + (int)(num3 * i / bins);
			int val = num + (int)(num3 * (i + 1) / bins);
			array[i] = RangePeak(num4, Math.Min(num2, Math.Max(num4 + 1, val)));
		}
		return array;
	}

	private ModWavePeak RangePeak(int start, int end)
	{
		float minimum = 1f;
		float maximum = -1f;
		int num = (start + 128 - 1) / 128;
		int num2 = end / 128;
		if (num >= num2)
		{
			return Scan(start, end);
		}
		Include(Scan(start, num * 128));
		for (int i = num; i < num2; i++)
		{
			Include(blocks[i]);
		}
		Include(Scan(num2 * 128, end));
		return new ModWavePeak(minimum, maximum);
		void Include(ModWavePeak p)
		{
			minimum = Math.Min(minimum, p.Minimum);
			maximum = Math.Max(maximum, p.Maximum);
		}
	}

	private ModWavePeak Scan(int start, int end)
	{
		float num = 1f;
		float num2 = -1f;
		for (int i = start * 4; i < end * 4; i += 2)
		{
			float val = (float)(short)(pcm[i] | (pcm[i + 1] << 8)) / 32768f;
			num = Math.Min(num, val);
			num2 = Math.Max(num2, val);
		}
		return new ModWavePeak(num, num2);
	}

	private int ToFrame(double seconds)
	{
		return (int)Math.Clamp(Math.Round(seconds * 44100.0), 0.0, FrameCount);
	}

	internal PlaybackPlan CreatePlaybackPlan(double startSeconds, double? loopBeginSeconds, double? loopEndSeconds)
	{
		if (!double.IsFinite(startSeconds))
		{
			throw new ArgumentOutOfRangeException("startSeconds");
		}
		bool num = loopBeginSeconds.HasValue || loopEndSeconds.HasValue;
		int num2 = ToFrame(startSeconds);
		if (!num)
		{
			return new PlaybackPlan(num2, FrameCount, 0, 0, Loop: false);
		}
		double valueOrDefault = loopBeginSeconds.GetValueOrDefault();
		double num3 = loopEndSeconds ?? DurationSeconds;
		if (!double.IsFinite(valueOrDefault) || !double.IsFinite(num3) || valueOrDefault < 0.0 || num3 > DurationSeconds || num3 <= valueOrDefault)
		{
			throw new ArgumentOutOfRangeException("loopEndSeconds");
		}
		int num4 = ToFrame(valueOrDefault);
		int num5 = ToFrame(num3);
		if (num5 <= num4)
		{
			throw new ArgumentOutOfRangeException("loopEndSeconds");
		}
		if (num2 >= num5)
		{
			num2 = num4;
		}
		return new PlaybackPlan(num2, num5, num4, num5, Loop: true);
	}

	public void Play(double startSeconds, double? loopBeginSeconds = null, double? loopEndSeconds = null)
	{
		lock (playbackLock)
		{
			ThrowIfDisposed();
			PlaybackPlan playbackPlan = CreatePlaybackPlan(startSeconds, loopBeginSeconds, loopEndSeconds);
			ReleasePlayback();
			plan = playbackPlan;
			stoppedPosition = (double)plan.StartFrame / 44100.0;
			ended = plan.StartFrame == plan.EndFrame && !plan.Loop;
			if (ended)
			{
				return;
			}
			WaveFormat format = new WaveFormat
			{
				FormatTag = 1,
				Channels = 2,
				SamplesPerSecond = 44100u,
				AverageBytesPerSecond = 176400u,
				BlockAlign = 4,
				BitsPerSample = 16
			};
			try
			{
				Check(waveOutOpen(out device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0u), "waveOutOpen");
				pinnedPcm = GCHandle.Alloc(pcm, GCHandleType.Pinned);
				Check(waveOutPause(device), "waveOutPause");
				Queue(plan.StartFrame, plan.EndFrame, loop: false);
				if (plan.Loop)
				{
					Queue(plan.LoopStartFrame, plan.LoopEndFrame, loop: true);
				}
				lastDevicePosition = 0u;
				devicePositionWrap = 0uL;
				Check(waveOutRestart(device), "waveOutRestart");
				playing = true;
				paused = false;
			}
			catch
			{
				ReleasePlayback();
				throw;
			}
		}
	}

	private void Queue(int start, int end, bool loop)
	{
		if (end > start)
		{
			WaveHeader structure = new WaveHeader
			{
				Data = IntPtr.Add(pinnedPcm.AddrOfPinnedObject(), start * 4),
				BufferLength = (uint)((end - start) * 4),
				Flags = (loop ? 12u : 0u),
				Loops = (loop ? uint.MaxValue : 0u)
			};
			IntPtr intPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
			Marshal.StructureToPtr(structure, intPtr, fDeleteOld: false);
			headers.Add(intPtr);
			Check(waveOutPrepareHeader(device, intPtr, (uint)Marshal.SizeOf<WaveHeader>()), "waveOutPrepareHeader");
			Check(waveOutWrite(device, intPtr, (uint)Marshal.SizeOf<WaveHeader>()), "waveOutWrite");
		}
	}

	private double CurrentPosition()
	{
		if (device == IntPtr.Zero)
		{
			return stoppedPosition;
		}
		MultimediaTime time = new MultimediaTime
		{
			Type = 2u
		};
		Check(waveOutGetPosition(device, ref time, (uint)Marshal.SizeOf<MultimediaTime>()), "waveOutGetPosition");
		uint value = time.Value;
		if (value < lastDevicePosition)
		{
			devicePositionWrap += 4294967296uL;
		}
		lastDevicePosition = value;
		ulong num = devicePositionWrap + value;
		ulong framesPlayed = time.Type switch
		{
			2u => num, 
			4u => num / 4, 
			1u => num * 44100 / 1000, 
			_ => throw new IOException("The audio device returned an unsupported position format."), 
		};
		if (!plan.Loop && headers.Count > 0)
		{
			List<IntPtr> list = headers;
			if ((Marshal.PtrToStructure<WaveHeader>(list[list.Count - 1]).Flags & 1) != 0)
			{
				playing = false;
				paused = false;
				ended = true;
				return (double)plan.EndFrame / 44100.0;
			}
		}
		return (double)plan.PositionFrame(framesPlayed) / 44100.0;
	}

	public void Pause()
	{
		lock (playbackLock)
		{
			ThrowIfDisposed();
			if (playing && !(device == IntPtr.Zero))
			{
				Check(waveOutPause(device), "waveOutPause");
				playing = false;
				paused = true;
			}
		}
	}

	public void Resume()
	{
		lock (playbackLock)
		{
			ThrowIfDisposed();
			if (paused && !(device == IntPtr.Zero))
			{
				Check(waveOutRestart(device), "waveOutRestart");
				playing = true;
				paused = false;
			}
		}
	}

	public void Stop()
	{
		lock (playbackLock)
		{
			ReleasePlayback();
			stoppedPosition = 0.0;
			ended = false;
		}
	}

	private void ReleasePlayback()
	{
		playing = false;
		paused = false;
		if (device != IntPtr.Zero)
		{
			waveOutReset(device);
			foreach (IntPtr header in headers)
			{
				waveOutUnprepareHeader(device, header, (uint)Marshal.SizeOf<WaveHeader>());
			}
			waveOutClose(device);
			device = IntPtr.Zero;
		}
		foreach (IntPtr header2 in headers)
		{
			Marshal.FreeHGlobal(header2);
		}
		headers.Clear();
		if (pinnedPcm.IsAllocated)
		{
			pinnedPcm.Free();
		}
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException("ModWaveAudio");
		}
	}

	public void Dispose()
	{
		lock (playbackLock)
		{
			if (!disposed)
			{
				ReleasePlayback();
				disposed = true;
			}
		}
	}

	private static void Check(uint result, string operation)
	{
		if (result == 0)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder(256);
		waveOutGetErrorText(result, stringBuilder, stringBuilder.Capacity);
		throw new IOException("Audio playback failed: " + operation + ": " + stringBuilder);
	}

	[DllImport("winmm.dll")]
	private static extern uint waveOutOpen(out IntPtr device, uint deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);

	[DllImport("winmm.dll")]
	private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);

	[DllImport("winmm.dll")]
	private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);

	[DllImport("winmm.dll")]
	private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);

	[DllImport("winmm.dll")]
	private static extern uint waveOutGetPosition(IntPtr device, ref MultimediaTime time, uint size);

	[DllImport("winmm.dll")]
	private static extern uint waveOutPause(IntPtr device);

	[DllImport("winmm.dll")]
	private static extern uint waveOutRestart(IntPtr device);

	[DllImport("winmm.dll")]
	private static extern uint waveOutReset(IntPtr device);

	[DllImport("winmm.dll")]
	private static extern uint waveOutClose(IntPtr device);

	[DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveOutGetErrorTextW")]
	private static extern uint waveOutGetErrorText(uint error, StringBuilder text, int capacity);
}
