using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class ModLoopEditor : Window, IComponentConnector
{
	private readonly string root;

	private readonly ModTrack track;

	private readonly Func<double, double, Task> apply;

	private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

	private readonly DispatcherTimer timer;

	private ModWaveAudio? audio;

	private bool closed;

	private bool scrollUpdate;

	private bool busy;

	private bool applying;

	private bool wholePlayback;

	private bool controlUpdate;

	private bool loopSliderDragging;

	private bool positionDragging;

	private bool positionWasPlaying;

	private bool manualPosition;

	private bool seamPlayback;

	private int loopRevision;

	private double position;

	public ModLoopEditor(string root, ModTrack track, Func<double, double, Task> apply)
	{
		this.root = root;
		this.track = track;
		this.apply = apply;
		InitializeComponent();
		WindowTheme.Apply(this);
		TrackTitle.Text = track.Title;
		ApplyButton.IsEnabled = false;
		Wave.LoopChanged += delegate(bool final)
		{
			loopRevision++;
			UpdateLoopText();
			UpdateSliders();
			if (final)
			{
				ModWaveAudio? modWaveAudio = audio;
				if (modWaveAudio != null && modWaveAudio.IsPlaying && RepeatLoop.IsChecked == true)
				{
					wholePlayback = false;
					seamPlayback = true;
					manualPosition = false;
					PlayAt(SeamStart());
				}
			}
		};
		Wave.SeekRequested += Seek;
		Wave.ViewChanged += UpdateScroll;
		timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33.0), DispatcherPriority.Background, delegate
		{
			Tick();
		}, base.Dispatcher);
		base.Loaded += LoadAudio;
		base.Closing += delegate(object? _, CancelEventArgs e)
		{
			if (applying)
			{
				e.Cancel = true;
			}
		};
		base.Closed += delegate
		{
			closed = true;
			cancellation.Cancel();
			timer.Stop();
			audio?.Dispose();
		};
		Localize();
	}

	private void Localize()
	{
		UpdateLoopText();
		Wave.InvalidateVisual();
	}

	private async void LoadAudio(object sender, RoutedEventArgs e)
	{
		Status.Text = "Reading the waveform...";
		try
		{
			ModWaveAudio modWaveAudio = await ModWaveAudio.LoadAsync(Path.Combine(root, "Tools", "ffmpeg.exe"), ModStorage.TrackPath(root, track.File), cancellation.Token);
			if (closed)
			{
				modWaveAudio.Dispose();
				return;
			}
			audio = modWaveAudio;
			Wave.SetAudio(audio, track.LoopBegin, track.LoopEnd);
			position = 0.0;
			ApplyButton.IsEnabled = true;
			Status.Text = (track.LoopAnalysis.Length == 0) ? "Drag across the waveform to choose the loop." : ModAudioAnalysis.Describe(track.LoopAnalysis);
			timer.Start();
			Tick();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			if (!closed)
			{
				Status.Text = ex2.Message;
			}
		}
	}

	private void Tick()
	{
		if (audio != null)
		{
			if (!positionDragging && (audio.IsPlaying || audio.Ended))
			{
				position = audio.PositionSeconds;
			}
			Wave.Position = position;
			UpdatePositionControl();
			PlaybackTime.Text = position.ToString("F3", CultureInfo.InvariantCulture) + " / " + audio.DurationSeconds.ToString("F3", CultureInfo.InvariantCulture) + " s";
		}
	}

	private void UpdateLoopText()
	{
		LoopText.Text = "Start" + "  " + Wave.LoopBegin.ToString("F3", CultureInfo.InvariantCulture) + " s     " + "End" + "  " + Wave.LoopEnd.ToString("F3", CultureInfo.InvariantCulture) + " s     " + "Loop length" + "  " + (Wave.LoopEnd - Wave.LoopBegin).ToString("F3", CultureInfo.InvariantCulture) + " s";
	}

	private static string Seconds(double value)
	{
		return value.ToString("F3", CultureInfo.InvariantCulture) + " s";
	}

	private void UpdateSliders()
	{
		if (BeginSlider != null && EndSlider != null && PositionSlider != null)
		{
			controlUpdate = true;
			try
			{
				Slider beginSlider = BeginSlider;
				Slider endSlider = EndSlider;
				double num = (PositionSlider.Maximum = Wave.Duration);
				double maximum = (endSlider.Maximum = num);
				beginSlider.Maximum = maximum;
				Slider beginSlider2 = BeginSlider;
				Slider endSlider2 = EndSlider;
				bool flag = (PositionSlider.IsEnabled = Wave.Duration > 0.0);
				bool isEnabled = (endSlider2.IsEnabled = flag);
				beginSlider2.IsEnabled = isEnabled;
				BeginSlider.Value = Wave.LoopBegin;
				EndSlider.Value = Wave.LoopEnd;
				BeginTime.Text = Seconds(Wave.LoopBegin);
				EndTime.Text = Seconds(Wave.LoopEnd);
			}
			finally
			{
				controlUpdate = false;
			}
			UpdatePositionControl();
		}
	}

	private void UpdatePositionControl()
	{
		if (PositionSlider == null)
		{
			return;
		}
		controlUpdate = true;
		try
		{
			if (!positionDragging)
			{
				PositionSlider.Value = position;
			}
			PositionTime.Text = Seconds(position);
		}
		finally
		{
			controlUpdate = false;
		}
	}

	private void BeginChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (!controlUpdate && audio != null)
		{
			Wave.SetLoop(Math.Clamp(e.NewValue, 0.0, Math.Max(0.0, Wave.LoopEnd - 2.2675736961451248E-05)), Wave.LoopEnd, !loopSliderDragging);
		}
	}

	private void EndChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (!controlUpdate && audio != null)
		{
			Wave.SetLoop(Wave.LoopBegin, Math.Clamp(e.NewValue, Wave.LoopBegin + 2.2675736961451248E-05, Wave.Duration), !loopSliderDragging);
		}
	}

	private void LoopSliderDragStarted(object sender, DragStartedEventArgs e)
	{
		loopSliderDragging = true;
	}

	private void LoopSliderDragCompleted(object sender, DragCompletedEventArgs e)
	{
		loopSliderDragging = false;
		Wave.SetLoop(Wave.LoopBegin, Wave.LoopEnd);
	}

	private void NudgeBeginBack(object sender, RoutedEventArgs e)
	{
		BeginSlider.Value -= 0.01;
	}

	private void NudgeBeginForward(object sender, RoutedEventArgs e)
	{
		BeginSlider.Value += 0.01;
	}

	private void NudgeEndBack(object sender, RoutedEventArgs e)
	{
		EndSlider.Value -= 0.01;
	}

	private void NudgeEndForward(object sender, RoutedEventArgs e)
	{
		EndSlider.Value += 0.01;
	}

	private void PositionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (!controlUpdate && audio != null)
		{
			wholePlayback = false;
			seamPlayback = false;
			if (positionDragging)
			{
				position = e.NewValue;
				manualPosition = true;
				Tick();
			}
			else
			{
				Seek(e.NewValue);
			}
		}
	}

	private void PositionDragStarted(object sender, DragStartedEventArgs e)
	{
		if (audio != null)
		{
			positionDragging = true;
			positionWasPlaying = audio.IsPlaying;
			if (positionWasPlaying)
			{
				audio.Pause();
			}
		}
	}

	private void PositionDragCompleted(object sender, DragCompletedEventArgs e)
	{
		positionDragging = false;
		position = PositionSlider.Value;
		manualPosition = true;
		if (positionWasPlaying)
		{
			PlayAt(position);
		}
		else
		{
			audio?.Stop();
			Tick();
		}
		positionWasPlaying = false;
	}

	private void UpdateScroll()
	{
		scrollUpdate = true;
		TimelineScroll.Maximum = Math.Max(0.0, Wave.Duration - Wave.ViewLength);
		TimelineScroll.ViewportSize = Wave.ViewLength;
		TimelineScroll.LargeChange = Wave.ViewLength * 0.8;
		TimelineScroll.SmallChange = Wave.ViewLength * 0.05;
		TimelineScroll.Value = Wave.ViewBegin;
		scrollUpdate = false;
	}

	private void ScrollTimeline(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (!scrollUpdate && Wave != null)
		{
			Wave.ShowRange(e.NewValue, Wave.ViewLength);
		}
	}

	private void Seek(double time)
	{
		position = time;
		manualPosition = true;
		wholePlayback = false;
		seamPlayback = false;
		ModWaveAudio? modWaveAudio = audio;
		if (modWaveAudio != null && modWaveAudio.IsPlaying)
		{
			PlayAt(time);
			return;
		}
		audio?.Stop();
		Tick();
	}

	private void PlayAt(double time)
	{
		if (audio == null)
		{
			return;
		}
		try
		{
			audio.Play(time, (RepeatLoop.IsChecked == true) ? new double?(Wave.LoopBegin) : ((double?)null), (RepeatLoop.IsChecked == true) ? new double?(Wave.LoopEnd) : ((double?)null));
			position = audio.PositionSeconds;
			Status.Text = wholePlayback ? "Playing from the start to the loop end, then the loop again and again." : (seamPlayback ? "Playing the join: the end wraps to the loop start." : ((RepeatLoop.IsChecked == true) ? "Repeating the loop." : "Playing the whole track."));
			Tick();
		}
		catch (Exception ex)
		{
			Status.Text = ex.Message;
		}
	}

	private void PlayPause(object sender, RoutedEventArgs e)
	{
		if (audio == null || busy)
		{
			return;
		}
		try
		{
			if (audio.IsPlaying)
			{
				audio.Pause();
				position = audio.PositionSeconds;
				Tick();
			}
			else if (audio.IsPaused)
			{
				PlayAt(position);
			}
			else
			{
				wholePlayback = false;
				seamPlayback = RepeatLoop.IsChecked == true && !manualPosition;
				PlayAt(seamPlayback ? SeamStart() : position);
			}
		}
		catch (Exception ex)
		{
			Status.Text = ex.Message;
		}
	}

	private double SeamStart()
	{
		return Math.Max(0.0, Wave.LoopEnd - 4.0);
	}

	private void PlaySeam(object sender, RoutedEventArgs e)
	{
		if (audio != null && !busy)
		{
			RepeatLoop.IsChecked = true;
			wholePlayback = false;
			seamPlayback = true;
			manualPosition = false;
			PlayAt(SeamStart());
		}
	}

	private void PlayWhole(object sender, RoutedEventArgs e)
	{
		if (audio != null && !busy)
		{
			RepeatLoop.IsChecked = true;
			wholePlayback = true;
			seamPlayback = false;
			manualPosition = false;
			PlayAt(0.0);
		}
	}

	private void StopPlayback(object sender, RoutedEventArgs e)
	{
		audio?.Stop();
		wholePlayback = false;
		seamPlayback = false;
		manualPosition = false;
		position = 0.0;
		Tick();
	}

	private void RepeatChanged(object sender, RoutedEventArgs e)
	{
		wholePlayback = false;
		seamPlayback = RepeatLoop.IsChecked == true;
		manualPosition = false;
		ModWaveAudio? modWaveAudio = audio;
		if (modWaveAudio != null && modWaveAudio.IsPlaying)
		{
			PlayAt(seamPlayback ? SeamStart() : audio.PositionSeconds);
			return;
		}
		ModWaveAudio? modWaveAudio2 = audio;
		if (modWaveAudio2 != null && modWaveAudio2.IsPaused && seamPlayback)
		{
			position = SeamStart();
			Tick();
		}
	}

	private void MarkBegin(object sender, RoutedEventArgs e)
	{
		if (position >= Wave.LoopEnd)
		{
			Status.Text = "The loop start must come before its end.";
		}
		else
		{
			Wave.SetLoop(position, Wave.LoopEnd);
		}
	}

	private void MarkEnd(object sender, RoutedEventArgs e)
	{
		if (position <= Wave.LoopBegin)
		{
			Status.Text = "The loop end must come after its start.";
		}
		else
		{
			Wave.SetLoop(Wave.LoopBegin, position);
		}
	}

	private void ZoomIn(object sender, RoutedEventArgs e)
	{
		Wave.Zoom(0.5);
	}

	private void ZoomOut(object sender, RoutedEventArgs e)
	{
		Wave.Zoom(2.0);
	}

	private void ShowFull(object sender, RoutedEventArgs e)
	{
		Wave.ShowRange(0.0, Wave.Duration);
	}

	private void ShowLoop(object sender, RoutedEventArgs e)
	{
		double num = Wave.LoopEnd - Wave.LoopBegin;
		Wave.ShowRange(Wave.LoopBegin - num * 0.1, num * 1.2);
	}

	private async void AnalyzeLoop(object sender, RoutedEventArgs e)
	{
		if (audio == null || busy)
		{
			return;
		}
		busy = true;
		int revision = loopRevision;
		ApplyButton.IsEnabled = false;
		Status.Text = "Looking for a loop...";
		try
		{
			ModLoopAnalysis modLoopAnalysis = await ModAudioAnalysis.Loop(Path.Combine(root, "Tools", "ffmpeg.exe"), ModStorage.TrackPath(root, track.File));
			if (!closed)
			{
				if (revision == loopRevision)
				{
					Wave.SetLoop(modLoopAnalysis.Begin.GetValueOrDefault(), modLoopAnalysis.End ?? Wave.Duration);
					Status.Text = modLoopAnalysis.Method;
				}
				else
				{
					Status.Text = "Chosen on the waveform";
				}
			}
		}
		catch (Exception ex)
		{
			if (!closed)
			{
				Status.Text = ex.Message;
			}
		}
		finally
		{
			busy = false;
			if (!closed)
			{
				ApplyButton.IsEnabled = true;
			}
		}
	}

	private async void ApplyLoop(object sender, RoutedEventArgs e)
	{
		if (audio == null || busy)
		{
			return;
		}
		busy = true;
		applying = true;
		base.IsEnabled = false;
		audio.Stop();
		Status.Text = "Saving the loop points...";
		try
		{
			await apply(Wave.LoopBegin, Wave.LoopEnd);
			applying = false;
			if (!closed)
			{
				base.DialogResult = true;
				Close();
			}
		}
		catch (Exception ex)
		{
			if (!closed)
			{
				Status.Text = ex.Message;
			}
		}
		finally
		{
			applying = false;
			busy = false;
			if (!closed)
			{
				base.IsEnabled = true;
			}
		}
	}

	private void Cancel(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
