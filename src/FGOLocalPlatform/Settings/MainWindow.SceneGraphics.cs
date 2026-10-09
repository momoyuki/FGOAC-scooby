using System.IO;
using System.Text.Json.Nodes;
using System.Windows;

namespace FGOLocalPlatform;

/// <summary>The Display card's scene settings and treasure chests: Cloud23333's keys, shown on his build only.</summary>
public partial class MainWindow
{
	private static bool SceneGraphicsOffered => SceneGraphics.Offered(Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..")));

	private void ShowSceneGraphics(JsonObject? graphics)
	{
		Visibility shown = SceneGraphicsOffered ? Visibility.Visible : Visibility.Collapsed;
		SceneGraphicsPanel.Visibility = shown;
		TreasureChestPanel.Visibility = shown;
		SceneGraphics scene = SceneGraphics.Read(graphics);
		VolumeFogCheckBox.IsChecked = scene.VolumeFog;
		ReflectionsCheckBox.IsChecked = scene.Reflections;
		SelectTag(DrawDistanceComboBox, scene.ViewDistance.ToString(System.Globalization.CultureInfo.InvariantCulture));
		UltraLowCheckBox.IsChecked = scene.UltraLow;
		TreasureScaleSlider.Value = scene.TreasureScale;
		TreasureOpacitySlider.Value = scene.TreasureOpacity;
		ApplyUltraLow();
	}

	/// <summary>On his build, writes the scene settings into <paramref name="graphics"/> and the draw distance into graphics-effects.ini.</summary>
	private void WriteSceneGraphics(JsonObject graphics)
	{
		if (!SceneGraphicsOffered)
		{
			return;
		}
		SceneGraphics scene = new SceneGraphics(VolumeFogCheckBox.IsChecked == true, ReflectionsCheckBox.IsChecked == true, UltraLowCheckBox.IsChecked == true, int.TryParse(SelectedTag(DrawDistanceComboBox), out int distance) ? distance : 100, TreasureScaleSlider.Value, TreasureOpacitySlider.Value);
		scene.Write(graphics);
		scene.WriteDrawDistance(GamePaths.GameRoot);
	}

	private void UltraLow_OnChanged(object sender, RoutedEventArgs e)
	{
		ApplyUltraLow();
	}

	/// <summary>Ultra-low quality fixes every quality setting at its lowest, so they are greyed while it is on, as in his launcher.</summary>
	private void ApplyUltraLow()
	{
		bool free = UltraLowCheckBox.IsChecked != true;
		foreach (UIElement control in new UIElement[10] { SmaaComboBox, AnisotropyComboBox, RenderScaleComboBox, MotionBlurCheckBox, DepthOfFieldCheckBox, BloomCheckBox, VolumeFogCheckBox, ReflectionsCheckBox, ShadowResolutionComboBox, DrawDistanceComboBox })
		{
			control.IsEnabled = free;
		}
	}

	private void ResetTreasureChests_OnClick(object sender, RoutedEventArgs e)
	{
		TreasureScaleSlider.Value = 100.0;
		TreasureOpacitySlider.Value = 100.0;
	}
}
