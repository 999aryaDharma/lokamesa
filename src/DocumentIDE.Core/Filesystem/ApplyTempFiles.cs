using System;
using System.IO;

namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Single source of truth for transactional apply temp files (<c>~apply-&lt;32 hex&gt;.tmp</c>).
/// <see cref="DocumentIDE.Infrastructure.Filesystem.AtomicFileReplacer"/> creates them;
/// startup recovery removes leftovers. Keeping the format in one place guarantees the
/// cleaner and the writer can never disagree about what an application temp file is
/// (and therefore what is safe to delete).
/// </summary>
public static class ApplyTempFiles
{
    public const string Prefix = "~apply-";
    public const string Suffix = ".tmp";

    private const int GuidHexLength = 32;

    /// <summary>Creates a new apply temp path in the same directory as its target (same volume ⇒ atomic replace).</summary>
    public static string CreatePath(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Path.Combine(directory, $"{Prefix}{Guid.NewGuid():N}{Suffix}");
    }

    /// <summary>
    /// True only for exact application temp file names: <c>~apply-</c> + 32 hex chars + <c>.tmp</c>.
    /// Anything else (user files, lock files, foreign temps) must never be treated as cleanable.
    /// </summary>
    public static bool IsApplyTempFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        if (fileName.Length != Prefix.Length + GuidHexLength + Suffix.Length) return false;
        if (!fileName.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        if (!fileName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) return false;
        for (var i = Prefix.Length; i < Prefix.Length + GuidHexLength; i++)
        {
            if (!Uri.IsHexDigit(fileName[i])) return false;
        }
        return true;
    }
}
