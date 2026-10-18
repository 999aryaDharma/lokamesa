using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.Markdown;
using DocumentIDE.Core.Search;

namespace DocumentIDE.Core.Indexing;

public class IncrementalIndexCoordinator : IDisposable
{
    private readonly IDocumentRepository _documentRepo;
    private readonly IDocumentMetadataRepository _metadataRepo;
    private readonly IFtsIndex _ftsIndex;
    private readonly IMarkdownStructureParser? _markdownParser;
    private readonly IDocxStructureParser? _docxParser;
    private readonly FileChangeCoordinator? _fileChangeCoordinator;

    private readonly ConcurrentDictionary<string, string> _indexStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _lastIndexedFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _documentLocks = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<IndexStateChangedEventArgs>? OnIndexStateChanged;
    public event EventHandler<CacheInvalidationEventArgs>? OnCacheInvalidated;

    public IncrementalIndexCoordinator(
        IDocumentRepository documentRepo,
        IDocumentMetadataRepository metadataRepo,
        IFtsIndex ftsIndex,
        IMarkdownStructureParser? markdownParser = null,
        IDocxStructureParser? docxParser = null,
        FileChangeCoordinator? fileChangeCoordinator = null)
    {
        _documentRepo = documentRepo ?? throw new ArgumentNullException(nameof(documentRepo));
        _metadataRepo = metadataRepo ?? throw new ArgumentNullException(nameof(metadataRepo));
        _ftsIndex = ftsIndex ?? throw new ArgumentNullException(nameof(ftsIndex));
        _markdownParser = markdownParser;
        _docxParser = docxParser;
        _fileChangeCoordinator = fileChangeCoordinator;

        if (_fileChangeCoordinator != null)
        {
            _fileChangeCoordinator.OnFileChanged += HandleFileChanged;
        }
    }

    private void HandleFileChanged(object? sender, FileChangeEvent e)
    {
        _ = ProcessFileChangeAsync(e);
    }

    public virtual async Task ProcessFileChangeAsync(FileChangeEvent e, CancellationToken cancellationToken = default)
    {
        if (e == null) return;

        string? documentId = e.DocumentId;
        string? path = e.Path;

        Document? doc = null;
        if (!string.IsNullOrEmpty(documentId))
        {
            doc = await _documentRepo.GetByIdAsync(documentId);
        }
        if (doc == null && !string.IsNullOrEmpty(path))
        {
            doc = await _documentRepo.GetByPathAsync(path);
            if (doc != null)
            {
                documentId = doc.Id;
            }
        }

        // Handle Deleted file change
        if (e.ChangeType == FileChangeType.Deleted)
        {
            if (!string.IsNullOrEmpty(documentId))
            {
                // Remove from active FTS index
                await _ftsIndex.DeleteDocumentAsync(documentId, cancellationToken);

                // Retain metadata in IDocumentMetadataRepository but mark status missing
                await _metadataRepo.MarkStatusAsync(documentId, "missing");

                if (doc != null)
                {
                    doc.AvailabilityStatus = "missing";
                    doc.IndexStatus = "missing";
                    await _documentRepo.UpdateAsync(doc);
                }

                _indexStates[documentId] = "missing";
                OnIndexStateChanged?.Invoke(this, new IndexStateChangedEventArgs(documentId, "missing"));

                if (!string.IsNullOrEmpty(path))
                {
                    OnCacheInvalidated?.Invoke(this, new CacheInvalidationEventArgs(documentId, path));
                }
            }
            return;
        }

        if (doc == null)
        {
            return;
        }

        documentId = doc.Id;
        path = !string.IsNullOrEmpty(doc.CurrentPath) ? doc.CurrentPath : path;
        if (string.IsNullOrEmpty(path)) return;

        // Deduplicate and lock per documentId (Step 4)
        var semaphore = _documentLocks.GetOrAdd(documentId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            // Compute content fingerprint
            string currentFingerprint = doc.ContentFingerprint ?? string.Empty;
            if (string.IsNullOrEmpty(currentFingerprint) && File.Exists(path))
            {
                currentFingerprint = ComputeContentFingerprint(path);
            }

            // Step 2: Skip deep parse when fingerprint unchanged
            if (_lastIndexedFingerprints.TryGetValue(documentId, out var lastFp) &&
                string.Equals(lastFp, currentFingerprint, StringComparison.Ordinal) &&
                _indexStates.TryGetValue(documentId, out var currentState) &&
                currentState == "ready")
            {
                return;
            }

            // Publish indexing state
            _indexStates[documentId] = "indexing";
            OnIndexStateChanged?.Invoke(this, new IndexStateChangedEventArgs(documentId, "indexing"));

            // Step 1: Map verified file changes to parse jobs (Parsing executed OUTSIDE DB transactions)
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".md")
            {
                if (_markdownParser == null)
                {
                    throw new InvalidOperationException("Markdown parser is not provided.");
                }

                string markdownText = await File.ReadAllTextAsync(path, cancellationToken);
                MarkdownParseResult parseResult = _markdownParser.Parse(markdownText);

                var mdMeta = new MarkdownDocumentMetadata
                {
                    DocumentId = documentId,
                    FrontmatterJson = parseResult.Frontmatter.StructuredData.Count > 0
                        ? System.Text.Json.JsonSerializer.Serialize(parseResult.Frontmatter.StructuredData)
                        : null,
                    HeadingsJson = parseResult.Headings.Count > 0
                        ? System.Text.Json.JsonSerializer.Serialize(parseResult.Headings)
                        : null,
                    OutboundLinksJson = parseResult.Links.Any(l => l.Type == MarkdownLinkType.NormalLink)
                        ? System.Text.Json.JsonSerializer.Serialize(parseResult.Links.Where(l => l.Type == MarkdownLinkType.NormalLink).Select(l => l.Target))
                        : null,
                    WikilinksJson = parseResult.Links.Any(l => l.Type == MarkdownLinkType.Wikilink)
                        ? System.Text.Json.JsonSerializer.Serialize(parseResult.Links.Where(l => l.Type == MarkdownLinkType.Wikilink).Select(l => l.Target))
                        : null,
                    CodeBlockCount = parseResult.CodeBlockCount,
                    UpdatedAt = DateTime.UtcNow.ToString("O"),
                    ParserVersion = parseResult.ParserVersion
                };

                var ftsEntry = new FtsIndexEntry(
                    DocumentId: documentId,
                    Filename: doc.Filename,
                    Body: parseResult.SearchableBody,
                    Headings: parseResult.Headings.Count > 0
                        ? string.Join(" ", parseResult.Headings.Select(h => h.Text))
                        : null,
                    Frontmatter: parseResult.Frontmatter.RawText
                );

                await _metadataRepo.SaveMarkdownMetadataAsync(mdMeta);
                await _ftsIndex.IndexDocumentAsync(ftsEntry, cancellationToken);
            }
            else if (ext == ".docx")
            {
                if (_docxParser == null)
                {
                    throw new InvalidOperationException("Docx parser is not provided.");
                }

                DocxParseResult parseResult = _docxParser.Parse(path);
                parseResult.Metadata.DocumentId = documentId;

                var ftsEntry = new FtsIndexEntry(
                    DocumentId: documentId,
                    Filename: doc.Filename,
                    Body: parseResult.SearchableBody,
                    Headings: parseResult.HeadingsText,
                    Tables: parseResult.TablesText,
                    Headers: parseResult.HeadersText,
                    Footers: parseResult.FootersText
                );

                await _metadataRepo.SaveDocxMetadataAsync(parseResult.Metadata);
                await _ftsIndex.IndexDocumentAsync(ftsEntry, cancellationToken);
            }
            else
            {
                return;
            }

            // Step 3: Invalidate cache
            OnCacheInvalidated?.Invoke(this, new CacheInvalidationEventArgs(documentId, path));

            // Step 5: Publish ready index state
            _lastIndexedFingerprints[documentId] = currentFingerprint;
            _indexStates[documentId] = "ready";
            await _metadataRepo.MarkStatusAsync(documentId, "ready");
            doc.IndexStatus = "ready";
            await _documentRepo.UpdateAsync(doc);

            OnIndexStateChanged?.Invoke(this, new IndexStateChangedEventArgs(documentId, "ready"));
        }
        catch (Exception ex)
        {
            // Step 5 & Acceptance Criteria: Publish error state without breaking other results
            _indexStates[documentId] = "error";
            await _metadataRepo.MarkStatusAsync(documentId, "error", ex.Message);
            doc.IndexStatus = "error";
            try { await _documentRepo.UpdateAsync(doc); } catch { }

            OnIndexStateChanged?.Invoke(this, new IndexStateChangedEventArgs(documentId, "error", ex.Message));
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task ReindexDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var doc = await _documentRepo.GetByIdAsync(documentId);
        if (doc != null && !string.IsNullOrEmpty(doc.CurrentPath))
        {
            _lastIndexedFingerprints.TryRemove(documentId, out _);
            await ProcessFileChangeAsync(new FileChangeEvent
            {
                ChangeType = FileChangeType.Modified,
                DocumentId = documentId,
                Path = doc.CurrentPath
            }, cancellationToken);
        }
    }

