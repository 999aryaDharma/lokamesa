using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Service for attaching and managing local folder sources within a workspace.
/// FS-01: Folder Source Attachment.
/// </summary>
public class FolderSourceService : IFolderSourceService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx",
        ".md"
    };

    private readonly IFolderSourceRepository _repository;

    public FolderSourceService(IFolderSourceRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<AttachFolderResult> AttachFolderAsync(string workspaceId, string rawFolderPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        }

        if (string.IsNullOrWhiteSpace(rawFolderPath))
        {
            throw new FolderNotFoundException(rawFolderPath ?? string.Empty);
        }

        string canonicalPath = CanonicalizePath(rawFolderPath);

        if (!Directory.Exists(canonicalPath))
        {
            throw new FolderNotFoundException(canonicalPath);
        }

        string displayName = Path.GetFileName(canonicalPath);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = canonicalPath;
        }

        int supportedFileCount = CountSupportedFiles(canonicalPath);

        var folderSource = new FolderSource
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = workspaceId,
            CanonicalPath = canonicalPath,
            DisplayName = displayName,
            AvailabilityStatus = "available",
            AddedAt = DateTime.UtcNow.ToString("O"),
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };

        await _repository.CreateAsync(folderSource);

        return new AttachFolderResult
        {
            FolderSource = folderSource,
            SupportedFileCount = supportedFileCount,
            DisplayName = displayName
        };
    }

    public async Task<IReadOnlyList<FolderSource>> GetFolderSourcesAsync(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return Array.Empty<FolderSource>();
        }

        var sources = await _repository.GetByWorkspaceAsync(workspaceId);

        foreach (var source in sources)
        {
            string status = Directory.Exists(source.CanonicalPath) ? "available" : "missing";
            if (source.AvailabilityStatus != status)
            {
                source.AvailabilityStatus = status;
                await _repository.UpdateAvailabilityAsync(source.Id, status);
            }
        }

        return sources;
    }

    public int CountSupportedFiles(string canonicalPath)
    {
        if (string.IsNullOrWhiteSpace(canonicalPath) || !Directory.Exists(canonicalPath))
        {
            return 0;
        }

        int count = 0;
        var dirInfo = new DirectoryInfo(canonicalPath);

        CountFilesRecursive(dirInfo, ref count);
        return count;
    }

    private static void CountFilesRecursive(DirectoryInfo dir, ref int count)
    {
        try
        {
            // Ignore hidden/system directories (e.g. .git, $RECYCLE.BIN, .vs)
            if ((dir.Attributes & FileAttributes.Hidden) != 0 || dir.Name.StartsWith('.'))
            {
                return;
            }

            foreach (var file in dir.EnumerateFiles())
            {
                if (SupportedExtensions.Contains(file.Extension))
                {
                    count++;
                }
            }

            foreach (var subDir in dir.EnumerateDirectories())
            {
                CountFilesRecursive(subDir, ref count);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore inaccessible subdirectories during enumeration
        }
        catch (DirectoryNotFoundException)
        {
            // Ignore directories that disappeared during enumeration
        }
    }

    public static string CanonicalizePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
