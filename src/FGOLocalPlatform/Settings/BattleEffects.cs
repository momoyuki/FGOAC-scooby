using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// The Battle effects switches, kept in [effects] of two files in the App folder. HideNpScreen is hideNpScreen in
/// segatools.ini, which reaches the Add-on through the runtime ini, and is also written to hide_dark_mask in
/// graphics-effects.ini, which the stock hook reads beside the game; only hideNpScreen is read. HideWhiteFlash is
/// hideWhiteFlash in segatools.ini. A missing file or key, 0 or a value that is not a number is off. Opacity is the
/// stock hook's opacity in graphics-effects.ini, read as the hook reads it: missing is 100, outside 0-100 is 100; at
/// 100 the key is removed. A write sets only these keys, creating a missing file; every other key and section stays.
/// </summary>
public sealed record BattleEffects(bool HideNpScreen, bool HideWhiteFlash, int Opacity)
{
	private const string Section = "effects";

	public const int OpacityDefault = 100;

	public static BattleEffects Read(string appFolder)
	{
		int opacity = (int)GetPrivateProfileInt(Section, "opacity", OpacityDefault, GraphicsEffectsIni(appFolder));
		return new BattleEffects(GetPrivateProfileInt(Section, "hideNpScreen", 0, SegatoolsIni(appFolder)) != 0, GetPrivateProfileInt(Section, "hideWhiteFlash", 0, SegatoolsIni(appFolder)) != 0, (opacity >= 0 && opacity <= 100) ? opacity : OpacityDefault);
	}

	/// <summary>Writes the switches; throws <see cref="Win32Exception"/> when a file cannot be written.</summary>
	public void Write(string appFolder)
	{
		Set(GraphicsEffectsIni(appFolder), "hide_dark_mask", HideNpScreen);
		Set(SegatoolsIni(appFolder), "hideNpScreen", HideNpScreen);
		Set(SegatoolsIni(appFolder), "hideWhiteFlash", HideWhiteFlash);
		SetOpacity(GraphicsEffectsIni(appFolder));
		SetHisOpacity(Path.Combine(appFolder, "fgo-launcher.json"));
	}

	/// <summary>
	/// Sets hide_dark_mask in graphics-effects.ini from the saved hideNpScreen, after Cloud23333's launcher rewrote the
	/// file with it off; every other key stays. Throws <see cref="Win32Exception"/> when the file cannot be written.
	/// </summary>
	public static void ApplyNpScreen(string appFolder)
	{
		Set(GraphicsEffectsIni(appFolder), "hide_dark_mask", GetPrivateProfileInt(Section, "hideNpScreen", 0, SegatoolsIni(appFolder)) != 0);
	}

	/// <summary>
	/// graphics.effectOpacity in fgo-launcher.json, which Cloud23333's launcher writes back into graphics-effects.ini at
	/// each Play; every other key stays, and a missing settings file is not created.
	/// </summary>
	private void SetHisOpacity(string settingsPath)
	{
		if (!File.Exists(settingsPath) || JsonNode.Parse(File.ReadAllText(settingsPath)) is not JsonObject settings)
		{
			return;
		}
		if (settings["graphics"] is not JsonObject graphics)
		{
			graphics = new JsonObject();
			settings["graphics"] = graphics;
		}
		graphics["effectOpacity"] = Math.Clamp(Opacity, 0, 100);
		AtomicFile.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
	}

	private static string GraphicsEffectsIni(string appFolder)
	{
		return Path.Combine(appFolder, "graphics-effects.ini");
	}

	private static string SegatoolsIni(string appFolder)
	{
		return Path.Combine(appFolder, "segatools.ini");
	}

	private static void Set(string file, string key, bool on)
	{
		Set(file, key, on ? "1" : "0");
	}

	/// <summary>Writes the opacity key, clamped to 0-100; at the default it is removed instead, if present.</summary>
	private void SetOpacity(string file)
	{
		int clamped = Math.Clamp(Opacity, 0, 100);
		Set(file, "opacity", (clamped == OpacityDefault) ? null : clamped.ToString(CultureInfo.InvariantCulture));
	}

	private static void Set(string file, string key, string? value)
	{
		if (!WritePrivateProfileString(Section, key, value, file))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern uint GetPrivateProfileInt(string section, string key, int defaultValue, string file);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool WritePrivateProfileString(string section, string key, string? value, string file);
}
