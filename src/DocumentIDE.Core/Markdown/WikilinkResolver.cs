using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DocumentIDE.Core.Markdown;

public enum WikilinkDiagnosticKind
{
    Unresolved,
    Ambiguous
}

public record WikilinkDiagnostic(
    WikilinkDiagnosticKind Kind,
    string Message,
    string RawTarget,
    string? Alias,
    int LineNumber,
    IReadOnlyList<string> CandidatePaths
);

public record WikilinkResolutionResult(
    string SourceFilePath,
    string RawTarget,
    string TargetIdentity,
    string? Anchor,
    string? Alias,
    int LineNumber,
    bool IsResolved,
    bool IsAmbiguous,
    bool IsUnresolved,
    string? ResolvedPath,
    IReadOnlyList<string> CandidatePaths,
    WikilinkDiagnostic? Diagnostic
);

public record BacklinkInput(
    string SourceFilePath,
    string TargetFilePath,
    string RawTarget,
    string? Alias,
    string? Anchor,
    int LineNumber
);

public static class WikilinkResolver
{
    public static (string TargetIdentity, string? Anchor, string? Alias) ParseWikilinkContent(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
            return (string.Empty, null, null);

        string content = rawContent.Trim();
        if (content.StartsWith("[[") && content.EndsWith("]]"))
        {
            content = content.Substring(2, content.Length - 4).Trim();
        }

        string targetWithAnchor = content;
        string? alias = null;

        int pipeIdx = content.IndexOf('|');
        if (pipeIdx >= 0)
        {
            targetWithAnchor = content.Substring(0, pipeIdx).Trim();
            alias = content.Substring(pipeIdx + 1).Trim();
        }

        string target = targetWithAnchor;
        string? anchor = null;

        int hashIdx = targetWithAnchor.IndexOf('#');
        if (hashIdx >= 0)
        {
            target = targetWithAnchor.Substring(0, hashIdx).Trim();
            anchor = targetWithAnchor.Substring(hashIdx + 1).Trim();
        }

        return (target, anchor, alias);
    }

    public static WikilinkResolutionResult ResolveWikilink(
        string sourceFilePath,
        string rawWikilink,
        IEnumerable<string> workspaceFiles,
        int lineNumber = 0)
    {
        var (target, anchor, alias) = ParseWikilinkContent(rawWikilink);
        return ResolveWikilinkInternal(
            sourceFilePath,
            rawWikilink ?? string.Empty,
            target,
            anchor,
            alias,
            workspaceFiles,
            lineNumber);
    }

    public static WikilinkResolutionResult ResolveWikilink(
        string sourceFilePath,
        MarkdownLinkInfo linkInfo,
        IEnumerable<string> workspaceFiles)
    {
        ArgumentNullException.ThrowIfNull(linkInfo);

        string target = linkInfo.Target;
        string? anchor = linkInfo.Anchor;
        string? alias = linkInfo.Alias;
        string rawTarget = linkInfo.RawTarget;

        if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(rawTarget))
        {
            (target, anchor, alias) = ParseWikilinkContent(rawTarget);
        }