    public async Task ReindexWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var docs = await _documentRepo.GetByWorkspaceAsync(workspaceId);
        foreach (var doc in docs)
        {
            // Only a truly absent file may take the Deleted/eviction path. The snapshot's
            // AvailabilityStatus can be stale (e.g. the file was restored without a watcher
            // event), and the filesystem is the source of truth: a present file always goes
            // through the normal re-read/re-index path below, even when the snapshot says
            // "missing". Conversely, a document with no readable path cannot be re-read.
            if (doc.AvailabilityStatus == "missing" &&
                (string.IsNullOrEmpty(doc.CurrentPath) || !File.Exists(doc.CurrentPath)))
            {
                // Converge the active index to the filesystem: a document whose file no
                // longer exists must leave the searchable index instead of keeping stale
                // rows (or failing a read and publishing an error state). This is the
                // reindex-time counterpart of a live Deleted watcher event, used after
                // reconciliation when the original event was lost (watcher overflow).
                await ProcessFileChangeAsync(new FileChangeEvent
                {
                    ChangeType = FileChangeType.Deleted,
                    DocumentId = doc.Id,
                    Path = doc.CurrentPath
                }, cancellationToken);
                continue;
            }

            await ReindexDocumentAsync(doc.Id, cancellationToken);
        }
    }

    public virtual string? GetIndexState(string documentId)
    {
        if (_indexStates.TryGetValue(documentId, out var state))
        {
            return state;
        }
        return null;
    }

    public IReadOnlyDictionary<string, string> GetAllIndexStates()
    {
        return _indexStates.ToDictionary(k => k.Key, v => v.Value);
    }

    private static string ComputeContentFingerprint(string path)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        if (_fileChangeCoordinator != null)
        {
            _fileChangeCoordinator.OnFileChanged -= HandleFileChanged;
        }

        foreach (var semaphore in _documentLocks.Values)
        {
            try { semaphore.Dispose(); } catch { }
        }
        _documentLocks.Clear();
    }
}
