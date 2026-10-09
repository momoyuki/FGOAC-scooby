using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FGOLocalPlatform;

public sealed class ModLoopWaveform : FrameworkElement
{
	private ModWaveAudio? audio;

	private Geometry? waveform;

	private double cursor;

	private double anchor;

	private double dragMarkerTime;

	private Point dragPoint;

	private int dragging;

	private bool rangeDragged;

	private const double PlotTop = 84.0;

	private const double MarkerHitWidth = 18.0;

	private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(18, 20, 24));

	private static readonly Brush RangeBrush = new SolidColorBrush(Color.FromArgb(85, 124, 58, 237));

	private static readonly Pen WavePen = new Pen(new SolidColorBrush(Color.FromRgb(167, 139, 250)), 1.0);

	private static readonly Pen BeginPen = new Pen(new SolidColorBrush(Color.FromRgb(196, 181, 253)), 3.0);

	private static readonly Pen EndPen = new Pen(new SolidColorBrush(Color.FromRgb(233, 213, 255)), 3.0);

	private static readonly Brush MarkerFill = new SolidColorBrush(Color.FromRgb(38, 42, 49));

	private static readonly Pen CursorPen = new Pen(new SolidColorBrush(Color.FromRgb(230, 230, 227)), 1.0);

	private static readonly Pen GridPen = new Pen(new SolidColorBrush(Color.FromRgb(46, 50, 58)), 1.0);

	public double Duration => audio?.DurationSeconds ?? 0.0;

	public double LoopBegin { get; private set; }

	public double LoopEnd { get; private set; }

	public double ViewBegin { get; private set; }

	public double ViewLength { get; private set; }

	public double Position
	{
		get
		{
			return cursor;
		}
		set
		{
			cursor = value;
			InvalidateVisual();
		}
	}

	public event Action<bool>? LoopChanged;

	public event Action<double>? SeekRequested;

	public event Action? ViewChanged;

	public ModLoopWaveform()
	{
		base.Focusable = true;
		base.Cursor = Cursors.Cross;
		base.ClipToBounds = true;
		base.SizeChanged += delegate
		{
			RefreshWave();
		};
	}

	public void SetAudio(ModWaveAudio value, double? begin, double? end)
	{
		audio = value;
		ViewBegin = 0.0;
		ViewLength = Duration;
		SetLoop(begin.GetValueOrDefault(), end ?? Duration);
		RefreshWave();
		this.ViewChanged?.Invoke();
	}

	public void SetLoop(double begin, double end, bool final = true)
	{
		if (!(Duration <= 0.0))
		{
			double num = 2.2675736961451248E-05;
			LoopBegin = Math.Clamp(begin, 0.0, Math.Max(0.0, Duration - num));
			LoopEnd = Math.Clamp(end, LoopBegin + num, Duration);
			InvalidateVisual();
			this.LoopChanged?.Invoke(final);
		}
	}

	public void ShowRange(double begin, double length)
	{
		if (!(Duration <= 0.0))
		{
			ViewLength = Math.Clamp(length, Math.Min(Duration, 0.02), Duration);
			ViewBegin = Math.Clamp(begin, 0.0, Math.Max(0.0, Duration - ViewLength));
			RefreshWave();
			this.ViewChanged?.Invoke();
		}
	}

	public void Zoom(double factor, double? around = null)
	{
		if (!(Duration <= 0.0) && double.IsFinite(factor) && !(factor <= 0.0))
		{
			double num = around ?? (ViewBegin + ViewLength / 2.0);
			double num2 = ((ViewLength > 0.0) ? ((num - ViewBegin) / ViewLength) : 0.5);
			double num3 = Math.Clamp(ViewLength * factor, Math.Min(Duration, 0.02), Math.Max(Duration, 0.02));
			ShowRange(num - num2 * num3, num3);
		}
	}

	public double TimeAt(double x)
	{
		return Math.Clamp(ViewBegin + Math.Clamp(x, 0.0, base.ActualWidth) / Math.Max(1.0, base.ActualWidth) * ViewLength, 0.0, Duration);
	}

	private double X(double time)
	{
		return (time - ViewBegin) / Math.Max(1E-06, ViewLength) * base.ActualWidth;
	}

	private void RefreshWave()
	{
		waveform = null;
		if (audio != null && base.ActualWidth > 0.0 && base.ActualHeight > 94.0)
		{
			ModWavePeak[] peaks = audio.GetPeaks(ViewBegin, ViewBegin + ViewLength, Math.Max(1, (int)Math.Ceiling(base.ActualWidth)));
			StreamGeometry streamGeometry = new StreamGeometry();
			double num = (base.ActualHeight - 84.0) / 2.0 + 84.0;
			double num2 = (base.ActualHeight - 84.0 - 10.0) / 2.0;
			using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
			{
				for (int i = 0; i < peaks.Length; i++)
				{
					double x = ((double)i + 0.5) * base.ActualWidth / (double)peaks.Length;
					streamGeometryContext.BeginFigure(new Point(x, num - (double)peaks[i].Maximum * num2), isFilled: false, isClosed: false);
					streamGeometryContext.LineTo(new Point(x, num - (double)peaks[i].Minimum * num2), isStroked: true, isSmoothJoin: false);
				}
			}
			streamGeometry.Freeze();
			waveform = streamGeometry;
		}
		InvalidateVisual();
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		dc.DrawRectangle(BackgroundBrush, null, new Rect(base.RenderSize));
		if (!(Duration <= 0.0))
		{
			double num = Math.Clamp(X(LoopBegin), 0.0, base.ActualWidth);
			double num2 = Math.Clamp(X(LoopEnd), 0.0, base.ActualWidth);
			if (num2 > num)
			{
				dc.DrawRectangle(RangeBrush, null, new Rect(num, 84.0, num2 - num, Math.Max(0.0, base.ActualHeight - 84.0)));
			}
			for (int i = 0; i <= 5; i++)
			{
				double num3 = (double)i * base.ActualWidth / 5.0;
				dc.DrawLine(GridPen, new Point(num3, 84.0), new Point(num3, base.ActualHeight));
				string text = (ViewBegin + ViewLength * (double)i / 5.0).ToString("F3", CultureInfo.InvariantCulture) + " s";
				FormattedText formattedText = Text(text, Brushes.Gray);
				dc.DrawText(formattedText, new Point(Math.Clamp(num3 - formattedText.Width / 2.0, 2.0, Math.Max(2.0, base.ActualWidth - formattedText.Width - 2.0)), 3.0));
			}
			if (waveform != null)
			{
				dc.DrawGeometry(null, WavePen, waveform);
			}
			DrawMarker(dc, LoopBegin, right: false);
			DrawMarker(dc, LoopEnd, right: true);
			if (cursor >= ViewBegin && cursor <= ViewBegin + ViewLength)
			{
				dc.DrawLine(CursorPen, new Point(X(cursor), 84.0), new Point(X(cursor), base.ActualHeight));
			}
		}
	}

	private FormattedText Text(string text, Brush color)
	{
		return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12.0, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
	}

	private FormattedText MarkerLabel(double time, bool right)
	{
		return Text((right ? "End" : "Start") + "  " + time.ToString("F3", CultureInfo.InvariantCulture) + " s", (right ? EndPen : BeginPen).Brush);
	}

	private Rect MarkerBounds(double time, bool right)
	{
		double num = X(time);
		if (num < 0.0 || num > base.ActualWidth)
		{
			return Rect.Empty;
		}
		double num2 = MarkerLabel(time, right).Width + 20.0;
		return new Rect(Math.Clamp(right ? (num - num2 + 18.0) : (num - 18.0), 1.0, Math.Max(1.0, base.ActualWidth - num2 - 1.0)), right ? 57 : 30, num2, 22.0);
	}

	private void DrawMarker(DrawingContext dc, double time, bool right)
	{
		double num = X(time);
		if (!(num < 0.0) && !(num > base.ActualWidth))
		{
			Pen pen = (right ? EndPen : BeginPen);
			double num2 = (right ? 57 : 30);
			dc.DrawLine(pen, new Point(num, num2 + 12.0), new Point(num, base.ActualHeight));
			FormattedText formattedText = MarkerLabel(time, right);
			Rect rectangle = MarkerBounds(time, right);
			dc.DrawRoundedRectangle(MarkerFill, pen, rectangle, 3.0, 3.0);
			dc.DrawText(formattedText, new Point(rectangle.Left + 10.0, num2 + 3.0));
			dc.DrawRectangle(pen.Brush, null, new Rect(Math.Clamp(num - 7.0, 0.0, Math.Max(0.0, base.ActualWidth - 14.0)), 79.0, 14.0, 10.0));
		}
	}

	private int MarkerAt(Point point)
	{
		if (MarkerBounds(LoopBegin, right: false).Contains(point))
		{
			return 1;
		}
		if (MarkerBounds(LoopEnd, right: true).Contains(point))
		{
			return 2;
		}
		double num = X(LoopBegin);
		double num2 = X(LoopEnd);
		double num3 = ((num >= 0.0 && num <= base.ActualWidth) ? Math.Abs(num - point.X) : double.PositiveInfinity);
		double num4 = ((num2 >= 0.0 && num2 <= base.ActualWidth) ? Math.Abs(num2 - point.X) : double.PositiveInfinity);
		if (num3 <= 18.0 && num4 <= 18.0 && point.Y < 84.0)
		{
			if (!(point.Y >= 55.0))
			{
				return 1;
			}
			return 2;
		}
		if (!(Math.Min(num3, num4) <= 18.0))
		{
			return 0;
		}
		if (!(num3 <= num4))
		{
			return 2;
		}
		return 1;
	}

	protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		base.OnMouseLeftButtonDown(e);
		if (!(Duration <= 0.0))
		{
			Focus();
			dragPoint = e.GetPosition(this);
			anchor = TimeAt(dragPoint.X);
			rangeDragged = false;
			dragging = MarkerAt(dragPoint);
			if (dragging == 0)
			{
				dragging = 3;
			}
			dragMarkerTime = ((dragging == 1) ? LoopBegin : LoopEnd);
			CaptureMouse();
			e.Handled = true;
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		Point position = e.GetPosition(this);
		if (dragging == 0)
		{
			base.Cursor = ((MarkerAt(position) != 0) ? Cursors.SizeWE : Cursors.Cross);
			return;
		}
		double num = TimeAt(position.X);
		if (dragging == 1 || dragging == 2)
		{
			num = dragMarkerTime + num - anchor;
		}
		if (dragging == 1)
		{
			SetLoop(Math.Min(num, LoopEnd - 2.2675736961451248E-05), LoopEnd, final: false);
		}
		else if (dragging == 2)
		{
			SetLoop(LoopBegin, Math.Max(num, LoopBegin + 2.2675736961451248E-05), final: false);
		}
		else if (rangeDragged || Math.Abs(e.GetPosition(this).X - dragPoint.X) > 4.0)
		{
			rangeDragged = true;
			SetLoop(Math.Min(anchor, num), Math.Max(anchor, num), final: false);
		}
		e.Handled = true;
	}

	protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
	{
		base.OnMouseLeftButtonUp(e);
		if (dragging != 0)
		{
			int num = dragging;
			dragging = 0;
			ReleaseMouseCapture();
			if (num == 3 && !rangeDragged)
			{
				this.SeekRequested?.Invoke(TimeAt(e.GetPosition(this).X));
			}
			else
			{
				this.LoopChanged?.Invoke(obj: true);
			}
			e.Handled = true;
		}
	}

	protected override void OnLostMouseCapture(MouseEventArgs e)
	{
		base.OnLostMouseCapture(e);
		if (dragging != 0)
		{
			dragging = 0;
			this.LoopChanged?.Invoke(obj: true);
		}
	}

	protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
	{
		base.OnPreviewMouseWheel(e);
		if (!(Duration <= 0.0))
		{
			if (dragging == 0 && e.Delta != 0)
			{
				Zoom(Math.Pow(0.7, (double)e.Delta / 120.0), TimeAt(e.GetPosition(this).X));
			}
			e.Handled = true;
		}
	}
}
