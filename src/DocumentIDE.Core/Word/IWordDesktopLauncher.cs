using System.Threading.Tasks;

namespace DocumentIDE.Core.Word;

public interface IWordDesktopLauncher
{
    /// <summary>
    /// Probe whether Microsoft Word Desktop is installed and available on the host OS.
    /// </summary>
    bool IsWordAvailable();

    /// <summary>
    /// Resolves the specified document ID to its canonical path and opens it directly in Microsoft Word Desktop.
    /// Serializes launch operations so Word COM/automation concurrency is strictly <= 1.
    /// Does not mutate source file content or SHA256 fingerprint.
    /// </summary>
    /// <param name="documentId">The stable document ID to open.</param>
    /// <returns>A typed launch result status.</returns>
    Task<WordLaunchResult> OpenDocumentAsync(string documentId);
}
