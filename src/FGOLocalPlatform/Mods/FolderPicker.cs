using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FGOLocalPlatform;

internal static class FolderPicker
{
	[ComImport]
	[Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
	private class FileOpenDialog
	{
	}

	[ComImport]
	[Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IFileDialog
	{
		[PreserveSig]
		int Show(IntPtr owner);

		void SetFileTypes();

		void SetFileTypeIndex();

		void GetFileTypeIndex();

		void Advise();

		void Unadvise();

		void SetOptions(uint options);

		void GetOptions();

		void SetDefaultFolder();

		void SetFolder();

		void GetFolder();

		void GetCurrentSelection();

		void SetFileName();

		void GetFileName();

		void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

		void SetOkButtonLabel();

		void SetFileNameLabel();

		void GetResult(out IShellItem item);
	}

	[ComImport]
	[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IShellItem
	{
		void BindToHandler();

		void GetParent();

		void GetDisplayName(uint format, out IntPtr name);
	}

	public static string? Show(Window? owner, string title)
	{
		if (DialogSink.Take("dialog", title))
		{
			return null;
		}
		IFileDialog fileDialog = (IFileDialog)new FileOpenDialog();
		try
		{
			fileDialog.SetOptions(2144u);
			fileDialog.SetTitle(title);
			int num = fileDialog.Show((owner == null) ? IntPtr.Zero : new WindowInteropHelper(owner).Handle);
			if (num == -2147023673)
			{
				return null;
			}
			Marshal.ThrowExceptionForHR(num);
			fileDialog.GetResult(out IShellItem item);
			try
			{
				item.GetDisplayName(2147844096u, out var name);
				try
				{
					return Marshal.PtrToStringUni(name);
				}
				finally
				{
					Marshal.FreeCoTaskMem(name);
				}
			}
			finally
			{
				Marshal.ReleaseComObject(item);
			}
		}
		finally
		{
			Marshal.ReleaseComObject(fileDialog);
		}
	}
}
