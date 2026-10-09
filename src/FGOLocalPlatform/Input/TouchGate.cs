using System;
using System.Collections.Generic;
using System.IO;

namespace FGOLocalPlatform;

/// <summary>A point in the game window's client area, in pixels from its top left corner.</summary>
public readonly record struct ClientPoint(int X, int Y);

/// <summary>
/// The stock hook's Touch gate (hook MAP section 2), run backwards: the gate maps a client point inside the 16:9
/// viewport of the render surface to the Canvas with <c>canvas = round(client * canvasSize / surfaceSize)</c>, y
/// measured from the surface's top, and drops a press outside that viewport.
/// </summary>
public sealed class TouchGate
{
	public TouchGate(int surfaceWidth, int surfaceHeight, int width, int height, bool preserveAspect)
	{
		SurfaceWidth = surfaceWidth;
		SurfaceHeight = surfaceHeight;
		Width = width;
		Height = height;
		PreserveAspect = preserveAspect;
	}

	/// <summary>The render surface, <c>[gfx] width</c> x <c>height</c>; 0 when the ini has none, and then the client size stands in.</summary>
	public int SurfaceWidth { get; }

	public int SurfaceHeight { get; }

	/// <summary>The Canvas size, <c>[touch] inputWidth</c> x <c>inputHeight</c>, 1920 x 1080 as the launch script writes it.</summary>
	public int Width { get; }

	public int Height { get; }

	public bool PreserveAspect { get; }

	public static TouchGate Read(string runtimeIniPath)
	{
		return Parse(File.Exists(runtimeIniPath) ? File.ReadAllText(runtimeIniPath) : "");
	}

	public static TouchGate Parse(string runtimeIniText)
	{
		Dictionary<string, Dictionary<string, string>> ini = IniText.Parse(runtimeIniText);
		int width = ini.Int("touch", "inputWidth", 1920);
		int height = ini.Int("touch", "inputHeight", 1080);
		return new TouchGate(Math.Max(ini.Int("gfx", "width", 0), 0), Math.Max(ini.Int("gfx", "height", 0), 0), (width > 0) ? width : 1920, (height > 0) ? height : 1080, ini.Int("gfx", "preserveAspect", 1) != 0);
	}

	/// <summary>
	/// The client point the stock hook turns into this Canvas point, within one Canvas pixel where the surface is
	/// smaller than the Canvas; null when that point is outside the 16:9 viewport, where the hook drops a press.
	/// </summary>
	public ClientPoint? ToClient(int x, int y, int clientWidth, int clientHeight)
	{
		int surfaceWidth = (SurfaceWidth > 0) ? SurfaceWidth : clientWidth;
		int surfaceHeight = (SurfaceHeight > 0) ? SurfaceHeight : clientHeight;
		if (x < 0 || x >= Width || y < 0 || y >= Height || surfaceWidth <= 0 || surfaceHeight <= 0)
		{
			return null;
		}
		int clientX = Nearest(x, surfaceWidth, Width);
		int clientY = Nearest(y, surfaceHeight, Height) - (surfaceHeight - clientHeight);
		long viewportWidth = surfaceWidth;
		long viewportHeight = surfaceHeight;
		if (PreserveAspect)
		{
			long wide = (long)surfaceWidth * Height;
			long tall = (long)surfaceHeight * Width;
			if (wide < tall)
			{
				viewportHeight = wide / Width;
			}
			else if (wide > tall)
			{
				viewportWidth = tall / Height;
			}
		}
		viewportWidth &= ~1L;
		viewportHeight &= ~1L;
		long left = (surfaceWidth - viewportWidth) / 2;
		long top = clientHeight - (surfaceHeight - viewportHeight) / 2 - viewportHeight;
		if (clientX < left || clientX >= left + viewportWidth || clientY < top || clientY >= top + viewportHeight || clientX >= clientWidth || clientY < 0 || clientY >= clientHeight)
		{
			return null;
		}
		return new ClientPoint(clientX, clientY);
	}

	/// <summary>The surface pixel the hook's <c>((p * 100 * canvas) + surface * 50) / (surface * 100)</c> takes to <paramref name="target"/>, or the nearest.</summary>
	private static int Nearest(int target, int surface, int canvas)
	{
		int guess = (int)((2L * target * surface + canvas) / (2L * canvas));
		foreach (int candidate in new int[3] { guess, guess - 1, guess + 1 })
		{
			if (candidate >= 0 && (candidate * 100L * canvas + surface * 50L) / (surface * 100L) == target)
			{
				return candidate;
			}
		}
		return guess;
	}
}