        return ResolveWikilinkInternal(
            sourceFilePath,
            rawTarget,
            target,
            anchor,
            alias,
            workspaceFiles,
            linkInfo.LineNumber);
    }

    public static IReadOnlyList<WikilinkResolutionResult> ResolveDocumentWikilinks(
        string sourceFilePath,
        MarkdownParseResult parseResult,
        IEnumerable<string> workspaceFiles)
    {
        if (parseResult?.Links == null)
            return Array.Empty<WikilinkResolutionResult>();

        var wikilinks = parseResult.Links.Where(l => l.Type == MarkdownLinkType.Wikilink).ToList();
        var results = new List<WikilinkResolutionResult>();

        foreach (var link in wikilinks)
        {
            results.Add(ResolveWikilink(sourceFilePath, link, workspaceFiles));
        }

        return results;
    }

    public static IReadOnlyList<WikilinkDiagnostic> GetDiagnostics(IEnumerable<WikilinkResolutionResult> resolutions)
    {
        if (resolutions == null)
            return Array.Empty<WikilinkDiagnostic>();

        return resolutions
            .Where(r => r.Diagnostic != null)
            .Select(r => r.Diagnostic!)
            .ToList();
    }

    public static IReadOnlyList<BacklinkInput> ExtractBacklinkInputs(
        string sourceFilePath,
        MarkdownParseResult parseResult,
        IEnumerable<string> workspaceFiles)
    {
        var resolutions = ResolveDocumentWikilinks(sourceFilePath, parseResult, workspaceFiles);
        return ExtractBacklinkInputs(resolutions);
    }

    public static IReadOnlyList<BacklinkInput> ExtractBacklinkInputs(IEnumerable<WikilinkResolutionResult> resolutions)
    {
        if (resolutions == null)
            return Array.Empty<BacklinkInput>();

        var backlinks = new List<BacklinkInput>();
        foreach (var r in resolutions)
        {
            if (r.IsResolved && !string.IsNullOrEmpty(r.ResolvedPath))
            {
                backlinks.Add(new BacklinkInput(
                    r.SourceFilePath,
                    r.ResolvedPath,
                    r.RawTarget,
                    r.Alias,
                    r.Anchor,
                    r.LineNumber));
            }
        }
        return backlinks;
    }

    private static WikilinkResolutionResult ResolveWikilinkInternal(
        string sourceFilePath,
        string rawWikilink,
        string target,
        string? anchor,
        string? alias,
        IEnumerable<string> workspaceFiles,
        int lineNumber)
    {
        var filesList = workspaceFiles?.Where(f => !string.IsNullOrWhiteSpace(f)).ToList() ?? new List<string>();

        if (string.IsNullOrWhiteSpace(target))
        {
            var emptyDiag = new WikilinkDiagnostic(
                WikilinkDiagnosticKind.Unresolved,
                "Wikilink target is empty or invalid.",
                rawWikilink ?? string.Empty,
                alias,
                lineNumber,
                Array.Empty<string>());

            return new WikilinkResolutionResult(
                sourceFilePath,
                rawWikilink ?? string.Empty,
                target ?? string.Empty,
                anchor,
                alias,
                lineNumber,
                false,
                false,
                true,
                null,
                Array.Empty<string>(),
                emptyDiag);
        }

        List<string> candidateMatches = new List<string>();

        // Step 1: Relative exact-path resolution first if target contains path separators, extension, or relative prefix
        bool looksLikePath = target.Contains('/') || target.Contains('\\') || Path.HasExtension(target) || target.StartsWith(".");
        if (looksLikePath)
        {
            string normPath = MarkdownLinkIndexer.NormalizeRelativeLink(sourceFilePath, target);
            var pathMatch = filesList.FirstOrDefault(f => string.Equals(Path.GetFullPath(f), normPath, StringComparison.OrdinalIgnoreCase));
            if (pathMatch != null)
            {
                candidateMatches.Add(pathMatch);
            }
            else if (!Path.HasExtension(target))
            {
                string normPathWithMd = MarkdownLinkIndexer.NormalizeRelativeLink(sourceFilePath, target + ".md");
                var mdMatch = filesList.FirstOrDefault(f => string.Equals(Path.GetFullPath(f), normPathWithMd, StringComparison.OrdinalIgnoreCase));
                if (mdMatch != null)
                {
                    candidateMatches.Add(mdMatch);
                }
            }
        }

        // Step 2: Unique filename-stem fallback if path resolution found no match
        if (candidateMatches.Count == 0)
        {
            string targetStem = Path.GetFileNameWithoutExtension(target);
            var stemMatches = filesList.Where(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), targetStem, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(f), target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(f), target + ".md", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            candidateMatches.AddRange(stemMatches);
        }

        var distinctMatches = candidateMatches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (distinctMatches.Count == 1)
        {
            return new WikilinkResolutionResult(
                sourceFilePath,
                rawWikilink,
                target,
                anchor,
                alias,
                lineNumber,
                true,
                false,
                false,
                distinctMatches[0],
                distinctMatches,
                null);
        }
        else if (distinctMatches.Count > 1)
        {
            var diagnostic = new WikilinkDiagnostic(
                WikilinkDiagnosticKind.Ambiguous,
                $"Wikilink '{target}' is ambiguous. Matching candidates: {string.Join(", ", distinctMatches)}",
                rawWikilink,
                alias,
                lineNumber,
                distinctMatches);

            return new WikilinkResolutionResult(
                sourceFilePath,
                rawWikilink,
                target,
                anchor,
                alias,
                lineNumber,
                false,
                true,
                false,
                null,
                distinctMatches,
                diagnostic);
        }
        else
        {
            var diagnostic = new WikilinkDiagnostic(
                WikilinkDiagnosticKind.Unresolved,
                $"Wikilink '{target}' could not be resolved in the workspace.",
                rawWikilink,
                alias,
                lineNumber,
                Array.Empty<string>());

            return new WikilinkResolutionResult(
                sourceFilePath,
                rawWikilink,
                target,
                anchor,
                alias,
                lineNumber,
                false,
                false,
                true,
                null,
                Array.Empty<string>(),
                diagnostic);
        }
    }
}
