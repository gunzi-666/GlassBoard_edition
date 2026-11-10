using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ManagedShell.Interop;
using ManagedShell.WindowsTray;
using TrayIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace GlassBoard;

public sealed class TrayBarWindow : GlassWindow
{
	private const double IconCell = 30.0;

	private readonly Controller C;

	private readonly NotificationArea area;

	private readonly ItemsControl icons;

	private readonly TextBlock empty;

	private readonly Border grip;

	private bool released;

	public static TrayBarWindow Start(Controller c)
	{
		NotificationArea notificationArea = new NotificationArea(new string[0], new TrayService(), new ExplorerTrayService());
		notificationArea.Initialize();
		if (notificationArea.IsFailed)
		{
			throw new InvalidOperationException("系统托盘接管失败");
		}
		return new TrayBarWindow(c, notificationArea);
	}

	private TrayBarWindow(Controller c, NotificationArea notificationArea)
		: base(16.0)
	{
		C = c;
		area = notificationArea;
		base.Title = "GlassBoard 托盘";
		base.ShowActivated = false;
		grip = new Border
		{
			Width = 4.0,
			Height = 18.0,
			CornerRadius = new CornerRadius(2.0),
			Margin = new Thickness(8.0, 0.0, 4.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			Background = new SolidColorBrush(Color.FromArgb(90, 180, 195, 215))
		};
		ItemsPanelTemplate panel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
		panel.VisualTree.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
		icons = new ItemsControl
		{
			ItemsPanel = panel,
			ItemTemplate = IconTemplate(),
			ItemsSource = area.AllIcons,
			VerticalAlignment = VerticalAlignment.Center,
			Focusable = false
		};
		empty = new TextBlock
		{
			Text = "等待托盘图标…",
			FontSize = 12.0,
			Margin = new Thickness(4.0, 0.0, 6.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		StackPanel row = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 5.0, 6.0, 5.0)
		};
		row.Children.Add(grip);
		row.Children.Add(icons);
		row.Children.Add(empty);
		ContentGrid.Children.Add(row);
		area.AllIcons.CollectionChanged += IconsChanged;
		Root.MouseLeftButtonDown += BeginMove;
		base.Closed += delegate
		{
			Release();
		};
		ApplyLook();
		Resize();
	}

	public void ApplyLook()
	{
		DockOptions dock = C.Prefs.Dock;
		LightTheme = C.Prefs.Light;
		MotionEnabled = dock.MotionEnabled;
		MotionLevel = dock.MotionLevel;
		ConfigureGlass(dock);
		Polish.Apply(this, LightTheme);
		empty.Foreground = base.MutedBrush;
		grip.Cursor = (C.Prefs.Placement.TrayBarLocked ? Cursors.Arrow : Cursors.SizeAll);
		GlassPercent = dock.OpacityPercent;
		Backdrop(LightTheme);
		base.ContextMenu = BarMenu();
	}

	public void Place()
	{
		Rect rect = DesktopWindowPolicy.WorkArea(this);
		PlacementOptions placement = C.Prefs.Placement;
		base.Left = FeatureRules.Position(placement.TrayBarLeft ?? (rect.Right - base.Width + 6.0), rect.Left, rect.Width, base.Width);
		base.Top = FeatureRules.Position(placement.TrayBarTop ?? (rect.Bottom - base.Height + 6.0), rect.Top, rect.Height, base.Height);
		Backdrop(LightTheme);
	}

	public void Release()
	{
		if (released)
		{
			return;
		}
		released = true;
		area.AllIcons.CollectionChanged -= IconsChanged;
		icons.ItemsSource = null;
		try
		{
			area.Dispose();
		}
		catch (Exception ex)
		{
			Store.Log(ex);
		}
		if (base.IsLoaded)
		{
			Close();
		}
	}

	private void IconsChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (!released)
			{
				Resize();
			}
		});
	}

	private void Resize()
	{
		bool any = !area.AllIcons.IsEmpty;
		empty.Visibility = (any ? Visibility.Collapsed : Visibility.Visible);
		ContentGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
		double width = Math.Ceiling(ContentGrid.DesiredSize.Width) + 28.0;
		double height = Math.Max(IconCell + 10.0, Math.Ceiling(ContentGrid.DesiredSize.Height)) + 28.0;
		double previous = base.Width;
		base.Width = width;
		base.Height = height;
		if (!base.IsVisible || double.IsNaN(previous))
		{
			return;
		}
		Rect rect = DesktopWindowPolicy.WorkArea(this);
		double left = base.Left;
		if (left + previous / 2.0 > rect.Left + rect.Width / 2.0)
		{
			left -= width - previous;
		}
		base.Left = FeatureRules.Position(left, rect.Left, rect.Width, width);
		SetGlassOpacity(GlassPercent);
	}

	private void BeginMove(object sender, MouseButtonEventArgs e)
	{
		if (e.Handled || C.Prefs.Placement.TrayBarLocked || e.ButtonState != MouseButtonState.Pressed)
		{
			return;
		}
		e.Handled = true;
		try
		{
			DragMove();
		}
		catch (InvalidOperationException)
		{
			return;
		}
		Rect rect = DesktopWindowPolicy.WorkArea(this);
		base.Left = FeatureRules.Position(base.Left, rect.Left, rect.Width, base.Width);
		base.Top = FeatureRules.Position(base.Top, rect.Top, rect.Height, base.Height);
		C.Prefs.Placement.TrayBarLeft = base.Left;
		C.Prefs.Placement.TrayBarTop = base.Top;
		C.Save();
		Backdrop(LightTheme);
	}

	private ContextMenu BarMenu()
	{
		ContextMenu menu = MenuTheme.Create(LightTheme);
		MenuItem lockItem = new MenuItem
		{
			Header = "锁定托盘条位置",
			IsCheckable = true,
			IsChecked = C.Prefs.Placement.TrayBarLocked
		};
		lockItem.Click += delegate
		{
			C.Prefs.Placement.TrayBarLocked = lockItem.IsChecked;
			C.Save();
			ApplyLook();
		};
		menu.Items.Add(lockItem);
		MenuItem reset = new MenuItem
		{
			Header = "移回屏幕右下角"
		};
		reset.Click += delegate
		{
			C.Prefs.Placement.TrayBarLeft = null;
			C.Prefs.Placement.TrayBarTop = null;
			C.Save();
			Place();
		};
		menu.Items.Add(reset);
		MenuItem off = new MenuItem
		{
			Header = "关闭托盘条（交还系统托盘）"
		};
		off.Click += delegate
		{
			C.SetTrayBar(enabled: false);
		};
		menu.Items.Add(off);
		return menu;
	}

	private DataTemplate IconTemplate()
	{
		FrameworkElementFactory cell = new FrameworkElementFactory(typeof(Border));
		cell.SetValue(FrameworkElement.WidthProperty, IconCell);
		cell.SetValue(FrameworkElement.HeightProperty, IconCell);
		cell.SetValue(Border.CornerRadiusProperty, new CornerRadius(6.0));
		cell.SetValue(Border.BackgroundProperty, Brushes.Transparent);
		cell.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Title"));
		cell.AddHandler(UIElement.MouseEnterEvent, new MouseEventHandler(IconEnter));
		cell.AddHandler(UIElement.MouseLeaveEvent, new MouseEventHandler(IconLeave));
		cell.AddHandler(UIElement.MouseMoveEvent, new MouseEventHandler(IconMove));
		cell.AddHandler(UIElement.MouseDownEvent, new MouseButtonEventHandler(IconDown));
		cell.AddHandler(UIElement.MouseUpEvent, new MouseButtonEventHandler(IconUp));
		cell.AddHandler(FrameworkElement.ContextMenuOpeningEvent, new ContextMenuEventHandler(delegate(object s, ContextMenuEventArgs e)
		{
			e.Handled = true;
		}));
		FrameworkElementFactory image = new FrameworkElementFactory(typeof(Image));
		image.SetBinding(Image.SourceProperty, new Binding("Icon"));
		image.SetValue(FrameworkElement.WidthProperty, 16.0);
		image.SetValue(FrameworkElement.HeightProperty, 16.0);
		image.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
		image.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		image.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
		cell.AppendChild(image);
		return new DataTemplate
		{
			VisualTree = cell
		};
	}

	private static uint CursorParam()
	{
		System.Drawing.Point position = System.Windows.Forms.Cursor.Position;
		return (uint)((position.Y << 16) | (position.X & 0xFFFF));
	}

	private void SetTrayHost()
	{
		PresentationSource source = PresentationSource.FromVisual(this);
		if (source == null)
		{
			return;
		}
		Matrix device = source.CompositionTarget.TransformToDevice;
		Rect rect = DesktopWindowPolicy.WorkArea(this);
		bool bottom = base.Top + base.Height / 2.0 > rect.Top + rect.Height / 2.0;
		area.SetTrayHostSizeData(new TrayHostSizeData
		{
			edge = (bottom ? NativeMethods.ABEdge.ABE_BOTTOM : NativeMethods.ABEdge.ABE_TOP),
			rc = new NativeMethods.Rect
			{
				Left = (int)Math.Round((base.Left + 14.0) * device.M11),
				Top = (int)Math.Round((base.Top + 14.0) * device.M22),
				Right = (int)Math.Round((base.Left + base.Width - 14.0) * device.M11),
				Bottom = (int)Math.Round((base.Top + base.Height - 14.0) * device.M22)
			}
		});
	}

	private void IconEnter(object sender, MouseEventArgs e)
	{
		e.Handled = true;
		Border cell = (Border)sender;
		cell.Background = Polish.Color(LightTheme ? "#33000000" : "#33FFFFFF");
		if (cell.DataContext is TrayIcon icon && PresentationSource.FromVisual(cell) is PresentationSource source)
		{
			Point location = cell.PointToScreen(new Point(0.0, 0.0));
			Matrix device = source.CompositionTarget.TransformToDevice;
			icon.Placement = new NativeMethods.Rect
			{
				Left = (int)location.X,
				Top = (int)location.Y,
				Right = (int)(cell.ActualWidth * device.M11),
				Bottom = (int)(cell.ActualHeight * device.M22)
			};
			icon.IconMouseEnter(CursorParam());
		}
	}

	private void IconLeave(object sender, MouseEventArgs e)
	{
		e.Handled = true;
		Border cell = (Border)sender;
		cell.Background = Brushes.Transparent;
		(cell.DataContext as TrayIcon)?.IconMouseLeave(CursorParam());
	}

	private void IconMove(object sender, MouseEventArgs e)
	{
		e.Handled = true;
		((sender as FrameworkElement)?.DataContext as TrayIcon)?.IconMouseMove(CursorParam());
	}

	private void IconDown(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		SetTrayHost();
		if (e.ChangedButton != MouseButton.Left)
		{
			((sender as FrameworkElement)?.DataContext as TrayIcon)?.IconMouseDown(e.ChangedButton, CursorParam(), System.Windows.Forms.SystemInformation.DoubleClickTime);
		}
	}

	private void IconUp(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		if (!((sender as FrameworkElement)?.DataContext is TrayIcon icon))
		{
			return;
		}
		if (e.ChangedButton == MouseButton.Left)
		{
			icon.IconMouseDown(e.ChangedButton, CursorParam(), System.Windows.Forms.SystemInformation.DoubleClickTime);
		}
		icon.IconMouseUp(e.ChangedButton, CursorParam(), System.Windows.Forms.SystemInformation.DoubleClickTime);
	}
}
