using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FGOLocalPlatform;

public sealed class PhotoModelView : StackPanel, IDisposable
{
	private sealed record Saved(double[] Offset, double[] Rotation, bool Weapons, bool Head, bool ManualHead, double[] HeadAngles, int[] Modes, int[] States, double[] Eyes, double Scale, double[] Mouth, Dictionary<int, MorphSaved> Morphs);

	private sealed record MorphSaved(bool Enabled, int[] Targets, double[] Weights);

	private sealed record MorphGroup(int Index, string Name, List<(int Target, string Name)> Shapes);

	private readonly CheckBox[] eyeFollow = new CheckBox[2];

	private readonly CheckBox[] eyeManual = new CheckBox[2];

	private readonly Slider[,] eyeValues = new Slider[2, 4];

	private readonly CheckBox[] eyeBlink = new CheckBox[2];

	private readonly Slider[] eyeClosure = new Slider[2];

	private readonly TextBlock[] eyeStatus = new TextBlock[2];

	private readonly Slider modelScale = new Slider
	{
		Minimum = 0.1,
		Maximum = 5.0,
		Value = 1.0,
		SmallChange = 0.01
	};

	private readonly PhotoFaceView selection;

	private readonly StackPanel newer = new StackPanel();

	private bool extended;

	private readonly PhotoStyleView styleView = new PhotoStyleView();

	private readonly Slider[] offsets = new Slider[3];

	private readonly Slider[] rotations = new Slider[3];

	private readonly Slider[] headAngles = new Slider[3];

	private readonly CheckBox manualHead = new CheckBox
	{
		Content = "Custom Head Rotation",
		Margin = new Thickness(0.0, 10.0, 0.0, 8.0)
	};

	private readonly CheckBox weapons = new CheckBox
	{
		Content = "Show Weapons",
		Foreground = Brushes.White,
		IsChecked = true,
		Margin = new Thickness(0.0, 10.0, 0.0, 10.0)
	};

	private readonly CheckBox head = new CheckBox
	{
		Content = "Head Follows Camera",
		Foreground = Brushes.White,
		Margin = new Thickness(0.0, 10.0, 0.0, 10.0)
	};

	private readonly ComboBox weaponChoice = new ComboBox
	{
		Margin = new Thickness(0.0, 6.0, 0.0, 6.0),
		MaxDropDownHeight = 220.0
	};

	private readonly ComboBox weaponPosition = new ComboBox
	{
		Margin = new Thickness(0.0, 6.0, 0.0, 6.0),
		MaxDropDownHeight = 220.0
	};

	private readonly ComboBox weaponMode = new ComboBox
	{
		Margin = new Thickness(0.0, 6.0, 0.0, 6.0)
	};

	private readonly int[] weaponModes = new int[10];

	private readonly int[] weaponStates = new int[10];

	private bool syncingWeapons;

	private bool loadingActor;

	private readonly Dictionary<ulong, Saved> saved = new Dictionary<ulong, Saved>();

	private string weaponSignature = "";

