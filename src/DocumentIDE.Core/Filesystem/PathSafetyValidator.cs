using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DocumentIDE.Core.Filesystem;

public class InvalidPathException : Exception
{
    public InvalidPathException(string message) : base(message) { }
    public InvalidPathException(string message, Exception inner) : base(message, inner) { }
}

public class PathCollisionException : Exception
{
    public string TargetPath { get; }

    public PathCollisionException(string targetPath)
        : base($"Target path already exists: '{targetPath}'")
    {
        TargetPath = targetPath;
    }

    public PathCollisionException(string targetPath, string message)
        : base(message)
    {
        TargetPath = targetPath;
    }
}

/// <summary>
/// Strict path safety validator enforcing basename invariants, path traversal guards,
/// Windows reserved name rejection, ADS syntax blocking, and trailing normalization checks.
/// FS-07: Explicit Physical File Operations.
/// </summary>
public static class PathSafetyValidator
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool IsValidBasename(string? basename, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(basename))
        {
            errorMessage = "Basename cannot be null, empty, or whitespace.";
            return false;
        }

        // 1. Must not contain directory separators
        if (basename.Contains('/') || basename.Contains('\\'))
        {
            errorMessage = $"Name '{basename}' cannot contain directory path separators ('/' or '\\').";
            return false;
        }

        // 2. Reject relative traversal navigation '.' and '..'
        if (basename == "." || basename == "..")
        {
            errorMessage = $"Name '{basename}' cannot be relative path navigation '.' or '..'.";
            return false;
        }

        // 3. Basename invariant check
        if (Path.GetFileName(basename) != basename)
        {
            errorMessage = $"Name '{basename}' must be a simple basename, not a path.";
            return false;
        }

        // 4. Reject Alternate Data Streams (ADS) colon syntax ':'
        if (basename.Contains(':'))
        {
            errorMessage = $"Name '{basename}' contains invalid character ':' (Alternate Data Stream syntax is forbidden).";
            return false;
        }

        // 5. Reject trailing dots or spaces (triggers OS filename truncation / aliasing bugs)
        if (basename.EndsWith('.') || basename.EndsWith(' '))
        {
            errorMessage = $"Name '{basename}' cannot end with a dot or trailing space.";
            return false;
        }

        // 6. Check invalid Windows characters and ASCII control codes (0-31)
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in basename)
        {
            if (c < 32 || invalidChars.Contains(c))
            {
                errorMessage = $"Name '{basename}' contains invalid character '{(c < 32 ? $"0x{(int)c:X2}" : c.ToString())}'.";
                return false;
            }
        }

        // 7. Reject reserved Windows device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9, CON.txt, NUL.md, etc.)
        string stem = Path.GetFileNameWithoutExtension(basename);
        if (ReservedDeviceNames.Contains(stem))
        {
            errorMessage = $"Name '{basename}' uses reserved Windows device name '{stem}'.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    public static void ValidateBasename(string? basename)
    {
        if (!IsValidBasename(basename, out string? errorMessage))
        {
            throw new InvalidPathException(errorMessage ?? "Invalid path component.");
        }
    }
}
