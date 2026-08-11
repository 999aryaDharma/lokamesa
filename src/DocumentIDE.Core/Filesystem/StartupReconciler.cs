using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Core.Filesystem;

public class StartupReconciler : IStartupReconciler
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx",
        ".md"
    };

    private readonly IFolderSourceRepository _folderRepo;
    private readonly IDocumentRepository _documentRepo;
    private readonly DocumentIdentityService _identityService;
    private readonly IFileIdentityProvider _identityProvider;
    private readonly ConcurrentDictionary<string, ReconciliationProgress> _progressMap = new();

    public StartupReconciler(
        IFolderSourceRepository folderRepo,
        IDocumentRepository documentRepo,
        DocumentIdentityService identityService,
        IFileIdentityProvider identityProvider)
    {
        _folderRepo = folderRepo ?? throw new ArgumentNullException(nameof(folderRepo));
        _documentRepo = documentRepo ?? throw new ArgumentNullException(nameof(documentRepo));
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
    }

    public async Task<IReadOnlyList<Document>> GetCachedWorkspaceDocumentsAsync(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return Array.Empty<Document>();
        }

        return await _documentRepo.GetByWorkspaceAsync(workspaceId);
    }

    public ReconciliationProgress GetProgress(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return new ReconciliationProgress("", false, 0, 0, 0, 0);
        }

        return _progressMap.TryGetValue(workspaceId, out var progress)
            ? progress
            : new ReconciliationProgress(workspaceId, false, 0, 0, 0, 0);
    }

    public async Task<WorkspaceReconciliationResult> ReconcileWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        }

        var stopwatch = Stopwatch.StartNew();

        _progressMap[workspaceId] = new ReconciliationProgress(
            WorkspaceId: workspaceId,
            IsReconciling: true,
            TotalFilesDiscovered: 0,
            ProcessedFiles: 0,
            ChangedFiles: 0,
            MissingFiles: 0
        );

        var sources = await _folderRepo.GetByWorkspaceAsync(workspaceId);

        int totalSources = sources.Count;
        int totalFilesDiscovered = 0;
        int processedFiles = 0;
        int unchangedCount = 0;
        int modifiedCount = 0;
        int newCount = 0;
        int renamedCount = 0;
        int missingCount = 0;

        foreach (var source in sources)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            string canonicalSourcePath = CanonicalizePath(source.CanonicalPath);

            if (!Directory.Exists(canonicalSourcePath))
            {
                // Source folder root is missing or unavailable on disk
                if (source.AvailabilityStatus != "missing")
                {
                    await _folderRepo.UpdateAvailabilityAsync(source.Id, "missing");
                }

                // Mark any cached DB documents under this missing folder as 'missing' without deleting them
                var dbDocsUnderSource = await _documentRepo.GetByFolderSourcePathAsync(canonicalSourcePath);
                foreach (var dbDoc in dbDocsUnderSource)
                {
                    if (dbDoc.AvailabilityStatus != "missing")
                    {
                        dbDoc.AvailabilityStatus = "missing";
                        if (string.IsNullOrEmpty(dbDoc.LastKnownPath))
                        {
                            dbDoc.LastKnownPath = dbDoc.CurrentPath;
                        }
                        await _documentRepo.UpdateAsync(dbDoc);
                    }
                    missingCount++;
                }

                continue;
            }

            // Folder source is available on disk
            if (source.AvailabilityStatus != "available")
            {
                await _folderRepo.UpdateAvailabilityAsync(source.Id, "available");
            }

            // 1. Collect lightweight disk file info recursively
            var diskFiles = EnumerateDiskFiles(canonicalSourcePath);
            totalFilesDiscovered += diskFiles.Count;

            _progressMap[workspaceId] = _progressMap[workspaceId] with
            {
                TotalFilesDiscovered = totalFilesDiscovered,
                CurrentFolderPath = canonicalSourcePath
            };

            // 2. Fetch existing DB documents under this folder source
            var cachedDocsList = await _documentRepo.GetByFolderSourcePathAsync(canonicalSourcePath);
            var cachedDocsByPath = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
            var cachedDocsByIdentity = new Dictionary<string, Document>();

            foreach (var doc in cachedDocsList)
            {
                if (!string.IsNullOrEmpty(doc.CurrentPath))
                {
                    cachedDocsByPath[CanonicalizePath(doc.CurrentPath)] = doc;
                }
                if (!string.IsNullOrEmpty(doc.FilesystemVolumeId) && !string.IsNullOrEmpty(doc.FilesystemObjectId))
                {
                    string key = $"{doc.FilesystemVolumeId}:{doc.FilesystemObjectId}";
                    cachedDocsByIdentity[key] = doc;
                }
            }

            var matchedDbDocIds = new HashSet<string>();

            // 3. Classify each physical disk file
            foreach (var diskFile in diskFiles)
            {
                if (cancellationToken.IsCancellationRequested) break;

                processedFiles++;

                string canonicalFilePath = diskFile.CanonicalPath;
                Document? matchingDoc = null;

                // Probe identity if available
                string? identityKey = null;
                if (!string.IsNullOrEmpty(diskFile.VolumeId) && !string.IsNullOrEmpty(diskFile.ObjectId))
                {
                    identityKey = $"{diskFile.VolumeId}:{diskFile.ObjectId}";
                    cachedDocsByIdentity.TryGetValue(identityKey, out matchingDoc);
                }

                if (matchingDoc == null)
                {
                    cachedDocsByPath.TryGetValue(canonicalFilePath, out matchingDoc);
                }

                if (matchingDoc != null)
                {
                    matchedDbDocIds.Add(matchingDoc.Id);

                    bool pathMatches = string.Equals(
                        CanonicalizePath(matchingDoc.CurrentPath),
                        canonicalFilePath,
                        StringComparison.OrdinalIgnoreCase
                    );

                    bool isUnchanged = pathMatches &&
                                       matchingDoc.SizeBytes == diskFile.SizeBytes &&
                                       IsTimestampEqual(matchingDoc.ModifiedAt, diskFile.ModifiedAtIso) &&
                                       matchingDoc.AvailabilityStatus == "available";

                    if (isUnchanged)
                    {
                        // Unchanged file: bypass SHA256 fingerprinting & re-parsing
                        unchangedCount++;
                    }
                    else
                    {
                        // File modified or path changed: resolve via DocumentIdentityService
                        try
                        {
                            var updatedDoc = await _identityService.ResolveDocumentAsync(canonicalFilePath);
                            updatedDoc.IndexStatus = "pending";
                            await _documentRepo.UpdateAsync(updatedDoc);

                            if (!pathMatches)
                            {
                                renamedCount++;
                            }
                            else
                            {
                                modifiedCount++;
                            }
                        }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            // Locked or inaccessible file
                            matchingDoc.AvailabilityStatus = "locked";
                            await _documentRepo.UpdateAsync(matchingDoc);
                        }
                    }
                }
                else
                {
                    // New file on disk (not in DB)
                    try
                    {
                        var newDoc = await _identityService.ResolveDocumentAsync(canonicalFilePath);
                        newDoc.IndexStatus = "pending";
                        await _documentRepo.UpdateAsync(newDoc);
                        newCount++;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // Ignore unreadable new files during startup scan
                    }
                }

                _progressMap[workspaceId] = _progressMap[workspaceId] with
                {
                    ProcessedFiles = processedFiles,
                    ChangedFiles = modifiedCount + newCount + renamedCount,
                    MissingFiles = missingCount
                };
            }

            // 4. Identify missing files (DB documents not found on disk during scan)
            foreach (var doc in cachedDocsList)
            {
                if (!matchedDbDocIds.Contains(doc.Id) && doc.AvailabilityStatus == "available")
                {
                    if (!File.Exists(doc.CurrentPath))
                    {
                        doc.AvailabilityStatus = "missing";
                        if (string.IsNullOrEmpty(doc.LastKnownPath))
                        {
                            doc.LastKnownPath = doc.CurrentPath;
                        }
                        await _documentRepo.UpdateAsync(doc);
                        missingCount++;
                    }
                }
            }

            // Update folder source LastReconciledAt timestamp
            await _folderRepo.UpdateLastReconciledAtAsync(source.Id, DateTime.UtcNow.ToString("O"));
        }

        stopwatch.Stop();

        _progressMap[workspaceId] = new ReconciliationProgress(
            WorkspaceId: workspaceId,
            IsReconciling: false,
            TotalFilesDiscovered: totalFilesDiscovered,
            ProcessedFiles: processedFiles,
            ChangedFiles: modifiedCount + newCount + renamedCount,
            MissingFiles: missingCount,
            CurrentFolderPath: null,
            ErrorMessage: null
        );

        return new WorkspaceReconciliationResult(
            WorkspaceId: workspaceId,
            TotalSources: totalSources,
            TotalFiles: totalFilesDiscovered,
            UnchangedCount: unchangedCount,
            ModifiedCount: modifiedCount,
            NewCount: newCount,
            RenamedCount: renamedCount,
            MissingCount: missingCount,
            Elapsed: stopwatch.Elapsed
        );
    }

    private List<DiskFileInfo> EnumerateDiskFiles(string rootPath)
    {
        var results = new List<DiskFileInfo>();
        var rootDir = new DirectoryInfo(rootPath);
        if (!rootDir.Exists) return results;

        EnumerateFilesRecursive(rootDir, rootPath, results);
        return results;
    }

    private void EnumerateFilesRecursive(DirectoryInfo dir, string rootPath, List<DiskFileInfo> results)
    {
        try
        {
            // Skip hidden directories or system folders (.git, .vs, $RECYCLE.BIN)
            if ((dir.Attributes & FileAttributes.Hidden) != 0 || dir.Name.StartsWith('.'))
            {
                return;
            }

            // Guard against symlink / reparse points escaping the root
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                string targetPath = CanonicalizePath(dir.FullName);
                if (!targetPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            foreach (var file in dir.EnumerateFiles())
            {
                try
                {
                    if (SupportedExtensions.Contains(file.Extension))
                    {
                        string canonicalPath = CanonicalizePath(file.FullName);
                        var identity = _identityProvider.GetFileIdentity(canonicalPath);

                        results.Add(new DiskFileInfo(
                            CanonicalPath: canonicalPath,
                            SizeBytes: file.Length,
                            ModifiedAtIso: file.LastWriteTimeUtc.ToString("O"),
                            VolumeId: identity?.VolumeId,
                            ObjectId: identity?.ObjectId
                        ));
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FileNotFoundException)
                {
                    // Skip unstattable individual file
                }
            }

            foreach (var subDir in dir.EnumerateDirectories())
            {
                EnumerateFilesRecursive(subDir, rootPath, results);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
        {
            // Skip unreadable directory
        }
    }

    private static bool IsTimestampEqual(string? iso1, string iso2)
    {
        if (string.IsNullOrEmpty(iso1)) return false;
        if (string.Equals(iso1, iso2, StringComparison.Ordinal)) return true;

        if (DateTimeOffset.TryParse(iso1, out var dt1) && DateTimeOffset.TryParse(iso2, out var dt2))
        {
            // Allow up to 100ms difference due to NTFS timestamp resolution variations
            return Math.Abs((dt1 - dt2).TotalMilliseconds) < 100;
        }

        return false;
    }

    private static string CanonicalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private record DiskFileInfo(
        string CanonicalPath,
        long SizeBytes,
        string ModifiedAtIso,
        string? VolumeId,
        string? ObjectId
    );
}
