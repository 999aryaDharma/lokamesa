namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Abstraction for Windows Recycle Bin shell deletion.
/// FS-07: Explicit Physical File Operations.
/// </summary>
public interface IRecycleBin
{
    bool SendToRecycleBin(string path, out string? error);
}
