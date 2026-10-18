using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Provides utilities for detecting Windows reparse points (junctions, symlinks, mount points)
/// and enforcing containment within configured folder sources.
/// FS-08 – Reparse Point & Source Containment.
/// </summary>
public static class SourceContainmentPolicy
{
    /// <summary>
    /// Determines whether the given path is a Windows reparse point.
    /// </summary>
    /// <param name="path">A fully qualified file or directory path.</param>
    /// <returns>True if the path has the ReparsePoint attribute set.</returns>
    public static bool IsReparsePoint(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            // If the file does not exist we cannot treat it as a reparse point.
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            // Same as above for directories.
            return false;
        }
        catch (Exception)
        {
            // Any other unexpected error – be conservative and report not a reparse point.
            return false;
        }
    }

    /// <summary>
    /// Resolves the ultimate target of a reparse point using <c>Path.GetFullPath</c>.
    /// This works for most symlinks and junctions on Windows where the OS presents the
    /// target path when the reparse point is accessed.
    /// </summary>
    /// <param name="reparsePath">The reparse point path.</param>
    /// <returns>The canonical target path.</returns>
    public static string ResolveTarget(string reparsePath)
    {
        // GetFullPath already follows the link for files and directories.
        return Path.GetFullPath(reparsePath);
    }

    /// <summary>
    /// Checks whether the supplied <paramref name="candidatePath"/> is contained within any of the
    /// provided <paramref name="folderSources"/>. Containment is evaluated after canonicalizing both
    /// the candidate and the source root, using case‑insensitive comparison (Windows semantics).
    /// </summary>
    /// <param name="candidatePath">Path to validate.</param>
    /// <param name="folderSources">Collection of configured folder sources.</param>
    /// <returns>True if the candidate resides inside at least one source root.</returns>
    public static bool IsContained(string candidatePath, IReadOnlyList<FolderSource> folderSources)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || folderSources == null || folderSources.Count == 0)
            return false;

        string canonicalCandidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var source in folderSources)
        {
            if (string.IsNullOrWhiteSpace(source?.CanonicalPath))
                continue;
            string canonicalSource = source.CanonicalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Ensure the source path ends with a separator before checking prefix to avoid false matches like C:\src vs C:\src2.
            string sourcePrefix = canonicalSource + Path.DirectorySeparatorChar;
            if (canonicalCandidate.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(canonicalCandidate, canonicalSource, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Validates that <paramref name="path"/> is safely contained within the workspace's folder sources.
    /// Throws <see cref="InvalidPathException"/> if containment cannot be guaranteed.
    /// </summary>
    /// <param name="path">Path to validate (file or directory).</param>
    /// <param name="folderSources">Current folder sources for the workspace.</param>
    public static void ValidateContainment(string path, IReadOnlyList<FolderSource> folderSources)
    {
        if (!IsContained(path, folderSources))
        {
            throw new InvalidPathException($"Path '{path}' escapes the configured folder sources.");
        }
    }
}