	private int weaponMask = -1;

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap
	};

	private MemoryMappedFile? mapping;

	private MemoryMappedViewAccessor? ipc;

	private int? pid;

	private ulong actor;

	private bool dirty = true;

	private readonly Dictionary<int, MorphSaved> morphSettings = new Dictionary<int, MorphSaved>();

	private readonly List<MorphGroup> morphGroups = new List<MorphGroup>();

	private readonly ComboBox morphGroupChoice = new ComboBox
	{
		Margin = new Thickness(0.0, 4.0, 0.0, 4.0),
		MaxDropDownHeight = 240.0
	};

	private readonly CheckBox morphEnabled = new CheckBox
	{
		Content = "Customize This Part",
		Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
	};

	private readonly List<ComboBox> morphChoices = new List<ComboBox>();

	private readonly StackPanel morphRows = new StackPanel();

	private readonly Button morphAdd = new Button
	{
		Content = "Add Shape",
		Margin = new Thickness(0.0, 6.0, 0.0, 6.0)
	};

	private readonly List<Slider> morphWeights = new List<Slider>();

	private readonly TextBlock morphStatus = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap
	};

	private bool syncingMorph;

	private int morphSequence = -1;

	private readonly CheckBox mouthEnabled = new CheckBox
	{
		Content = "Custom Mouth",
		Margin = new Thickness(0.0, 6.0, 0.0, 4.0)
	};

	private readonly ComboBox mouthShape = new ComboBox
	{
		Margin = new Thickness(0.0, 4.0, 0.0, 4.0),
		MaxDropDownHeight = 240.0
	};

	private readonly TabControl mouthMode = new TabControl
	{
		Margin = new Thickness(0.0, 4.0, 0.0, 8.0)
	};

	private readonly Slider[] mouthWeights = new Slider[5];

	private readonly Grid[] mouthRows = new Grid[5];

	private readonly TextBlock mouthStatus = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap
	};

	private int mouthSelection;

	private int mouthAvailable = -1;

	private bool syncingMouth;

	private static readonly (int Id, string Name)[] MouthShapes = new(int, string)[12]
	{
		(0, "A"),
		(1, "I"),
		(2, "U"),
		(3, "E"),
		(4, "O"),
		(12, "Smile"),
		(13, "Angry"),
		(14, "Sad"),
		(15, "Shy"),
		(16, "Shout"),
		(17, "Pain"),
		(21, "Calm")
	};

	private static readonly int[] MouthVowels = new int[5] { 0, 3, 1, 4, 2 };

	public PhotoBackdropView Backdrop => styleView.Backdrop;

	private int SelectedMorphGroup
	{
		get
		{
			if (morphGroupChoice.SelectedItem is ComboBoxItem { Tag: var tag } && tag is int)
			{
				return (int)tag;
			}
			return -1;
		}
	}

	private void BuildEyeControls()
	{
		newer.Children.Add(new TextBlock
		{
			Text = "Eye Gaze",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 14.0, 0.0, 4.0)
		});
		newer.Children.Add(new TextBlock
		{
			Text = "Each eye is set on its own. Horizontal and Vertical are degrees. A strength from 0 to 1 keeps the eye inside its natural limits; above 1 lifts them and the eye looks straight at its target.",
			TextWrapping = TextWrapping.Wrap
		});
		newer.Children.Add(new TextBlock
		{
			Text = "Iris size is set per eye too; 1 is the original size.",
			TextWrapping = TextWrapping.Wrap
		});
		for (int i = 0; i < 2; i++)
		{
			eyeFollow[i] = new CheckBox
			{
				Content = ((i == 0) ? "Left" : "Right") + " Eye Follows Camera",
				Margin = new Thickness(0.0, 10.0, 0.0, 4.0)
			};
			eyeManual[i] = new CheckBox
			{
				Content = "Custom Gaze Offset",
				Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
			};
			CheckBox[] array = new CheckBox[2]
			{
				eyeFollow[i],
				eyeManual[i]
			};
			foreach (CheckBox checkBox in array)
			{
				checkBox.Checked += delegate
				{
					dirty = true;
					Publish();
				};
				checkBox.Unchecked += delegate
				{
					dirty = true;
					Publish();
				};
				newer.Children.Add(checkBox);
			}
			for (int num = 0; num < 4; num++)
			{
				Grid grid = new Grid
				{
					Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
				};
				grid.ColumnDefinitions.Add(new ColumnDefinition
				{
					Width = new GridLength(80.0)
				});
				grid.ColumnDefinitions.Add(new ColumnDefinition());
				grid.ColumnDefinitions.Add(new ColumnDefinition
				{
					Width = new GridLength(90.0)
				});
				grid.Children.Add(new TextBlock
				{
					Text = (new string[4] { "Horizontal", "Vertical", "Strength", "Iris Size" })[num]
				});
				Slider slider = new Slider
				{
					Minimum = ((num == 3) ? 0.1 : ((double)((num != 2) ? (-90) : 0))),
					Maximum = ((num >= 2) ? 4 : 90),
					Value = ((num >= 2) ? 1 : 0),
					SmallChange = ((num >= 2) ? 0.01 : 1.0)
				};
				eyeValues[i, num] = slider;
				Grid.SetColumn(slider, 1);
				grid.Children.Add(slider);
				TextBox element = PhotoNumberInput.Create(slider, 1.0, null, num == 2, expandMinimum: false);
				Grid.SetColumn(element, 2);
				grid.Children.Add(element);
				newer.Children.Add(grid);
				slider.ValueChanged += delegate
				{
					dirty = true;
					Publish();
				};
			}
			eyeBlink[i] = new CheckBox
			{
				Content = "Custom Eyelid",
				Margin = new Thickness(0.0, 6.0, 0.0, 4.0)
			};
			eyeBlink[i].Checked += delegate
			{
				dirty = true;
				Publish();
			};
			eyeBlink[i].Unchecked += delegate
			{
				dirty = true;
				Publish();
			};
			newer.Children.Add(eyeBlink[i]);
			Grid grid2 = new Grid
			{
				Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
			};
			grid2.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(80.0)
			});
			grid2.ColumnDefinitions.Add(new ColumnDefinition());
			grid2.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(90.0)
			});
			grid2.Children.Add(new TextBlock
			{
				Text = "Closed"
			});
			eyeClosure[i] = new Slider
			{
				Minimum = 0.0,
				Maximum = 1.0,
				Value = 0.0,
				SmallChange = 0.01
			};
			Grid.SetColumn(eyeClosure[i], 1);
			grid2.Children.Add(eyeClosure[i]);
			TextBox element2 = PhotoNumberInput.Create(eyeClosure[i]);
			Grid.SetColumn(element2, 2);
			grid2.Children.Add(element2);
			newer.Children.Add(grid2);
			eyeClosure[i].ValueChanged += delegate
			{
				dirty = true;
				Publish();
			};
			newer.Children.Add(new TextBlock
			{
				Text = "0 is open and 1 is closed, on top of the current expression; untick to give the eye back to the expression.",
				TextWrapping = TextWrapping.Wrap
			});
			eyeStatus[i] = new TextBlock
			{
				TextWrapping = TextWrapping.Wrap
			};
			newer.Children.Add(eyeStatus[i]);
		}
		Button button = new Button
		{
			Content = "Restore Original Eyes",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button.Click += delegate
		{
			bool flag = loadingActor;
			loadingActor = true;
			try
			{
				LoadEyes(null);
			}
			finally
			{
				loadingActor = flag;
			}
			dirty = true;
			Publish();
		};
		newer.Children.Add(button);
	}

	private double[] SaveEyes()
	{
		double[] array = new double[16];
		for (int i = 0; i < 2; i++)
		{
			array[i * 8] = ((eyeFollow[i].IsChecked == true) ? 1 : 0);
			array[i * 8 + 1] = ((eyeManual[i].IsChecked == true) ? 1 : 0);
			for (int j = 0; j < 4; j++)
			{
				array[i * 8 + 2 + j] = eyeValues[i, j].Value;
			}
			array[i * 8 + 6] = ((eyeBlink[i].IsChecked == true) ? 1 : 0);
			array[i * 8 + 7] = eyeClosure[i].Value;
		}
		return array;
	}

	private void LoadEyes(double[]? v)
	{
		for (int i = 0; i < 2; i++)
		{
			eyeBlink[i].IsChecked = v != null && v[i * 8 + 6] == 1.0;
			eyeClosure[i].Value = ((v != null) ? v[i * 8 + 7] : 0.0);
			eyeFollow[i].IsChecked = v != null && v[i * 8] == 1.0;
			eyeManual[i].IsChecked = v != null && v[i * 8 + 1] == 1.0;
			for (int j = 0; j < 4; j++)
			{
				double num = ((v != null) ? v[i * 8 + 2 + j] : ((double)((j >= 2) ? 1 : 0)));
				if (j == 2)
				{
					PhotoNumberInput.SetExpandedValue(eyeValues[i, j], Math.Max(0.0, num));
				}
				else
				{
					eyeValues[i, j].Value = num;
				}
			}
		}
	}

	private void WriteEyes()
	{
		if (ipc == null)
		{
			return;
		}
		for (int i = 0; i < 2; i++)
		{
			ipc.Write(912 + i * 8, (eyeBlink[i].IsChecked == true) ? 1 : 0);
			ipc.Write(916 + i * 8, (float)eyeClosure[i].Value);
			int num = 852 + i * 20;
			ipc.Write(num, (eyeFollow[i].IsChecked == true) ? 1 : 0);
			ipc.Write(num + 4, (eyeManual[i].IsChecked == true) ? 1 : 0);
			for (int j = 0; j < 4; j++)
			{
				if (j < 3)
				{
					ipc.Write(num + 8 + j * 4, (float)eyeValues[i, j].Value);
				}
				else
				{
					ipc.Write(904 + i * 4, (float)eyeValues[i, j].Value);
				}
			}
		}
	}

	private void RefreshEyeStatus()
	{
		if (ipc == null)
		{
			return;
		}
		for (int i = 0; i < 2; i++)
		{
			TextBlock textBlock = eyeStatus[i];
			textBlock.Text = ipc.ReadInt32(892 + i * 4) switch
			{
				1 => "Eye gaze applied",
				-4 => "This model has no matching eye bones or eye limits.",
				-5 => "The game hook's eye control is not running - restart the game.",
				_ => "Using the original eye motion",
			};
			if (eyeBlink[i].IsChecked == true)
			{
				TextBlock obj = eyeStatus[i];
				obj.Text = obj.Text + "\n" + ((ipc.ReadInt32(928 + i * 4) == 1) ? "Eyelid applied" : "This model's eyelids cannot be set.");
			}
		}
	}

	private static int Choice(ComboBox box)
	{
		if (box.SelectedItem is ComboBoxItem { Tag: var tag } && tag is int)
		{
			return (int)tag;
		}
		return 0;
	}

	public PhotoModelView(PhotoFaceView selection)
	{
		this.selection = selection;
		base.Children.Add(new TextBlock
		{
			Text = "Model and Weapons",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		base.Children.Add(new TextBlock
		{
			Text = "Moves the character picked in Face Animation above along the scene axes. The camera does not move.",
			TextWrapping = TextWrapping.Wrap
		});
		for (int i = 0; i < 3; i++)
		{
			Grid grid = new Grid
			{
				Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(65.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(90.0)
			});
			grid.Children.Add(new TextBlock
			{
				Text = (new string[3] { "X Side", "Y Height", "Z Depth" })[i]
			});
			Slider slider = PhotoNumberInput.CreatePositionSlider();
			offsets[i] = slider;
			Grid.SetColumn(slider, 1);
			grid.Children.Add(slider);
			TextBox element = PhotoNumberInput.Create(slider, 1.0, null, expandRange: true);
			Grid.SetColumn(element, 2);
			grid.Children.Add(element);
			slider.ValueChanged += delegate
			{
				dirty = true;
				Publish();
			};
			base.Children.Add(grid);
		}
		Button button = new Button
		{
			Content = "Reset Position",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button.Click += delegate
		{
			Slider[] array2 = offsets;
			for (int j = 0; j < array2.Length; j++)
			{
				array2[j].Value = 0.0;
			}
			dirty = true;
			Publish();
		};
		base.Children.Add(button);
		base.Children.Add(new TextBlock
		{
			Text = "Model Rotation (degrees)",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 10.0, 0.0, 4.0)
		});
		base.Children.Add(new TextBlock
		{
			Text = "Turns the model about its own position on the scene X, Y and Z axes; 0° is the original facing.",
			TextWrapping = TextWrapping.Wrap
		});
		for (int num = 0; num < 3; num++)
		{
			Grid grid2 = new Grid
			{
				Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
			};
			grid2.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(65.0)
			});
			grid2.ColumnDefinitions.Add(new ColumnDefinition());
			grid2.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(90.0)
			});
			grid2.Children.Add(new TextBlock
			{
				Text = (new string[3] { "X Pitch", "Y Yaw", "Z Roll" })[num],
				VerticalAlignment = VerticalAlignment.Center
			});
			Slider slider2 = new Slider
			{
				Minimum = -180.0,
				Maximum = 180.0,
				SmallChange = 0.1,
				LargeChange = 5.0
			};
			rotations[num] = slider2;
			Grid.SetColumn(slider2, 1);
			grid2.Children.Add(slider2);
			TextBox element2 = PhotoNumberInput.Create(slider2);
			Grid.SetColumn(element2, 2);
			grid2.Children.Add(element2);
			slider2.ValueChanged += delegate
			{
				dirty = true;
				Publish();
			};
			base.Children.Add(grid2);
		}
		Button button2 = new Button
		{
			Content = "Reset Rotation",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button2.Click += delegate
		{
			Slider[] array2 = rotations;
			for (int j = 0; j < array2.Length; j++)
			{
				array2[j].Value = 0.0;
			}
			dirty = true;
			Publish();
		};
		base.Children.Add(button2);
		base.Children.Add(weapons);
		base.Children.Add(new TextBlock
		{
			Text = "Shows the weapons set below; turn this off to hide them all.",
			TextWrapping = TextWrapping.Wrap
		});
		base.Children.Add(status);
		weapons.Checked += delegate
		{
			dirty = true;
			Publish();
		};
		weapons.Unchecked += delegate
		{
			dirty = true;
			Publish();
		};
		base.Children.Add(head);
		base.Children.Add(new TextBlock
		{
			Text = "Turns the head on top of the current motion and eases it back to center once the camera passes the character's limit. The eyes are not affected.",
			TextWrapping = TextWrapping.Wrap
		});
		head.Checked += delegate
		{
			dirty = true;
			Publish();
		};
		head.Unchecked += delegate
		{
			dirty = true;
			Publish();
		};
		base.Children.Add(manualHead);
		base.Children.Add(new TextBlock
		{
			Text = "Uses the same limits and smoothing as Head Follows Camera. With follow on these fine-tune the local X, Y and Z axes; with follow off they aim the head on their own. Values are degrees, and 0° adds no offset.",
			TextWrapping = TextWrapping.Wrap
		});
		manualHead.Checked += delegate
		{
			dirty = true;
			Publish();
		};
		manualHead.Unchecked += delegate
		{
			dirty = true;
			Publish();
		};
		for (int num2 = 0; num2 < 3; num2++)
		{
			Grid grid3 = new Grid
			{
				Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
			};
			grid3.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(65.0)
			});
			grid3.ColumnDefinitions.Add(new ColumnDefinition());
			grid3.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(90.0)
			});
			grid3.Children.Add(new TextBlock
			{
				Text = (new string[3] { "Head X", "Head Y", "Head Z" })[num2],
				VerticalAlignment = VerticalAlignment.Center
			});
			Slider slider3 = new Slider
			{
				Minimum = -180.0,
				Maximum = 180.0,
				SmallChange = 0.1,
				LargeChange = 5.0
			};
			headAngles[num2] = slider3;
			Grid.SetColumn(slider3, 1);
			grid3.Children.Add(slider3);
			TextBox element3 = PhotoNumberInput.Create(slider3);
			Grid.SetColumn(element3, 2);
			grid3.Children.Add(element3);
			slider3.ValueChanged += delegate
			{
				dirty = true;
				Publish();
			};
			base.Children.Add(grid3);
		}
		Button button3 = new Button
		{
			Content = "Reset Head Rotation",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button3.Click += delegate
		{
			Slider[] array2 = headAngles;
			for (int j = 0; j < array2.Length; j++)
			{
				array2[j].Value = 0.0;
			}
			dirty = true;
			Publish();
		};
		base.Children.Add(button3);
		Grid grid4 = new Grid
		{
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		grid4.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(90.0)
		});
		grid4.ColumnDefinitions.Add(new ColumnDefinition());
		grid4.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(90.0)
		});
		grid4.Children.Add(new TextBlock
		{
			Text = "Model Scale"
		});
		Grid.SetColumn(modelScale, 1);
		grid4.Children.Add(modelScale);
		TextBox element4 = PhotoNumberInput.Create(modelScale);
		Grid.SetColumn(element4, 2);
		grid4.Children.Add(element4);
		newer.Children.Add(grid4);
		modelScale.ValueChanged += delegate
		{
			dirty = true;
			Publish();
		};
		Button button4 = new Button
		{
			Content = "Reset Scale",
			Margin = new Thickness(0.0, 4.0, 0.0, 8.0)
		};
		button4.Click += delegate
		{
			modelScale.Value = 1.0;
			dirty = true;
			Publish();
		};
		newer.Children.Add(button4);
		BuildEyeControls();
		BuildMouthControls();
		BuildMorphControls();
		newer.Children.Add(styleView);
		newer.Visibility = Visibility.Collapsed;
		base.Children.Add(newer);
		base.Children.Add(new TextBlock
		{
			Text = "Weapon Switching",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		});
		weaponChoice.Items.Add(new ComboBoxItem
		{
			Content = "Select a weapon to adjust",
			Tag = 0
		});
		weaponChoice.SelectedIndex = 0;
		weaponPosition.Items.Add(new ComboBoxItem
		{
			Content = "Original attach point",
			Tag = 0
		});
		weaponPosition.SelectedIndex = 0;
		string[] array = new string[3] { "Leave as is", "Show", "Hide" };
		foreach (string newItem in array)
		{
			weaponMode.Items.Add(newItem);
		}
		weaponMode.SelectedIndex = 0;
		weaponMode.IsEnabled = false;
		weaponPosition.IsEnabled = false;
		base.Children.Add(weaponChoice);
		base.Children.Add(weaponMode);
		base.Children.Add(weaponPosition);
		Button button5 = new Button
		{
			Content = "Restore Original Weapons",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button5.Click += delegate
		{
			Array.Clear(weaponModes);
			Array.Clear(weaponStates);
			weapons.IsChecked = true;
			LoadWeaponEditor();
			dirty = true;
			Publish();
		};
		base.Children.Add(button5);
		base.Children.Add(new TextBlock
		{
			Text = "Each weapon is set on its own and several can show at once; leaving photo mode restores them.",
			TextWrapping = TextWrapping.Wrap
		});
		weaponChoice.SelectionChanged += delegate
		{
			if (!syncingWeapons)
			{
				LoadWeaponEditor();
				dirty = true;
				Publish();
			}
		};
		weaponMode.SelectionChanged += delegate
		{
			int num4 = Choice(weaponChoice);
			if (!syncingWeapons && num4 > 0)
			{
				weaponModes[num4 - 1] = weaponMode.SelectedIndex;
				if (weaponMode.SelectedIndex == 1)
				{
					weapons.IsChecked = true;
				}
				dirty = true;
				Publish();
			}
		};
		weaponPosition.SelectionChanged += delegate
		{
			int num4 = Choice(weaponChoice);
			if (!syncingWeapons && num4 > 0)
			{
				weaponStates[num4 - 1] = Choice(weaponPosition);
				weaponModes[num4 - 1] = 1;
				weaponMode.SelectedIndex = 1;
				weapons.IsChecked = true;
				dirty = true;
				Publish();
			}
		};
	}

	public void Refresh(int? gamePid, bool active)
	{
		if (pid != gamePid)
		{
			Dispose();
			pid = gamePid;
		}
		styleView.Refresh(gamePid, active && extended);
		base.IsEnabled = active && selection.SelectedActorId != 0;
		if (!active)
		{
			modelScale.Value = 1.0;
			LoadEyes(null);
			LoadMouth(null);
			LoadMorphs(null);
			saved.Clear();
			actor = 0uL;
			Array.Clear(weaponModes);
			Array.Clear(weaponStates);
			Slider[] array = offsets;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Value = 0.0;
			}
			array = rotations;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Value = 0.0;
			}
			weapons.IsChecked = true;
			head.IsChecked = false;
			manualHead.IsChecked = false;
			array = headAngles;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Value = 0.0;
			}
			dirty = true;
			status.Text = "Enter photo mode, then pick a character.";
			return;
		}
		try
		{
			if (ipc == null && gamePid.HasValue)
			{
				mapping = MemoryMappedFile.OpenExisting($"Local\\FGOLocalPhotoModel_{gamePid}");
				ipc = mapping.CreateViewAccessor(0L, 0L);
				int version = ipc.ReadInt32(4L);
				if (ipc.ReadInt32(0L) != 1297106758 || !((version == 12 && ipc.Capacity >= 158824) || (version == 6 && ipc.Capacity >= 856)))
				{
					Dispose();
					status.Text = "Restart the game to load the newer photo hook.";
					return;
				}
				extended = version == 12;
				newer.Visibility = (extended ? Visibility.Visible : Visibility.Collapsed);
			}
			if (actor != selection.SelectedActorId)
			{
				if (ipc != null && ipc.ReadInt32(8L) != 0)
				{
					return;
				}
				if (actor != 0L)
				{
					saved[actor] = new Saved(offsets.Select((Slider s) => s.Value).ToArray(), rotations.Select((Slider s) => s.Value).ToArray(), weapons.IsChecked == true, head.IsChecked == true, manualHead.IsChecked == true, headAngles.Select((Slider s) => s.Value).ToArray(), (int[])weaponModes.Clone(), (int[])weaponStates.Clone(), SaveEyes(), modelScale.Value, SaveMouth(), SaveMorphs());
				}
				loadingActor = true;
				try
				{
					actor = selection.SelectedActorId;
					saved.TryGetValue(actor, out Saved value);
					for (int num = 0; num < 3; num++)
					{
						PhotoNumberInput.SetExpandedValue(offsets[num], ((object)value != null) ? value.Offset[num] : 0.0);
						rotations[num].Value = (((object)value != null) ? value.Rotation[num] : 0.0);
					}
					for (int num2 = 0; num2 < 10; num2++)
					{
						weaponModes[num2] = (((object)value != null) ? value.Modes[num2] : 0);
						weaponStates[num2] = (((object)value != null) ? value.States[num2] : 0);
					}
					weapons.IsChecked = value?.Weapons ?? true;
					head.IsChecked = value?.Head ?? false;
					manualHead.IsChecked = value?.ManualHead ?? false;
					for (int num3 = 0; num3 < 3; num3++)
					{
						headAngles[num3].Value = (((object)value != null) ? value.HeadAngles[num3] : 0.0);
					}
					LoadEyes(value?.Eyes);
					LoadMouth(value?.Mouth);
					LoadMorphs(value?.Morphs);
					modelScale.Value = value?.Scale ?? 1.0;
					weaponChoice.SelectedIndex = 0;
					LoadWeaponEditor();
					weaponSignature = "";
					dirty = true;
				}
				finally
				{
					loadingActor = false;
				}
			}
			RefreshWeapons();
			if (extended)
			{
				RefreshEyeStatus();
				RefreshMouthStatus();
				RefreshMorphStatus();
			}
			Publish();
			TextBlock textBlock = status;
			textBlock.Text = ipc?.ReadInt32(12L) switch
			{
				1 => (weapons.IsChecked == true) ? "Weapons are shown" : "Weapons are hidden - tick Show Weapons above", 
				-1 => "That character has left the scene.", 
				-2 => "Those values are not valid.", 
				-3 => "This model cannot be edited right now.", 
				-5 => "This weapon has no original attach point - pick one of the listed positions.", 
				_ => "Pick a character.", 
			};
			if (head.IsChecked == true || manualHead.IsChecked == true)
			{
				MemoryMappedViewAccessor? memoryMappedViewAccessor = ipc;
				if (memoryMappedViewAccessor != null && memoryMappedViewAccessor.ReadInt32(44L) == -4)
				{
					status.Text = "No usable head bone or follow limit was found on this character.";
				}
			}
		}
		catch (FileNotFoundException)
		{
			status.Text = "This needs a newer game hook - restart the game.";
		}
		catch (IOException)
		{
			Dispose();
			status.Text = "The connection to the game was lost.";
		}
	}

	private void Publish()
	{
		if (!loadingActor && base.IsEnabled && dirty && ipc != null && ipc.ReadInt32(8L) == 0)
		{
			ipc.Write(16L, actor);
			for (int i = 0; i < 3; i++)
			{
				ipc.Write(24 + i * 4, (float)offsets[i].Value);
			}
			for (int j = 0; j < 3; j++)
			{
				ipc.Write(824 + j * 4, (float)rotations[j].Value);
			}
			ipc.Write(36L, (weapons.IsChecked != true) ? 1 : 0);
			ipc.Write(40L, (head.IsChecked == true) ? 1 : 0);
			ipc.Write(836L, (manualHead.IsChecked == true) ? 1 : 0);
			for (int k = 0; k < 3; k++)
			{
				ipc.Write(840 + k * 4, (float)headAngles[k].Value);
			}
			if (extended)
			{
				WriteEyes();
				WriteMouth();
				WriteMorphs();
				ipc.Write(900L, (float)modelScale.Value);
			}
			ipc.Write(48L, Choice(weaponChoice));
			ipc.Write(52L, 0);
			ipc.WriteArray(704L, weaponModes, 0, 10);
			ipc.WriteArray(744L, weaponStates, 0, 10);
			ipc.Write(8L, 1);
			dirty = false;
		}
	}

	private void LoadWeaponEditor()
	{
		syncingWeapons = true;
		try
		{
			int num = Choice(weaponChoice);
			ComboBox comboBox = weaponMode;
			bool isEnabled = (weaponPosition.IsEnabled = num > 0);
			comboBox.IsEnabled = isEnabled;
			weaponMode.SelectedIndex = ((num > 0) ? weaponModes[num - 1] : 0);
			weaponPosition.Items.Clear();
			weaponPosition.Items.Add(new ComboBoxItem
			{
				Content = "Original attach point",
				Tag = 0
			});
			weaponPosition.SelectedIndex = 0;
			weaponMask = -1;
		}
		finally
		{
			syncingWeapons = false;
		}
	}

	private void RefreshWeapons()
	{
		if (ipc == null || ipc.ReadInt32(8L) != 0)
		{
			return;
		}
		int num = ipc.ReadInt32(60L);
		if (num < 0 || num > 10)
		{
			return;
		}
		List<string> list = new List<string>();
		for (int i = 0; i < num; i++)
		{
			byte[] array = new byte[64];
			ipc.ReadArray(64 + i * 64, array, 0, 64);
			int num2 = Array.IndexOf(array, (byte)0);
			list.Add(Encoding.UTF8.GetString(array, 0, (num2 < 0) ? 64 : num2));
		}
		string text = string.Join("\n", list);
		int num3 = ipc.ReadInt32(56L);
		syncingWeapons = true;
		try
		{
			if (text != weaponSignature)
			{
				int num4 = Choice(weaponChoice);
				weaponChoice.Items.Clear();
				weaponChoice.Items.Add(new ComboBoxItem
				{
					Content = "Select a weapon to adjust",
					Tag = 0
				});
				for (int j = 0; j < list.Count; j++)
				{
					weaponChoice.Items.Add(new ComboBoxItem
					{
						Content = $"{j + 1} - {list[j]}",
						Tag = j + 1
					});
				}
				weaponChoice.SelectedIndex = ((num4 <= num) ? num4 : 0);
				weaponSignature = text;
				if (num4 > num)
				{
					dirty = true;
					weaponMask = -1;
				}
			}
			if (num3 == weaponMask)
			{
				return;
			}
			int num5 = Choice(weaponChoice);
			int num6 = ((num5 > 0) ? weaponStates[num5 - 1] : 0);
			weaponPosition.Items.Clear();
			weaponPosition.Items.Add(new ComboBoxItem
			{
				Content = "Original attach point",
				Tag = 0
			});
			string[] array2 = new string[14]
			{
				"", "Right Hand", "Left Hand", "", "Upper Body", "Chest", "Left Thigh", "Lower Body", "Head", "Waist",
				"Right Forearm", "Left Forearm", "Root", "Base"
			};
			for (int k = 1; k < array2.Length; k++)
			{
				if ((num3 & (1 << k)) != 0)
				{
					weaponPosition.Items.Add(new ComboBoxItem
					{
						Content = array2[k],
						Tag = k
					});
				}
			}
			weaponPosition.SelectedIndex = 0;
			foreach (ComboBoxItem item in (IEnumerable)weaponPosition.Items)
			{
				if ((int)item.Tag == num6)
				{
					weaponPosition.SelectedItem = item;
				}
			}
			if (Choice(weaponPosition) != num6 && num5 > 0)
			{
				weaponStates[num5 - 1] = 0;
				dirty = true;
			}
			weaponMask = num3;
		}
		finally
		{
			syncingWeapons = false;
		}
	}

	public void Dispose()
	{
		styleView.Dispose();
		extended = false;
		saved.Clear();
		ipc?.Dispose();
		mapping?.Dispose();
		ipc = null;
		mapping = null;
		actor = 0uL;
		dirty = true;
	}

	private void BuildMorphControls()
	{
		StackPanel stackPanel = new StackPanel();
		newer.Children.Add(stackPanel);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Face Parts",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 14.0, 0.0, 6.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Pick a part such as the face, the inside of the mouth or the teeth, add shapes to it and set each shape's weight. Shapes add up.",
			TextWrapping = TextWrapping.Wrap
		});
		stackPanel.Children.Add(morphGroupChoice);
		stackPanel.Children.Add(morphEnabled);
		stackPanel.Children.Add(morphRows);
		stackPanel.Children.Add(morphAdd);
		morphAdd.Click += delegate
		{
			MorphGroup morphGroup = morphGroups.FirstOrDefault((MorphGroup g) => g.Index == SelectedMorphGroup);
			if (!(morphGroup == null))
			{
				HashSet<int> used = morphChoices.Select((ComboBox c) => (c.SelectedItem is ComboBoxItem comboBoxItem) ? ((int)comboBoxItem.Tag) : 0).ToHashSet();
				(int, string) tuple = morphGroup.Shapes.FirstOrDefault<(int, string)>(((int Target, string Name) s) => !used.Contains(s.Target));
				if (tuple.Item1 != 0)
				{
					AddMorphRow(morphGroup, tuple.Item1, 0.0);
					morphEnabled.IsChecked = true;
					MorphChanged();
					UpdateMorphAdd();
				}
			}
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "A customized part uses these shapes instead of the expression's; the other parts keep the expression, and each eyelid can still be set above.",
			TextWrapping = TextWrapping.Wrap
		});
		Button button = new Button
		{
			Content = "Restore All Parts",
			Margin = new Thickness(0.0, 6.0, 0.0, 4.0)
		};
		button.Click += delegate
		{
			morphSettings.Clear();
			LoadMorphEditor();
			dirty = true;
			Publish();
		};
		stackPanel.Children.Add(button);
		stackPanel.Children.Add(morphStatus);
		morphEnabled.Checked += delegate
		{
			MorphChanged();
		};
		morphEnabled.Unchecked += delegate
		{
			MorphChanged();
		};
		morphGroupChoice.SelectionChanged += delegate
		{
			if (!syncingMorph)
			{
				LoadMorphEditor();
			}
		};
		LoadMorphEditor();
	}

	private void MorphChanged()
	{
		if (syncingMorph || loadingActor)
		{
			return;
		}
		int selectedMorphGroup = SelectedMorphGroup;
		if (selectedMorphGroup >= 0)
		{
			morphSettings[selectedMorphGroup] = new MorphSaved(morphEnabled.IsChecked == true, morphChoices.Select((ComboBox c) => (c.SelectedItem is ComboBoxItem { Tag: var tag } && tag is int) ? ((int)tag) : 0).ToArray(), morphWeights.Select((Slider s) => s.Value).ToArray());
			dirty = true;
			Publish();
		}
	}

	private Dictionary<int, MorphSaved> SaveMorphs()
	{
		return morphSettings.ToDictionary<KeyValuePair<int, MorphSaved>, int, MorphSaved>((KeyValuePair<int, MorphSaved> p) => p.Key, (KeyValuePair<int, MorphSaved> p) => new MorphSaved(p.Value.Enabled, (int[])p.Value.Targets.Clone(), (double[])p.Value.Weights.Clone()));
	}

	private void LoadMorphs(Dictionary<int, MorphSaved>? savedMorphs)
	{
		syncingMorph = true;
		try
		{
			morphSettings.Clear();
			if (savedMorphs != null)
			{
				foreach (KeyValuePair<int, MorphSaved> savedMorph in savedMorphs)
				{
					morphSettings[savedMorph.Key] = new MorphSaved(savedMorph.Value.Enabled, (int[])savedMorph.Value.Targets.Clone(), (double[])savedMorph.Value.Weights.Clone());
				}
			}
			morphGroups.Clear();
			morphGroupChoice.Items.Clear();
			morphSequence = -1;
		}
		finally
		{
			syncingMorph = false;
		}
		LoadMorphEditor();
	}

	private void LoadMorphEditor()
	{
		syncingMorph = true;
		try
		{
			int id = SelectedMorphGroup;
			MorphGroup morphGroup = morphGroups.FirstOrDefault((MorphGroup g) => g.Index == id);
			morphSettings.TryGetValue(id, out MorphSaved value);
			morphEnabled.IsEnabled = morphGroup != null;
			morphEnabled.IsChecked = value?.Enabled ?? false;
			morphRows.Children.Clear();
			morphChoices.Clear();
			morphWeights.Clear();
			if (morphGroup != null && value != null)
			{
				int i;
				for (i = 0; i < value.Targets.Length; i++)
				{
					if (morphGroup.Shapes.Any<(int, string)>(((int Target, string Name) s) => s.Target == value.Targets[i]))
					{
						AddMorphRow(morphGroup, value.Targets[i], value.Weights.ElementAtOrDefault(i));
					}
				}
			}
			UpdateMorphAdd();
		}
		finally
		{
			syncingMorph = false;
		}
	}

	private void UpdateMorphAdd()
	{
		MorphGroup morphGroup = morphGroups.FirstOrDefault((MorphGroup g) => g.Index == SelectedMorphGroup);
		morphAdd.IsEnabled = morphGroup != null && morphChoices.Count < morphGroup.Shapes.Select<(int, string), int>(((int Target, string Name) s) => s.Target).Distinct().Count();
	}

	private void AddMorphRow(MorphGroup group, int target, double weight)
	{
		Border card = new Border
		{
			Padding = new Thickness(8.0),
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0),
			CornerRadius = new CornerRadius(4.0),
			BorderThickness = new Thickness(1.0)
		};
		card.SetResourceReference(Border.BorderBrushProperty, "LineHighBrush");
		StackPanel stackPanel = new StackPanel();
		card.Child = stackPanel;
		DockPanel dockPanel = new DockPanel
		{
			LastChildFill = true
		};
		stackPanel.Children.Add(dockPanel);
		Button button = new Button
		{
			Content = "Remove",
			MinWidth = 56.0,
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		DockPanel.SetDock(button, Dock.Right);
		dockPanel.Children.Add(button);
		ComboBox choice = new ComboBox
		{
			MinWidth = 0.0,
			MaxDropDownHeight = 260.0
		};
		foreach (var shape in group.Shapes)
		{
			choice.Items.Add(new ComboBoxItem
			{
				Content = shape.Name,
				ToolTip = shape.Name,
				Tag = shape.Target
			});
		}
		foreach (ComboBoxItem item in (IEnumerable)choice.Items)
		{
			if ((int)item.Tag == target)
			{
				choice.SelectedItem = item;
			}
		}
		dockPanel.Children.Add(choice);
		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(88.0)
		});
		stackPanel.Children.Add(grid);
		Slider slider = new Slider
		{
			Minimum = 0.0,
			Maximum = 1.0,
			Value = weight,
			SmallChange = 0.01,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		grid.Children.Add(slider);
		TextBox element = PhotoNumberInput.Create(slider);
		Grid.SetColumn(element, 1);
		grid.Children.Add(element);
		morphChoices.Add(choice);
		morphWeights.Add(slider);
		morphRows.Children.Add(card);
		choice.SelectionChanged += delegate
		{
			MorphChanged();
			UpdateMorphAdd();
		};
		slider.ValueChanged += delegate
		{
			MorphChanged();
		};
		button.Click += delegate
		{
			morphChoices.Remove(choice);
			morphWeights.Remove(slider);
			morphRows.Children.Remove(card);
			MorphChanged();
			UpdateMorphAdd();
		};
	}

	private void WriteMorphs()
	{
		if (ipc == null)
		{
			return;
		}
		for (int i = 0; i < 32; i++)
		{
			int num = 976 + i * 508;
			morphSettings.TryGetValue(i, out MorphSaved value);
			ipc.Write(num, (value?.Enabled ?? false) ? 1 : 0);
			for (int j = 0; j < 63; j++)
			{
				ipc.Write(num + 4 + j * 8, value?.Targets.ElementAtOrDefault(j) ?? 0);
				ipc.Write(num + 8 + j * 8, (float)(value?.Weights.ElementAtOrDefault(j) ?? 0.0));
			}
		}
	}

	private static string MorphName(byte[] data, int offset)
	{
		int num = Array.IndexOf(data, (byte)0, offset, 64);
		return Encoding.UTF8.GetString(data, offset, ((num < 0) ? (offset + 64) : num) - offset);
	}

	private void RefreshMorphStatus()
	{
		if (ipc == null || actor == 0L)
		{
			return;
		}
		int num = ipc.ReadInt32(17232L);
		if ((num & 1) != 0 || ipc.ReadUInt64(17240L) != actor || ipc.ReadUInt64(17248L) == 0L)
		{
			return;
		}
		if (num != morphSequence)
		{
			int num2 = ipc.ReadInt32(17236L);
			if (num2 < 0 || num2 > 32)
			{
				return;
			}
			byte[] array = new byte[num2 * 4424];
			ipc.ReadArray(17256L, array, 0, array.Length);
			if (ipc.ReadInt32(17232L) != num || ipc.ReadUInt64(17240L) != actor)
			{
				return;
			}
			int selectedMorphGroup = SelectedMorphGroup;
			List<MorphGroup> list = new List<MorphGroup>();
			for (int i = 0; i < num2; i++)
			{
				int num3 = i * 4424;
				int num4 = BitConverter.ToInt32(array, num3 + 64);
				int num5 = BitConverter.ToInt32(array, num3 + 68);
				if (num4 < 0 || num4 >= 32 || num5 < 0 || num5 > 64)
				{
					continue;
				}
				List<(int, string)> list2 = new List<(int, string)>();
				for (int j = 0; j < num5; j++)
				{
					int num6 = num3 + 72 + j * 68;
					int num7 = BitConverter.ToInt32(array, num6 + 64);
					if (num7 > 0 && num7 < 64)
					{
						list2.Add((num7, MorphName(array, num6)));
					}
				}
				if (list2.Count > 0)
				{
					list.Add(new MorphGroup(num4, MorphName(array, num3), list2));
				}
			}
			syncingMorph = true;
			try
			{
				morphGroups.Clear();
				morphGroups.AddRange(list);
				morphGroupChoice.Items.Clear();
				foreach (MorphGroup item in list)
				{
					string content = item.Name switch
					{
						"face" => "Face",
						"oral" => "Inside of the Mouth",
						"tooth" => "Teeth",
						"cheek" => "Cheeks",
						"eyebrow" => "Eyebrows",
						"eyelashes" => "Eyelashes",
						"eyelid" => "Eyelids",
						"eyehole" => "Eye Area",
						_ => item.Name, 
					};
					morphGroupChoice.Items.Add(new ComboBoxItem
					{
						Content = content,
						ToolTip = item.Name,
						Tag = item.Index
					});
				}
				morphGroupChoice.SelectedIndex = ((list.Count <= 0) ? (-1) : 0);
				foreach (ComboBoxItem item2 in (IEnumerable)morphGroupChoice.Items)
				{
					if ((int)item2.Tag == selectedMorphGroup)
					{
						morphGroupChoice.SelectedItem = item2;
					}
				}
				morphSequence = num;
			}
			finally
			{
				syncingMorph = false;
			}
			LoadMorphEditor();
		}
		TextBlock textBlock = morphStatus;
		textBlock.Text = ipc.ReadInt32(972L) switch
		{
			1 => "Face parts applied",
			-4 => "Some shapes are no longer available - pick them again.",
			_ => (morphGroups.Count > 0) ? "Parts that are not customized keep the expression." : "This character has no face shapes to edit.",
		};
	}

	private void BuildMouthControls()
	{
		newer.Children.Add(new TextBlock
		{
			Text = "Mouth",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 14.0, 0.0, 4.0)
		});
		newer.Children.Add(mouthEnabled);
		mouthMode.Items.Add(new TabItem
		{
			Header = "Mix",
			Content = new TextBlock
			{
				Text = "Set A, E, I, O and U each on its own; they add up.",
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(4.0, 8.0, 4.0, 8.0)
			}
		});
		mouthMode.Items.Add(new TabItem
		{
			Header = "Preset",
			Content = mouthShape
		});
		mouthMode.SelectedIndex = 0;
		newer.Children.Add(mouthMode);
		for (int i = 0; i < 5; i++)
		{
			Grid grid = new Grid
			{
				Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
			};
			mouthRows[i] = grid;
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(32.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(88.0)
			});
			grid.Children.Add(new TextBlock
			{
				Text = (new string[5] { "A", "E", "I", "O", "U" })[i],
				VerticalAlignment = VerticalAlignment.Center
			});
			Slider slider = new Slider
			{
				Minimum = 0.0,
				Maximum = 1.0,
				Value = 0.0,
				SmallChange = 0.01,
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
			};
			mouthWeights[i] = slider;
			Grid.SetColumn(slider, 1);
			grid.Children.Add(slider);
			TextBox element = PhotoNumberInput.Create(slider);
			Grid.SetColumn(element, 2);
			grid.Children.Add(element);
			newer.Children.Add(grid);
			slider.ValueChanged += delegate
			{
				MouthChanged();
			};
		}
		newer.Children.Add(new TextBlock
		{
			Text = "A preset locks the weights above; Mix keeps their values. Untick Custom Mouth to give the mouth back to its animation.",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 6.0, 0.0, 4.0)
		});
		newer.Children.Add(mouthStatus);
		mouthEnabled.Checked += delegate
		{
			MouthChanged();
		};
		mouthEnabled.Unchecked += delegate
		{
			MouthChanged();
		};
		mouthMode.SelectionChanged += delegate(object _, SelectionChangedEventArgs e)
		{
			if (e.Source == mouthMode)
			{
				MouthChanged();
			}
		};
		mouthShape.SelectionChanged += delegate
		{
			if (!syncingMouth && mouthShape.SelectedItem is ComboBoxItem { Tag: var tag } && tag is int num)
			{
				mouthSelection = num;
				MouthChanged();
			}
		};
		UpdateMouthEditor();
	}

	private void UpdateMouthEditor()
	{
		bool flag = mouthAvailable > 0;
		bool flag2 = flag && mouthEnabled.IsChecked == true;
		mouthEnabled.IsEnabled = flag;
		mouthMode.IsEnabled = flag2;
		mouthShape.IsEnabled = flag2;
		for (int i = 0; i < 5; i++)
		{
			if (mouthRows[i] != null)
			{
				mouthRows[i].IsEnabled = flag2 && mouthMode.SelectedIndex == 0 && (mouthAvailable & (1 << MouthVowels[i])) != 0;
				mouthRows[i].Opacity = (mouthRows[i].IsEnabled ? 1.0 : 0.75);
			}
		}
	}

	private void MouthChanged()
	{
		if (!syncingMouth)
		{
			UpdateMouthEditor();
			dirty = true;
			Publish();
		}
	}

	private double[] SaveMouth()
	{
		return new double[3]
		{
			(mouthEnabled.IsChecked == true) ? 1.0 : 0.0,
			mouthMode.SelectedIndex,
			mouthSelection
		}.Concat(mouthWeights.Select((Slider s) => s.Value)).ToArray();
	}

	private void LoadMouth(double[]? values)
	{
		syncingMouth = true;
		try
		{
			mouthEnabled.IsChecked = values != null && values.ElementAtOrDefault(0) == 1.0;
			mouthMode.SelectedIndex = (int)(values?.ElementAtOrDefault(1) ?? 0.0);
			mouthSelection = (int)(values?.ElementAtOrDefault(2) ?? 0.0);
			for (int i = 0; i < 5; i++)
			{
				mouthWeights[i].Value = values?.ElementAtOrDefault(i + 3) ?? 0.0;
			}
			mouthAvailable = -1;
			mouthShape.Items.Clear();
			mouthStatus.Text = "Reading the character's mouth shapes...";
			UpdateMouthEditor();
		}
		finally
		{
			syncingMouth = false;
		}
	}

	private void WriteMouth()
	{
		if (ipc != null)
		{
			ipc.Write(936L, (mouthEnabled.IsChecked == true) ? 1 : 0);
			ipc.Write(940L, (mouthMode.SelectedIndex == 1) ? mouthSelection : (-1));
			for (int i = 0; i < 5; i++)
			{
				ipc.Write(944 + i * 4, (float)mouthWeights[i].Value);
			}
		}
	}

	private void RefreshMouthStatus()
	{
		if (ipc == null || actor == 0L)
		{
			return;
		}
		int num = ipc.ReadInt32(17232L);
		if ((num & 1) != 0 || ipc.ReadUInt64(17240L) != actor)
		{
			return;
		}
		if (ipc.ReadUInt64(17248L) == 0L)
		{
			mouthStatus.Text = "Reading the character's mouth shapes...";
			return;
		}
		int num2 = ipc.ReadInt32(968L);
		int num3 = ipc.ReadInt32(964L);
		if (ipc.ReadInt32(17232L) != num || ipc.ReadUInt64(17240L) != actor)
		{
			return;
		}
		if (num2 != mouthAvailable)
		{
			syncingMouth = true;
			try
			{
				mouthShape.Items.Clear();
				(int, string)[] mouthShapes = MouthShapes;
				for (int i = 0; i < mouthShapes.Length; i++)
				{
					(int, string) tuple = mouthShapes[i];
					if ((num2 & (1 << tuple.Item1)) != 0)
					{
						mouthShape.Items.Add(new ComboBoxItem
						{
							Content = tuple.Item2,
							Tag = tuple.Item1
						});
					}
				}
				foreach (ComboBoxItem item in (IEnumerable)mouthShape.Items)
				{
					if ((int)item.Tag == mouthSelection)
					{
						mouthShape.SelectedItem = item;
					}
				}
				if (mouthShape.SelectedIndex < 0 && mouthShape.Items.Count > 0)
				{
					mouthShape.SelectedIndex = 0;
					mouthSelection = (int)((ComboBoxItem)mouthShape.SelectedItem).Tag;
					dirty = true;
				}
				mouthAvailable = num2;
				UpdateMouthEditor();
			}
			finally
			{
				syncingMouth = false;
			}
		}
		TextBlock textBlock = mouthStatus;
		textBlock.Text = num3 switch
		{
			-1 => "The game hook's mouth control is not running - restart the game.",
			-2 => "The mouth values or the model data are not valid.",
			-3 => "The mouth cannot be set right now.",
			-4 => "That mouth shape is not available - pick one from the list.",
			1 => "Mouth applied",
			_ => (num2 != 0) ? "Using the original mouth animation" : "This model has no mouth shapes.",
		};
	}
}
