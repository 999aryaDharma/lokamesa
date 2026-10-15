using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Word;
using Microsoft.Win32;

namespace DocumentIDE.Infrastructure.Word;

public class WordDesktopLauncher : IWordDesktopLauncher
{
    private static readonly SemaphoreSlim ComLock = new(1, 1);
    private readonly IDocumentRepository _documentRepository;
    private readonly Func<string, bool> _processLauncher;
    private readonly Func<bool> _availabilityChecker;
    private readonly WordAutomationQueue? _automationQueue;

    public WordDesktopLauncher(
        IDocumentRepository documentRepository,
        Func<string, bool>? processLauncher = null,
        Func<bool>? availabilityChecker = null,
        WordAutomationQueue? automationQueue = null)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _processLauncher = processLauncher ?? DefaultProcessLauncher;
        _availabilityChecker = availabilityChecker ?? DefaultAvailabilityChecker;
        _automationQueue = automationQueue;
    }

    public bool IsWordAvailable()
    {
        return _availabilityChecker();
    }

    public async Task<WordLaunchResult> OpenDocumentAsync(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return WordLaunchResult.NotFound(documentId ?? string.Empty, "Document ID is required.");
        }

        if (!IsWordAvailable())
        {
            return WordLaunchResult.WordNotInstalled("Microsoft Word is required for this operation.");
        }

        var doc = await _documentRepository.GetByIdAsync(documentId);
        if (doc == null)
        {
            return WordLaunchResult.NotFound(documentId, $"Document with ID '{documentId}' was not found.");
        }

        var extension = doc.Extension?.Trim().ToLowerInvariant();
        if (extension != ".docx" && extension != ".doc")
        {
            return WordLaunchResult.InvalidType(documentId, $"File type '{doc.Extension}' is not supported for opening in Microsoft Word.");
        }

        if (string.IsNullOrWhiteSpace(doc.CurrentPath) || !File.Exists(doc.CurrentPath))
        {
            return WordLaunchResult.NotFound(documentId, $"Document file at '{doc.CurrentPath}' does not exist on disk.");
        }

        var canonicalPath = Path.GetFullPath(doc.CurrentPath);

        // WORD-03: route the launch through the dedicated STA Word automation
        // queue (concurrency exactly 1) when one is available; otherwise fall
        // back to the local COM lock so behavior is unchanged in tests/hosts
        // without a registered queue.
        if (_automationQueue != null)
        {
            try
            {
                bool queued = await _automationQueue.EnqueueAsync(() => _processLauncher(canonicalPath));
                if (!queued)
                {
                    return WordLaunchResult.Failed(
                        WordLaunchStatus.ComError,
                        $"Failed to launch Microsoft Word Desktop for file '{canonicalPath}'.",
                        documentId,
                        canonicalPath
                    );
                }

                return WordLaunchResult.Ok(documentId, canonicalPath);
            }
            catch (Exception ex)
            {
                return WordLaunchResult.Failed(
                    WordLaunchStatus.ComError,
                    $"Exception while launching Microsoft Word: {ex.Message}",
                    documentId,
                    canonicalPath
                );
            }
        }

        await ComLock.WaitAsync();
        try
        {
            bool success = _processLauncher(canonicalPath);
            if (!success)
            {
                return WordLaunchResult.Failed(
                    WordLaunchStatus.ComError,
                    $"Failed to launch Microsoft Word Desktop for file '{canonicalPath}'.",
                    documentId,
                    canonicalPath
                );
            }

            return WordLaunchResult.Ok(documentId, canonicalPath);
        }
        catch (Exception ex)
        {
            return WordLaunchResult.Failed(
                WordLaunchStatus.ComError,
                $"Exception while launching Microsoft Word: {ex.Message}",
                documentId,
                canonicalPath
            );
        }
        finally
        {
            ComLock.Release();
        }
    }

    private static bool DefaultAvailabilityChecker()
    {
        try
        {
            var wordType = Type.GetTypeFromProgID("Word.Application");
            if (wordType != null)
            {
                return true;
            }

            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Winword.exe");
            if (key != null)
            {
                return true;
            }

            using var userKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Winword.exe");
            return userKey != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool DefaultProcessLauncher(string filePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            };
            using var proc = Process.Start(psi);
            return proc != null || true;
        }
        catch
        {
            return false;
        }
    }
}
