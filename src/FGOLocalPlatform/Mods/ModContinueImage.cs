using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FGOLocalPlatform;

public static class ModContinueImage
{
	public static string PathFor(string root, string id)
	{
		if (!Regex.IsMatch(id, "\\A[a-f0-9]{32}\\z"))
		{
			throw new InvalidDataException("Invalid event ID.");
		}
		return Path.Combine(root, "Dialogue", "Events", "event-" + id, "continue.bmp");
	}

	public static void Import(string root, string id, string source)
	{
		using FileStream bitmapStream = File.OpenRead(source);
		BitmapFrame bitmapFrame = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
		if (bitmapFrame.PixelWidth <= 0 || bitmapFrame.PixelHeight <= 0 || (long)bitmapFrame.PixelWidth * (long)bitmapFrame.PixelHeight > 67108864)
		{
			throw new InvalidDataException("The image is too large. Use an image with no more than 64 million pixels.");
		}
		double num = Math.Min(1920.0 / (double)bitmapFrame.PixelWidth, 1080.0 / (double)bitmapFrame.PixelHeight);
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			drawingContext.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, 1920.0, 1080.0));
			double num2 = (double)bitmapFrame.PixelWidth * num;
			double num3 = (double)bitmapFrame.PixelHeight * num;
			drawingContext.DrawImage(bitmapFrame, new Rect((1920.0 - num2) / 2.0, (1080.0 - num3) / 2.0, num2, num3));
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(1920, 1080, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		BmpBitmapEncoder bmpBitmapEncoder = new BmpBitmapEncoder();
		bmpBitmapEncoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(renderTargetBitmap, PixelFormats.Bgr24, null, 0.0)));
		string text = PathFor(root, id);
		string text2 = text + "." + Guid.NewGuid().ToString("N") + ".tmp";
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		try
		{
			using (FileStream stream = File.Create(text2))
			{
				bmpBitmapEncoder.Save(stream);
			}
			File.Move(text2, text, overwrite: true);
		}
		finally
		{
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
		}
	}
}
