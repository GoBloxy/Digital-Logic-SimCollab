using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DLS.SaveSystem
{
	/// <summary>
	/// Platform-native file dialogs. Falls back to default paths on unsupported platforms.
	/// </summary>
	public static class NativeFileDialog
	{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		struct OpenFileName
		{
			public int lStructSize;
			public IntPtr hwndOwner;
			public IntPtr hInstance;
			public string lpstrFilter;
			public string lpstrCustomFilter;
			public int nMaxCustFilter;
			public int nFilterIndex;
			public string lpstrFile;
			public int nMaxFile;
			public string lpstrFileTitle;
			public int nMaxFileTitle;
			public string lpstrInitialDir;
			public string lpstrTitle;
			public int Flags;
			public short nFileOffset;
			public short nFileExtension;
			public string lpstrDefExt;
			public IntPtr lCustData;
			public IntPtr lpfnHook;
			public string lpTemplateName;
			public IntPtr pvReserved;
			public int dwReserved;
			public int flagsEx;
		}

		[DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		static extern bool GetOpenFileName(ref OpenFileName ofn);

		[DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		static extern bool GetSaveFileName(ref OpenFileName ofn);

		const int OFN_EXPLORER = 0x00080000;
		const int OFN_FILEMUSTEXIST = 0x00001000;
		const int OFN_PATHMUSTEXIST = 0x00000800;
		const int OFN_NOCHANGEDIR = 0x00000008;
		const int OFN_OVERWRITEPROMPT = 0x00000002;
#endif

		/// <summary>
		/// Opens a file dialog to select a .dlsproj file to import.
		/// Returns the selected file path, or null if cancelled.
		/// </summary>
		public static string OpenFileDialog(string title, string filterDescription, string filterExtension)
		{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			try
			{
				OpenFileName ofn = new();
				ofn.lStructSize = Marshal.SizeOf(ofn);
				ofn.lpstrFilter = $"{filterDescription}\0*{filterExtension}\0All Files\0*.*\0\0";
				ofn.lpstrFile = new string('\0', 512);
				ofn.nMaxFile = 512;
				ofn.lpstrTitle = title;
				ofn.lpstrInitialDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
				ofn.Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
				ofn.lpstrDefExt = filterExtension.TrimStart('.');

				if (GetOpenFileName(ref ofn))
				{
					string result = ofn.lpstrFile;
					// Remove null terminators
					int nullIndex = result.IndexOf('\0');
					if (nullIndex >= 0) result = result.Substring(0, nullIndex);
					return result;
				}
			}
			catch (Exception e)
			{
				Debug.LogError("File dialog error: " + e.Message);
			}
#endif
			return null;
		}

		/// <summary>
		/// Opens a save file dialog.
		/// Returns the selected file path, or null if cancelled.
		/// </summary>
		public static string SaveFileDialog(string title, string filterDescription, string filterExtension, string defaultFileName)
		{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			try
			{
				OpenFileName ofn = new();
				ofn.lStructSize = Marshal.SizeOf(ofn);
				ofn.lpstrFilter = $"{filterDescription}\0*{filterExtension}\0\0";

				// Pre-fill with default filename
				char[] fileBuffer = new char[512];
				defaultFileName.CopyTo(0, fileBuffer, 0, Math.Min(defaultFileName.Length, 511));
				ofn.lpstrFile = new string(fileBuffer);
				ofn.nMaxFile = 512;
				ofn.lpstrTitle = title;
				ofn.lpstrInitialDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
				ofn.Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_OVERWRITEPROMPT;
				ofn.lpstrDefExt = filterExtension.TrimStart('.');

				if (GetSaveFileName(ref ofn))
				{
					string result = ofn.lpstrFile;
					int nullIndex = result.IndexOf('\0');
					if (nullIndex >= 0) result = result.Substring(0, nullIndex);
					return result;
				}
			}
			catch (Exception e)
			{
				Debug.LogError("Save dialog error: " + e.Message);
			}
#endif
			return null;
		}
	}
}
