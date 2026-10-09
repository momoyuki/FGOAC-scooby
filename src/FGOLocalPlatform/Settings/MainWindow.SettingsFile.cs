using System;
using System.Collections;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private ComboBox InputModeComboBox => ControlsPanel.InputModeSelector;

	private static string? SelectedTag(ComboBox comboBox)
	{
		return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
	}

	private static void SelectTag(ComboBox comboBox, string value)
	{
		foreach (object item in (IEnumerable)comboBox.Items)
		{
			if (item is ComboBoxItem { Tag: var tag } comboBoxItem && string.Equals(tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
			{
				comboBox.SelectedItem = comboBoxItem;
				return;
			}
		}
		comboBox.SelectedIndex = 0;
	}

	private void LoadLauncherSettings()
	{
		string path = Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");
		string value = "exclusive";
		string text = "keyboard";
		int num = 60;
		int num2 = 1920;
		int num3 = 1080;
		string text2 = "";
		string device = "";
		PcPortSwitches pcPort = PcPortSwitches.Defaults;
		JsonObject? widescreen = null;
		JsonObject? graphics = null;
		try
		{
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject jsonObject)
			{
				object obj = jsonObject["displayMode"]?.GetValue<string>();
				if (obj == null)
				{
					JsonNode? jsonNode = jsonObject["windowed"];
					obj = ((jsonNode != null && jsonNode.GetValue<bool>()) ? "windowed" : "exclusive");
				}
				value = (string)obj;
				text = jsonObject["inputMode"]?.GetValue<string>() ?? text;
				num = jsonObject["targetFps"]?.GetValue<int>() ?? num;
				num2 = jsonObject["resolutionWidth"]?.GetValue<int>() ?? num2;
				num3 = jsonObject["resolutionHeight"]?.GetValue<int>() ?? num3;
				text2 = jsonObject["aspectRatio"]?.GetValue<string>() ?? "";
				device = jsonObject["monitorDevice"]?.GetValue<string>() ?? "";
				ChineseEnabledCheckBox.IsChecked = GameLanguage.IsEnglish(jsonObject);
				ControlsPanel.OperatorMenuKeysSwitch.IsChecked = jsonObject["operatorMenuKeys"]?.GetValue<bool>() ?? false;
				pcPort = PcPortSwitches.Read(jsonObject);
				ControlsPanel.CursorLockSwitch.IsChecked = (jsonObject["cursor"] as JsonObject)?["confine"] is JsonValue confine && confine.TryGetValue(out bool locked) && locked;
				widescreen = jsonObject["widescreen"] as JsonObject;
				RefreshGpuCompatSection();
				if (jsonObject["graphics"] is JsonObject jsonObject2)
				{
					graphics = jsonObject2;
					SelectTag(SmaaComboBox, (jsonObject2["smaa"]?.GetValue<int>() ?? 0).ToString());
					SelectTag(RenderScaleComboBox, (jsonObject2["renderScale"]?.GetValue<int>() ?? 100).ToString());
					SelectTag(ShadowResolutionComboBox, (jsonObject2["shadowResolution"]?.GetValue<int>() ?? 0).ToString());
					HideTargetLinesCheckBox.IsChecked = jsonObject2["hideTargetLines"]?.GetValue<bool>() ?? false;
					DamageNumberScaleSlider.Value = Math.Clamp(jsonObject2["damageNumberScale"]?.GetValue<double>() ?? 100.0, 0.0, 200.0);
					DamageTextureScaleSlider.Value = Math.Clamp(jsonObject2["damageTextureScale"]?.GetValue<double>() ?? 100.0, 0.0, 200.0);
					DamageNumberOpacitySlider.Value = Math.Clamp(jsonObject2["damageNumberOpacity"]?.GetValue<double>() ?? 100.0, 0.0, 100.0);
					DamageTextureOpacitySlider.Value = Math.Clamp(jsonObject2["damageTextureOpacity"]?.GetValue<double>() ?? 100.0, 0.0, 100.0);
					SelectTag(AnisotropyComboBox, (jsonObject2["anisotropy"]?.GetValue<int>() ?? 16).ToString());
					MotionBlurCheckBox.IsChecked = jsonObject2["motionBlur"]?.GetValue<bool>() ?? false;
					DepthOfFieldCheckBox.IsChecked = jsonObject2["depthOfField"]?.GetValue<bool>() ?? true;
					BloomCheckBox.IsChecked = jsonObject2["bloom"]?.GetValue<bool>() ?? true;
					HideUiCheckBox.IsChecked = jsonObject2["hideUi"]?.GetValue<bool>() ?? false;
					DisableCameraShakeCheckBox.IsChecked = jsonObject2["disableCameraShake"]?.GetValue<bool>() ?? false;
					HideCabinetHudCheckBox.IsChecked = jsonObject2["hideCabinetHud"]?.GetValue<bool>() ?? false;
					hideUiVirtualKey = jsonObject2["hideUiKey"]?.GetValue<int>() ?? 121;
					if (hideUiVirtualKey < 8 || hideUiVirtualKey > 254)
					{
						hideUiVirtualKey = 121;
					}
					UpdateHideUiKeyLabel();
				}
			}
		}
		catch (Exception ex)
		{
			RuntimeStatusText.Text = "Could not read the display settings: " + ex.Message;
		}
		PcPlayPanel.Switches = pcPort;
		ShowWidescreen(widescreen);
		ShowSceneGraphics(graphics);
		ShowFrameSync(graphics);
		SelectTag(DisplayModeComboBox, value);
		PopulateMonitors(device);
		ShowGpuPreference();
		SelectTag(InputModeComboBox, text);
		ControlsPanel.RestorePreferredInputMode(text);
		SelectTag(FrameRateComboBox, num.ToString());
		bool game1100 = GameBuild.Is1100(Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..")));
		FrameRateHelpText.Text = FrameRateHelp(game1100, graphics);
		GameLanguageHelpText.Text = GameLanguage.Help(game1100);
		string text3 = ((text2 == "custom") ? "custom" : (ResolutionPresets.ContainsKey(text2) ? text2 : DetectAspectRatio(num2, num3)));
		updatingResolutionOptions = true;
		SelectTag(AspectRatioComboBox, text3);
		updatingResolutionOptions = false;
		PopulateResolutionOptions(text3, $"{num2}x{num3}");
	}

	/// <summary>
	/// What the frame rate pick does: on 11.00 the launch script holds the game at 60; on Cloud23333's update his launch
	/// script applies the pick while graphics.highFpsV2 is true, which his updater sets.
	/// </summary>
	internal static string FrameRateHelp(bool game1100, JsonObject? graphics)
	{
		if (game1100)
		{
			return "On this game version the game runs at 60 FPS whatever is picked: higher rates break the menus and touch.";
		}
		return (graphics?["highFpsV2"] is JsonValue value && value.TryGetValue(out bool on) && on) ? "The game runs at the rate picked." : "The game runs at 60 FPS whatever is picked while High frame rate timing below is off.";
	}

	private void ControlsPanel_OnSaveRequested(object? sender, EventArgs e)
	{
		if (SaveLauncherSettings(out string _, out int _, out int _, out string _, out int _))
		{
			HoldStatus("Control settings saved - they take effect the next time the game starts.");
		}
	}

	private void PcPlayPanel_OnSaveRequested(object? sender, EventArgs e)
	{
		if (SaveLauncherSettings(out string _, out int _, out int _, out string _, out int _))
		{
			HoldStatus("PC play settings saved - they take effect the next time the game starts.");
		}
	}

	private bool SaveLauncherSettings(out string displayMode, out int width, out int height, out string inputMode, out int targetFps)
	{
		displayMode = SelectedTag(DisplayModeComboBox) ?? "exclusive";
		inputMode = SelectedTag(InputModeComboBox) ?? "keyboard";
		int result;
		bool flag = int.TryParse(SelectedTag(FrameRateComboBox), out result);
		if (flag)
		{
			bool flag2;
			switch (result)
			{
			case 60:
			case 90:
			case 120:
			case 144:
			case 240:
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		targetFps = (flag ? result : 60);
		width = 0;
		height = 0;
		if (!int.TryParse(CustomWidthTextBox.Text, out width) || !int.TryParse(CustomHeightTextBox.Text, out height) || width < 480 || width > 7680 || height < 480 || height > 7680)
		{
			ThemedMessageBox.Show("Enter whole numbers from 480 to 7680 in the width and height boxes, for example 1920x1080, 720x1280 or 1080x2560.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return false;
		}
		try
		{
			string text = Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");
			if (!ControlsPanel.SaveBindings())
			{
				return false;
			}
			JsonObject jsonObject = ((File.Exists(text) && JsonNode.Parse(File.ReadAllText(text)) is JsonObject jsonObject2) ? jsonObject2 : new JsonObject());
			jsonObject["displayMode"] = displayMode;
			jsonObject["monitorDevice"] = SelectedTag(MonitorComboBox) ?? "";
			jsonObject["resolutionWidth"] = width;
			jsonObject["resolutionHeight"] = height;
			jsonObject["aspectRatio"] = SelectedTag(AspectRatioComboBox) ?? DetectAspectRatio(width, height);
			jsonObject["windowed"] = displayMode != "exclusive";
			jsonObject["inputMode"] = inputMode;
			if (ControlsPanel.HisHookOffered)
			{
				jsonObject["controlProfile"] = ControlsPanel.ControlProfile;
			}
			jsonObject["showControlGuides"] = false;
			jsonObject["targetFps"] = targetFps;
			GameLanguage.Write(jsonObject, ChineseEnabledCheckBox.IsChecked == true, Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..")));
			jsonObject["operatorMenuKeys"] = ControlsPanel.OperatorMenuKeysSwitch.IsChecked == true;
			jsonObject["gpuCompat"] = PickedGpuLayer != GpuCompat.Choice.Off;
			PcPlayPanel.Switches.Write(jsonObject);
			JsonObject cursor = (jsonObject["cursor"] as JsonObject) ?? new JsonObject();
			cursor["confine"] = ControlsPanel.CursorLockSwitch.IsChecked == true;
			if (jsonObject["cursor"] != cursor)
			{
				jsonObject["cursor"] = cursor;
			}
			WriteWidescreen(jsonObject);
			JsonObject jsonObject3 = (jsonObject["graphics"] as JsonObject) ?? new JsonObject();
			jsonObject3["smaa"] = (int.TryParse(SelectedTag(SmaaComboBox), out var result2) ? result2 : 0);
			JsonObject jsonObject4 = jsonObject3;
			bool flag2 = int.TryParse(SelectedTag(RenderScaleComboBox), out var result3);
			if (flag2)
			{
				bool flag3;
				switch (result3)
				{
				case 100:
				case 125:
				case 150:
				case 200:
					flag3 = true;
					break;
				default:
					flag3 = false;
					break;
				}
				flag2 = flag3;
			}
			jsonObject4["renderScale"] = (flag2 ? result3 : 100);
			jsonObject3["anisotropy"] = (int.TryParse(SelectedTag(AnisotropyComboBox), out var result4) ? result4 : 16);
			jsonObject4 = jsonObject3;
			flag2 = int.TryParse(SelectedTag(ShadowResolutionComboBox), out var result5);
			if (flag2)
			{
				bool flag3 = ((result5 == 1024 || result5 == 2048 || result5 == 4096) ? true : false);
				flag2 = flag3;
			}
			jsonObject4["shadowResolution"] = (flag2 ? result5 : 0);
			jsonObject3["motionBlur"] = MotionBlurCheckBox.IsChecked == true;
			jsonObject3["depthOfField"] = DepthOfFieldCheckBox.IsChecked == true;
			jsonObject3["bloom"] = BloomCheckBox.IsChecked == true;
			jsonObject3["hideUi"] = HideUiCheckBox.IsChecked == true;
			jsonObject3["disableCameraShake"] = DisableCameraShakeCheckBox.IsChecked == true;
			jsonObject3["hideCabinetHud"] = HideCabinetHudCheckBox.IsChecked == true;
			jsonObject3["hideUiKey"] = hideUiVirtualKey;
			jsonObject3["hideTargetLines"] = HideTargetLinesCheckBox.IsChecked == true;
			jsonObject3["damageNumberScale"] = Math.Round(DamageNumberScaleSlider.Value, 1);
			jsonObject3["damageTextureScale"] = Math.Round(DamageTextureScaleSlider.Value, 1);
			jsonObject3["damageNumberOpacity"] = Math.Round(DamageNumberOpacitySlider.Value, 1);
			jsonObject3["damageTextureOpacity"] = Math.Round(DamageTextureOpacitySlider.Value, 1);
			WriteSceneGraphics(jsonObject3);
			WriteFrameSync(jsonObject3);
			WriteGpuCard(jsonObject3, GpuCompat.HisBuild && PickedGpuLayer == GpuCompat.Choice.Fluphus);
			if (jsonObject["graphics"] != jsonObject3)
			{
				jsonObject["graphics"] = jsonObject3;
			}
			jsonObject.Remove("startDeckReaderUI");
			AtomicFile.WriteAllText(text, jsonObject.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}) + Environment.NewLine);
			return true;
		}
		catch (Exception ex)
		{
			ThemedMessageBox.Show("Could not save the launch settings:\n" + ex.Message, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Hand);
			return false;
		}
	}
}
