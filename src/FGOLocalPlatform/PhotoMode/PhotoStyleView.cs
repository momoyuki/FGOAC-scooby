using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Windows;
using System.Windows.Controls;

namespace FGOLocalPlatform;

public sealed class PhotoStyleView : StackPanel, IDisposable
{
	private readonly PhotoBackdropView backdrop = new PhotoBackdropView();

	private MemoryMappedFile? map;

	private MemoryMappedViewAccessor? ipc;

	private int? pid;

	private int sequence;

	private readonly CheckBox shading = new CheckBox
	{
		Content = "Custom Character Shading (every toon-shaded character)",
		Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
	};

	private readonly CheckBox lines = new CheckBox
	{
		Content = "Custom Outlines (whole picture)",
		Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
	};

	private readonly Slider[] values = new Slider[9];

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap
	};

	public PhotoBackdropView Backdrop => backdrop;

	public PhotoStyleView()
	{
		base.Children.Add(backdrop);
		base.Children.Add(new TextBlock
		{
			Text = "Shading and Outlines",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 14.0, 0.0, 6.0)
		});
		base.Children.Add(new TextBlock
		{
			Text = "Applies in photo mode only. Shading changes the toon-shaded materials; outlines are drawn over the whole picture, so they cannot be set for one character.",
			TextWrapping = TextWrapping.Wrap
		});
		base.Children.Add(shading);
		string[] array = new string[9] { "Light Offset", "Shadow Edge", "Ambient Light", "Rim Light", "Outline Width", "Outline Strength", "Outline Red", "Outline Green", "Outline Blue" };
		for (int i = 0; i < 9; i++)
		{
			if (i == 4)
			{
				base.Children.Add(lines);
			}
			Grid grid = new Grid
			{
				Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(112.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(88.0)
			});
			grid.Children.Add(new TextBlock
			{
				Text = array[i]
			});
			Slider slider = new Slider
			{
				Minimum = ((i < 2) ? (-1.0) : ((i == 4) ? 0.1 : 0.0)),
				Maximum = ((i < 2) ? 1 : ((i >= 5) ? 1 : 4))
			};
			Slider slider2 = slider;
			bool flag = (uint)(i - 2) <= 3u;
			slider2.Value = (flag ? 1 : 0);
			slider.SmallChange = 0.01;
			Slider slider3 = slider;
			values[i] = slider3;
			Grid.SetColumn(slider3, 1);
			grid.Children.Add(slider3);
			TextBox element = PhotoNumberInput.Create(slider3);
			Grid.SetColumn(element, 2);
			grid.Children.Add(element);
			base.Children.Add(grid);
			slider3.ValueChanged += delegate
			{
				Write();
			};
		}
		CheckBox[] array2 = new CheckBox[2] { shading, lines };
		foreach (CheckBox obj in array2)
		{
			obj.Checked += delegate
			{
				Write();
			};
			obj.Unchecked += delegate
			{
				Write();
			};
		}
		Button button = new Button
		{
			Content = "Restore Original Shading and Outlines",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button.Click += delegate
		{
			shading.IsChecked = false;
			lines.IsChecked = false;
			for (int j = 0; j < 9; j++)
			{
				Slider slider4 = values[j];
				bool flag2 = (uint)(j - 2) <= 3u;
				slider4.Value = (flag2 ? 1 : 0);
			}
			Write();
		};
		base.Children.Add(button);
		base.Children.Add(status);
	}

	public void Refresh(int? gamePid, bool active)
	{
		if (pid != gamePid)
		{
			Dispose();
			pid = gamePid;
		}
		backdrop.Refresh(gamePid, active);
		base.IsEnabled = active;
		if (!active)
		{
			if (ipc != null)
			{
				ipc.Write(12L, 0);
				ipc.Write(52L, 0);
			}
			return;
		}
		try
		{
			if (ipc == null && gamePid.HasValue)
			{
				map = MemoryMappedFile.CreateOrOpen($"Local\\FGOLocalPhotoStyle2_{gamePid}", 72L);
				ipc = map.CreateViewAccessor(0L, 72L);
				ipc.Write(0L, 1397770054);
				ipc.Write(4L, 2);
			}
			Write();
			status.Text = ((ipc != null) ? $"Shading: {((ipc.ReadInt32(56L) == 1) ? "the renderer is connected" : "no renderer found yet")}; outlines: {((ipc.ReadInt32(60L) == 1) ? "the renderer is connected" : "no renderer found yet")}." : "Waiting for the renderer.");
		}
		catch (IOException)
		{
			status.Text = "The shading connection is not available.";
		}
	}

	private void Write()
	{
		if (ipc != null && base.IsEnabled)
		{
			ipc.Write(8L, ++sequence);
			ipc.Write(12L, (shading.IsChecked == true) ? 1 : 0);
			for (int i = 0; i < 9; i++)
			{
				ipc.Write(16 + i * 4, (float)values[i].Value);
			}
			ipc.Write(52L, (lines.IsChecked == true) ? 1 : 0);
			ipc.Write(8L, ++sequence);
		}
	}

	public void Dispose()
	{
		backdrop.Dispose();
		if (ipc != null)
		{
			ipc.Write(12L, 0);
			ipc.Write(52L, 0);
		}
		ipc?.Dispose();
		map?.Dispose();
		ipc = null;
		map = null;
	}
}
