using System;
using System.Runtime.InteropServices;

namespace GlassBoard;

public static class WindowPreview
{
	[StructLayout(LayoutKind.Sequential)]
	public struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct SIZE
	{
		public int X;

		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DWM_THUMBNAIL_PROPERTIES
	{
		public uint Flags;

		public RECT Destination;

		public RECT Source;

		public byte Opacity;

		[MarshalAs(UnmanagedType.Bool)]
		public bool Visible;

		[MarshalAs(UnmanagedType.Bool)]
		public bool SourceClientAreaOnly;
	}

	private const uint DWM_TNP_RECTDESTINATION = 1u;

	private const uint DWM_TNP_OPACITY = 4u;

	private const uint DWM_TNP_VISIBLE = 8u;

	private const uint DWM_TNP_SOURCECLIENTAREAONLY = 16u;

	public const int HSHELL_WINDOWCREATED = 1;

	public const int HSHELL_WINDOWDESTROYED = 2;

	public const int HSHELL_WINDOWACTIVATED = 4;

	public const int HSHELL_REDRAW = 6;

	public const int HSHELL_WINDOWREPLACED = 13;

	public const int HSHELL_RUDEAPPACTIVATED = 32772;

	public const int HSHELL_FLASH = 32774;

	private const int WM_CLOSE = 16;

	[DllImport("dwmapi.dll")]
	private static extern int DwmIsCompositionEnabled(out bool enabled);

	[DllImport("dwmapi.dll")]
	private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);

	[DllImport("dwmapi.dll")]
	private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);

	[DllImport("dwmapi.dll")]
	private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref DWM_THUMBNAIL_PROPERTIES properties);

	[DllImport("dwmapi.dll")]
	private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out SIZE size);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int RegisterWindowMessage(string name);

	[DllImport("user32.dll")]
	private static extern bool RegisterShellHookWindow(IntPtr h);

	[DllImport("user32.dll")]
	private static extern bool DeregisterShellHookWindow(IntPtr h);

	[DllImport("user32.dll")]
	private static extern bool PostMessage(IntPtr h, int message, IntPtr w, IntPtr l);

	public static int RegisterShellHook(IntPtr window)
	{
		try
		{
			int message = RegisterWindowMessage("SHELLHOOK");
			if (message != 0 && RegisterShellHookWindow(window))
			{
				return message;
			}
		}
		catch (Exception ex)
		{
			Store.Log(ex);
		}
		return 0;
	}

	public static void UnregisterShellHook(IntPtr window)
	{
		try
		{
			DeregisterShellHookWindow(window);
		}
		catch
		{
		}
	}

	public static bool ThumbnailsAvailable()
	{
		try
		{
			bool enabled;
			return DwmIsCompositionEnabled(out enabled) == 0 && enabled;
		}
		catch
		{
			return false;
		}
	}

	public static IntPtr Register(IntPtr destination, IntPtr source)
	{
		try
		{
			if (destination != IntPtr.Zero && source != IntPtr.Zero && DwmRegisterThumbnail(destination, source, out var thumbnail) == 0)
			{
				return thumbnail;
			}
		}
		catch (Exception ex)
		{
			Store.Log(ex);
		}
		return IntPtr.Zero;
	}

	public static void Unregister(IntPtr thumbnail)
	{
		if (thumbnail != IntPtr.Zero)
		{
			DwmUnregisterThumbnail(thumbnail);
		}
	}

	public static bool SourceSize(IntPtr thumbnail, out int width, out int height)
	{
		width = 0;
		height = 0;
		if (thumbnail == IntPtr.Zero)
		{
			return false;
		}
		DWM_THUMBNAIL_PROPERTIES properties = new DWM_THUMBNAIL_PROPERTIES
		{
			Flags = DWM_TNP_SOURCECLIENTAREAONLY,
			SourceClientAreaOnly = true
		};
		DwmUpdateThumbnailProperties(thumbnail, ref properties);
		if (DwmQueryThumbnailSourceSize(thumbnail, out var size) != 0 || size.X <= 0 || size.Y <= 0)
		{
			return false;
		}
		width = size.X;
		height = size.Y;
		return true;
	}

	public static void Place(IntPtr thumbnail, RECT destination, bool visible)
	{
		if (thumbnail != IntPtr.Zero)
		{
			DWM_THUMBNAIL_PROPERTIES properties = new DWM_THUMBNAIL_PROPERTIES
			{
				Flags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
				Destination = destination,
				Opacity = byte.MaxValue,
				Visible = visible,
				SourceClientAreaOnly = true
			};
			DwmUpdateThumbnailProperties(thumbnail, ref properties);
		}
	}

	public static void Close(IntPtr window)
	{
		PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
	}
}
