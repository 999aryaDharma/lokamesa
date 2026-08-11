using System;
using System.IO;
using System.Runtime.InteropServices;
using DocumentIDE.Core.Filesystem;

namespace DocumentIDE.Infrastructure.Filesystem;

/// <summary>
/// Infrastructure wrapper for Windows Recycle Bin operations using P/Invoke SHFileOperationW.
/// FS-07: Explicit Physical File Operations.
/// </summary>
public class WindowsRecycleBin : IRecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW FileOp);

    public bool SendToRecycleBin(string path, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path cannot be empty.";
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            error = $"Target file or directory does not exist: '{fullPath}'";
            return false;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            error = "Windows Recycle Bin is only supported on Windows OS.";
            return false;
        }

        try
        {
            // SHFileOperationW requires a double-null-terminated string list
            string pFrom = fullPath + "\0\0";

            var fileOp = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                pFrom = pFrom,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            };

            int result = SHFileOperationW(ref fileOp);
            if (result != 0 || fileOp.fAnyOperationsAborted)
            {
                error = $"SHFileOperationW failed with code {result}. Aborted: {fileOp.fAnyOperationsAborted}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
