using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using DocumentIDE.Core.Docx;

namespace DocumentIDE.Infrastructure.Docx;

public class DocxPackagePreflight : IDocxPackagePreflight
{
    private const string OfficeDocRelTransitional = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string OfficeDocRelStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument";

    public DocxPreflightResult ValidatePackage(string filePath, DocxPreflightOptions? options = null)
    {
        options ??= new DocxPreflightOptions();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return DocxPreflightResult.Reject(
                DocxPreflightErrorCode.FileNotFound,
                $"File not found at path: '{filePath}'."
            );
        }

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > options.MaxCompressedSizeBytes)
        {
            return DocxPreflightResult.Reject(
                DocxPreflightErrorCode.FileTooLarge,
                $"Compressed package size ({fileInfo.Length} bytes) exceeds maximum limit of {options.MaxCompressedSizeBytes} bytes.",
                new DocxPackageMetrics(CompressedSizeBytes: fileInfo.Length)
            );
        }

        try
        {
            using var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ValidateStreamCore(fileStream, options, fileInfo.Length);
        }
        catch (IOException ex)
        {
            return DocxPreflightResult.Corrupted(
                DocxPreflightErrorCode.InvalidZipArchive,
                $"Unable to access file stream: {ex.Message}"
            );
        }
    }

    public DocxPreflightResult ValidatePackage(Stream stream, DocxPreflightOptions? options = null)
    {
        options ??= new DocxPreflightOptions();

        if (stream == null || !stream.CanRead || !stream.CanSeek)
        {
            return DocxPreflightResult.Corrupted(
                DocxPreflightErrorCode.InvalidZipArchive,
                "Stream is null or unreadable or non-seekable."
            );
        }

        long compressedSizeBytes = 0;
        long initialPosition = 0;
        bool canSeek = stream.CanSeek;

        try
        {
            if (canSeek)
            {
                initialPosition = stream.Position;
                compressedSizeBytes = stream.Length - initialPosition;
            }

            if (compressedSizeBytes > options.MaxCompressedSizeBytes)
            {
                return DocxPreflightResult.Reject(
                    DocxPreflightErrorCode.FileTooLarge,
                    $"Compressed package stream size ({compressedSizeBytes} bytes) exceeds maximum limit of {options.MaxCompressedSizeBytes} bytes.",
                    new DocxPackageMetrics(CompressedSizeBytes: compressedSizeBytes)
                );
            }

            return ValidateStreamCore(stream, options, compressedSizeBytes);
        }
        finally
        {
            if (canSeek)
            {
                try { stream.Position = initialPosition; } catch { }
            }
        }
    }

    private static DocxPreflightResult ValidateStreamCore(
        Stream stream,
        DocxPreflightOptions options,
        long compressedSizeBytes)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            _ = archive.Entries;
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is NotSupportedException || ex is ArgumentException)
        {
            return DocxPreflightResult.Corrupted(
                DocxPreflightErrorCode.InvalidZipArchive,
                $"Invalid or corrupted ZIP archive header: {ex.Message}",
                new DocxPackageMetrics(CompressedSizeBytes: compressedSizeBytes)
            );
        }

        using (archive)
        {
            // Step 1: Entry count limit check
            if (archive.Entries.Count > options.MaxEntryCount)
            {
                return DocxPreflightResult.Reject(
                    DocxPreflightErrorCode.TooManyEntries,
                    $"ZIP entry count ({archive.Entries.Count}) exceeds maximum permitted entries limit of {options.MaxEntryCount}.",
                    new DocxPackageMetrics(CompressedSizeBytes: compressedSizeBytes, EntryCount: archive.Entries.Count)
                );
            }

            long totalUncompressedSizeBytes = 0;
            int mediaCount = 0;

            // Step 2: Zip Slip and part size bounds check
            foreach (var entry in archive.Entries)
            {
                if (IsZipSlipAttempt(entry.FullName))
                {
                    return DocxPreflightResult.Reject(
                        DocxPreflightErrorCode.ZipSlipAttempt,
                        $"Zip Slip path traversal attempt detected in entry path: '{entry.FullName}'.",
                        new DocxPackageMetrics(CompressedSizeBytes: compressedSizeBytes, EntryCount: archive.Entries.Count)
                    );
                }

                long entryUncompressedSize = entry.Length;
                if (entryUncompressedSize > options.MaxPartUncompressedSizeBytes)
                {
                    return DocxPreflightResult.Reject(
                        DocxPreflightErrorCode.PartSizeExceeded,
                        $"Uncompressed size of package part '{entry.FullName}' ({entryUncompressedSize} bytes) exceeds single part limit of {options.MaxPartUncompressedSizeBytes} bytes.",
                        new DocxPackageMetrics(CompressedSizeBytes: compressedSizeBytes, EntryCount: archive.Entries.Count)
                    );
                }

                totalUncompressedSizeBytes += entryUncompressedSize;
                if (totalUncompressedSizeBytes > options.MaxTotalUncompressedSizeBytes)
                {
                    return DocxPreflightResult.Reject(
                        DocxPreflightErrorCode.TotalUncompressedSizeExceeded,
                        $"Total uncompressed package size ({totalUncompressedSizeBytes} bytes) exceeds maximum limit of {options.MaxTotalUncompressedSizeBytes} bytes.",
                        new DocxPackageMetrics(
                            CompressedSizeBytes: compressedSizeBytes,
                            EntryCount: archive.Entries.Count,
                            TotalUncompressedSizeBytes: totalUncompressedSizeBytes
                        )
                    );
                }

                string entryNameLower = entry.FullName.Replace('\\', '/').ToLowerInvariant();
                if (entryNameLower.StartsWith("word/media/") || entryNameLower.EndsWith(".png") || entryNameLower.EndsWith(".jpeg") || entryNameLower.EndsWith(".jpg"))
                {
                    mediaCount++;
                }
            }

            // Step 3: Mandatory OpenXML package entry verification
            var contentTypesEntry = FindEntry(archive, "[Content_Types].xml");
            if (contentTypesEntry == null)
            {
                return DocxPreflightResult.Reject(
                    DocxPreflightErrorCode.MissingContentTypes,
                    "Mandatory OpenXML package manifest entry '[Content_Types].xml' is missing.",
                    new DocxPackageMetrics(CompressedSizeBytes: compressedSizeBytes, EntryCount: archive.Entries.Count, TotalUncompressedSizeBytes: totalUncompressedSizeBytes)
                );
            }

            var rootRelsEntry = FindEntry(archive, "_rels/.rels");
            if (rootRelsEntry == null)
            {
                return DocxPreflightResult.Reject(
                    DocxPreflightErrorCode.MissingRootRels,
                    "Mandatory OpenXML package relationships entry '_rels/.rels' is missing.",
                    new DocxPackageMetrics(CompressedSizeBytes: compressedSizeBytes, EntryCount: archive.Entries.Count, TotalUncompressedSizeBytes: totalUncompressedSizeBytes)
                );
            }

            // Step 4: Secure XML Reader Configuration (DTD Prohibited, XmlResolver = null)
            var xmlSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };

            // Inspect [Content_Types].xml
            var (ctSuccess, ctErrorResult) = InspectXmlEntry(contentTypesEntry, xmlSettings, compressedSizeBytes, archive.Entries.Count, totalUncompressedSizeBytes);
            if (!ctSuccess)
            {
                return ctErrorResult!;
            }

            // Inspect _rels/.rels and extract root relationships
            var relationships = new List<DocxRelationshipInfo>();
            string? mainDocumentTarget = null;

            try
            {
                using var relsStream = rootRelsEntry.Open();
                using var reader = XmlReader.Create(relsStream, xmlSettings);
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && string.Equals(reader.LocalName, "Relationship", StringComparison.OrdinalIgnoreCase))
                    {
                        string id = reader.GetAttribute("Id") ?? string.Empty;
                        string type = reader.GetAttribute("Type") ?? string.Empty;
                        string target = reader.GetAttribute("Target") ?? string.Empty;
                        string targetMode = reader.GetAttribute("TargetMode") ?? "Internal";

                        relationships.Add(new DocxRelationshipInfo(id, type, target, targetMode));

                        if (type.Equals(OfficeDocRelTransitional, StringComparison.OrdinalIgnoreCase) ||
                            type.Equals(OfficeDocRelStrict, StringComparison.OrdinalIgnoreCase))
                        {
                            mainDocumentTarget = target;
                        }
                    }
                }
            }
            catch (XmlException ex)
            {
                if (IsDtdError(ex))
                {
                    return DocxPreflightResult.Reject(
                        DocxPreflightErrorCode.DtdProhibited,
                        $"DTD or external entity declaration prohibited in _rels/.rels: {ex.Message}"
                    );
                }
                return DocxPreflightResult.Corrupted(
                    DocxPreflightErrorCode.XmlMalformed,
                    $"Malformed XML structure in _rels/.rels: {ex.Message}"
                );
            }

            // Default main document path fallback
            mainDocumentTarget ??= "word/document.xml";
            string normalizedMainTarget = NormalizeZipPath(mainDocumentTarget);

            var mainDocEntry = FindEntry(archive, normalizedMainTarget);
            if (mainDocEntry == null)
            {
                return DocxPreflightResult.Reject(
                    DocxPreflightErrorCode.MissingMainDocument,
                    $"Main document part '{normalizedMainTarget}' was not found in package.",
                    new DocxPackageMetrics(CompressedSizeBytes: compressedSizeBytes, EntryCount: archive.Entries.Count, TotalUncompressedSizeBytes: totalUncompressedSizeBytes)
                );
            }

            // Inspect main document XML
            var (docSuccess, docErrorResult) = InspectXmlEntry(mainDocEntry, xmlSettings, compressedSizeBytes, archive.Entries.Count, totalUncompressedSizeBytes);
            if (!docSuccess)
            {
                return docErrorResult!;
            }

            // Step 5: Relationship Target Validation (Check for missing internal targets / media)
            var warnings = new List<string>();

            // Check main document relationships if present (e.g. word/_rels/document.xml.rels)
            string mainDocRelsPath = GetRelsPathForPart(normalizedMainTarget);
            var mainDocRelsEntry = FindEntry(archive, mainDocRelsPath);
            if (mainDocRelsEntry != null)
            {
                try
                {
                    using var streamRels = mainDocRelsEntry.Open();
                    using var readerRels = XmlReader.Create(streamRels, xmlSettings);
                    while (readerRels.Read())
                    {
                        if (readerRels.NodeType == XmlNodeType.Element && string.Equals(readerRels.LocalName, "Relationship", StringComparison.OrdinalIgnoreCase))
                        {
                            string target = readerRels.GetAttribute("Target") ?? string.Empty;
                            string targetMode = readerRels.GetAttribute("TargetMode") ?? "Internal";

                            if (!string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(target))
                            {
                                string targetPartPath = ResolveRelativePath(normalizedMainTarget, target);
                                if (FindEntry(archive, targetPartPath) == null)
                                {
                                    warnings.Add($"Referenced target part '{targetPartPath}' is missing from package.");
                                }
                            }
                        }
                    }
                }
                catch (XmlException ex)
                {
                    if (IsDtdError(ex))
                    {
                        return DocxPreflightResult.Reject(
                            DocxPreflightErrorCode.DtdProhibited,
                            $"DTD prohibited in relationship file {mainDocRelsPath}: {ex.Message}"
                        );
                    }
                    warnings.Add($"Warning: Relationship file '{mainDocRelsPath}' is malformed: {ex.Message}");
                }
            }

            var metrics = new DocxPackageMetrics(
                CompressedSizeBytes: compressedSizeBytes,
                EntryCount: archive.Entries.Count,
                TotalUncompressedSizeBytes: totalUncompressedSizeBytes,
                PartCount: archive.Entries.Count,
                MediaCount: mediaCount
            );

            return DocxPreflightResult.Success(metrics, relationships, warnings);
        }
    }

    private static (bool Success, DocxPreflightResult? ErrorResult) InspectXmlEntry(
        ZipArchiveEntry entry,
        XmlReaderSettings xmlSettings,
        long compressedSize,
        int entryCount,
        long totalUncompressedSize)
    {
        try
        {
            using var entryStream = entry.Open();
            using var reader = XmlReader.Create(entryStream, xmlSettings);
            while (reader.Read())
            {
                // Traverse stream to validate XML syntax and DTD prohibition
            }
            return (true, null);
        }
        catch (XmlException ex)
        {
            var metrics = new DocxPackageMetrics(CompressedSizeBytes: compressedSize, EntryCount: entryCount, TotalUncompressedSizeBytes: totalUncompressedSize);
            if (IsDtdError(ex))
            {
                return (false, DocxPreflightResult.Reject(
                    DocxPreflightErrorCode.DtdProhibited,
                    $"DTD or external entity declaration prohibited in '{entry.FullName}': {ex.Message}",
                    metrics
                ));
            }

            return (false, DocxPreflightResult.Corrupted(
                DocxPreflightErrorCode.XmlMalformed,
                $"Malformed XML structure in '{entry.FullName}': {ex.Message}",
                metrics
            ));
        }
    }

    private static bool IsDtdError(XmlException ex)
    {
        string msg = ex.Message.ToLowerInvariant();
        return msg.Contains("dtd") || msg.Contains("doctype") || msg.Contains("entity");
    }

    private static bool IsZipSlipAttempt(string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath)) return false;

        string normalized = entryPath.Replace('\\', '/');

        if (normalized.StartsWith('/') || normalized.StartsWith('\\') || (normalized.Length > 1 && normalized[1] == ':'))
        {
            return true;
        }

        string[] segments = normalized.Split('/');
        foreach (var seg in segments)
        {
            if (seg == "..") return true;
        }

        return false;
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string entryPath)
    {
        string targetNormalized = NormalizeZipPath(entryPath);
        return archive.Entries.FirstOrDefault(e =>
            string.Equals(NormalizeZipPath(e.FullName), targetNormalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeZipPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        string normalized = path.Replace('\\', '/').TrimStart('/');
        return normalized;
    }

    private static string GetRelsPathForPart(string partPath)
    {
        string normalized = NormalizeZipPath(partPath);
        int lastSlash = normalized.LastIndexOf('/');
        if (lastSlash < 0)
        {
            return $"_rels/{normalized}.rels";
        }
        string dir = normalized.Substring(0, lastSlash);
        string filename = normalized.Substring(lastSlash + 1);
        return $"{dir}/_rels/{filename}.rels";
    }

    private static string ResolveRelativePath(string basePartPath, string relativePath)
    {
        string normRelative = relativePath.Replace('\\', '/');
        if (normRelative.StartsWith('/'))
        {
            return normRelative.TrimStart('/');
        }

        int lastSlash = basePartPath.LastIndexOf('/');
        string baseDir = lastSlash >= 0 ? basePartPath.Substring(0, lastSlash) : string.Empty;

        string combined = string.IsNullOrEmpty(baseDir) ? normRelative : $"{baseDir}/{normRelative}";
        string[] parts = combined.Split('/');
        var stack = new List<string>();

        foreach (var part in parts)
        {
            if (part == "." || string.IsNullOrEmpty(part)) continue;
            if (part == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }
            else
            {
                stack.Add(part);
            }
        }

        return string.Join('/', stack);
    }
}
