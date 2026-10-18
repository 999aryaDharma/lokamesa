using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using DocumentIDE.Core.Filesystem;

namespace DocumentIDE.Infrastructure.Filesystem;

public class WindowsFileIdentityProvider : IFileIdentityProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    public FileIdentity? GetFileIdentity(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            // Open the file with minimal access to get its handle
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (handle.IsInvalid)
            {
                return null;
            }

            if (GetFileInformationByHandle(handle, out var info))
            {
                string volumeId = info.VolumeSerialNumber.ToString();
                ulong fileIndex = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
                string objectId = fileIndex.ToString();
                return new FileIdentity(volumeId, objectId);
            }
        }
        catch
        {
            // Fallback or return null if cannot open handle (e.g. sharing violation, or permission denied)
        }

        return null;
    }
}
