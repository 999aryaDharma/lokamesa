using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentIDE.Core.Docx;

namespace DocumentIDE.Infrastructure.Docx;

public class StrictMutationOptions
{
    public StringComparison Comparison { get; set; } = StringComparison.Ordinal;
    public DocxPreflightOptions? PreflightOptions { get; set; }
    public bool CreateRecoverySnapshot { get; set; } = true;
    public string? RecoveryDirectory { get; set; }
}

public class DocxMutationResult
{
    public required bool Success { get; init; }
    public required int ReplacementsApplied { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RecoverySnapshotPath { get; init; }
    public string? MutatedFilePath { get; init; }
}

public class StrictTextMutator
{
    private readonly IDocxPackagePreflight? _preflight;
    private static readonly XNamespace WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace XmlNs = "http://www.w3.org/XML/1998/namespace";

    public StrictTextMutator(IDocxPackagePreflight? preflight = null)
    {
        _preflight = preflight;
    }

    public DocxMutationResult MutateText(
        string filePath,
        string targetText,
        string replacementText,
        StrictMutationOptions? options = null)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            throw new ArgumentException($"File does not exist or invalid path: '{filePath}'", nameof(filePath));

        if (string.IsNullOrEmpty(targetText))
        {
            return new DocxMutationResult
            {
                Success = true,
                ReplacementsApplied = 0,
                MutatedFilePath = filePath
            };
        }

        options ??= new StrictMutationOptions();

        // Validate package preflight on source file
        if (_preflight != null)
        {
            var preflightResult = _preflight.ValidatePackage(filePath, options.PreflightOptions);
            if (!preflightResult.IsValid)
            {
                throw new InvalidOperationException($"Package preflight validation failed: {preflightResult.ErrorMessage} (Code: {preflightResult.ErrorCode})");
            }
        }

        string tempFilePath = Path.Combine(Path.GetTempPath(), $"docx_mutate_{Guid.NewGuid():N}.docx");
        try
        {
            File.Copy(filePath, tempFilePath, overwrite: true);

            int totalReplacements = MutatePackageText(tempFilePath, targetText, replacementText, options);

            // Validate mutated package
            if (_preflight != null)
            {
                var postValidation = _preflight.ValidatePackage(tempFilePath, options.PreflightOptions);
                if (!postValidation.IsValid)
                {
                    throw new InvalidOperationException($"Mutated package failed preflight validation: {postValidation.ErrorMessage} (Code: {postValidation.ErrorCode})");
                }
            }

            // Verify XML readable package integrity
            VerifyPackageIntegrity(tempFilePath);

            // Create recovery snapshot if requested
            string? recoveryPath = null;
            if (options.CreateRecoverySnapshot)
            {
                string recoveryDir = options.RecoveryDirectory ?? Path.GetTempPath();
                Directory.CreateDirectory(recoveryDir);
                recoveryPath = Path.Combine(recoveryDir, $"recovery_{Guid.NewGuid():N}_{Path.GetFileName(filePath)}");
                File.Copy(filePath, recoveryPath, overwrite: true);
            }

            // Atomic replace
            AtomicReplaceFile(tempFilePath, filePath);

            return new DocxMutationResult
            {
                Success = true,
                ReplacementsApplied = totalReplacements,
                RecoverySnapshotPath = recoveryPath,
                MutatedFilePath = filePath
            };
        }
        catch
        {
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
            throw;
        }
    }

    public DocxMutationResult MutateText(
        string filePath,
        IEnumerable<DocxMatchResult> matchesToReplace,
        string replacementText,
        StrictMutationOptions? options = null)
    {
        var matchArray = matchesToReplace?.ToList() ?? new List<DocxMatchResult>();
        if (!matchArray.Any())
        {
            return new DocxMutationResult
            {
                Success = true,
                ReplacementsApplied = 0,
                MutatedFilePath = filePath
            };
        }

        string targetText = matchArray[0].MatchedText;
        return MutateText(filePath, targetText, replacementText, options);
    }

    private static int MutatePackageText(
        string tempFilePath,
        string targetText,
        string replacementText,
        StrictMutationOptions options)
    {
        int totalReplacements = 0;

        using (var archive = ZipFile.Open(tempFilePath, ZipArchiveMode.Update))
        {
            var partsToMutate = IdentifyTargetParts(archive);

            var xmlSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = false,
                IgnoreWhitespace = false
            };

            foreach (var entry in partsToMutate)
            {
                XDocument xdoc;
                using (var entryStream = entry.Open())
                using (var xmlReader = XmlReader.Create(entryStream, xmlSettings))
                {
                    xdoc = XDocument.Load(xmlReader);
                }

                if (xdoc.Root == null) continue;

                int partReplacements = MutateDocumentXml(xdoc, targetText, replacementText, options);

                if (partReplacements > 0)
                {
                    totalReplacements += partReplacements;

                    string entryName = entry.FullName;
                    entry.Delete();
                    var newEntry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                    using (var newStream = newEntry.Open())
                    using (var writer = XmlWriter.Create(newStream, new XmlWriterSettings { Encoding = Encoding.UTF8, Indent = false }))
                    {
                        xdoc.Save(writer);
                    }
                }
            }
        }

        return totalReplacements;
    }

    private static List<ZipArchiveEntry> IdentifyTargetParts(ZipArchive archive)
    {
        var parts = new List<ZipArchiveEntry>();

        var docEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/'), "word/document.xml", StringComparison.OrdinalIgnoreCase));
        if (docEntry != null) parts.Add(docEntry);

        foreach (var entry in archive.Entries)
        {
            string normPath = entry.FullName.Replace('\\', '/').ToLowerInvariant();
            if ((normPath.StartsWith("word/header") || normPath.StartsWith("word/footer")) && normPath.EndsWith(".xml"))
            {
                if (!parts.Contains(entry)) parts.Add(entry);
            }
        }

        return parts;
    }

    private static int MutateDocumentXml(
        XDocument xdoc,
        string targetText,
        string replacementText,
        StrictMutationOptions options)
    {
        int count = 0;
        var paragraphs = xdoc.Descendants(WNs + "p").ToList();

        foreach (var pElem in paragraphs)
        {
            count += MutateParagraph(pElem, targetText, replacementText, options);
        }

        return count;
    }

    private class CharMapping
    {
        public char CharValue { get; init; }
        public required XElement RunElement { get; init; }
        public required XElement TextElement { get; init; }
        public required int LocalCharIndex { get; init; }
    }

    private class MatchRange
    {
        public required int StartIndex { get; init; }
        public required int Length { get; init; }
    }

    private static int MutateParagraph(
        XElement pElem,
        string targetText,
        string replacementText,
        StrictMutationOptions options)
    {
        var charMap = BuildCharMapping(pElem);
        if (charMap.Count == 0) return 0;

        string logicalText = new string(charMap.Select(c => c.CharValue).ToArray());

        var matches = new List<MatchRange>();
        int searchPos = 0;
        while (searchPos < logicalText.Length)
        {
            int foundIdx = logicalText.IndexOf(targetText, searchPos, options.Comparison);
            if (foundIdx < 0) break;

            matches.Add(new MatchRange { StartIndex = foundIdx, Length = targetText.Length });
            searchPos = foundIdx + Math.Max(1, targetText.Length);
        }

        if (matches.Count == 0) return 0;

        // Apply replacements right-to-left (descending order) to preserve indices of earlier matches
        foreach (var match in matches.OrderByDescending(m => m.StartIndex))
        {
            ApplySingleMatchReplacement(pElem, charMap, match.StartIndex, match.Length, replacementText);
        }

        // Clean up safely redundant empty run structures in paragraph
        CleanRedundantEmptiedRuns(pElem);

        return matches.Count;
    }

    private static List<CharMapping> BuildCharMapping(XElement pElem)
    {
        var list = new List<CharMapping>();
        int fieldDepth = 0;

        foreach (var child in pElem.Elements())
        {
            ProcessElementForCharMap(child, list, ref fieldDepth);
        }

        return list;
    }

    private static void ProcessElementForCharMap(
        XElement element,
        List<CharMapping> list,
        ref int fieldDepth)
    {
        string localName = element.Name.LocalName;

        if (localName == "fldChar")
        {
            string? fldCharType = element.Attribute(WNs + "fldCharType")?.Value
                ?? element.Attribute(XName.Get("fldCharType"))?.Value;

            if (string.Equals(fldCharType, "begin", StringComparison.OrdinalIgnoreCase))
            {
                fieldDepth++;
            }
            else if (string.Equals(fldCharType, "separate", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(fldCharType, "end", StringComparison.OrdinalIgnoreCase))
            {
                if (fieldDepth > 0) fieldDepth--;
            }
            return;
        }

        if (localName == "instrText") return;

        if (fieldDepth > 0 && (localName == "instrText" || localName == "t")) return;

        if (localName == "r")
        {
            foreach (var rChild in element.Elements())
            {
                string rChildName = rChild.Name.LocalName;
                if (rChildName == "fldChar")
                {
                    string? fldCharType = rChild.Attribute(WNs + "fldCharType")?.Value
                        ?? rChild.Attribute(XName.Get("fldCharType"))?.Value;

                    if (string.Equals(fldCharType, "begin", StringComparison.OrdinalIgnoreCase)) fieldDepth++;
                    else if (string.Equals(fldCharType, "separate", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(fldCharType, "end", StringComparison.OrdinalIgnoreCase))
                    {
                        if (fieldDepth > 0) fieldDepth--;
                    }
                }
                else if (rChildName == "instrText")
                {
                    continue;
                }
                else if (fieldDepth == 0 && rChildName == "t")
                {
                    string val = rChild.Value;
                    for (int i = 0; i < val.Length; i++)
                    {
                        list.Add(new CharMapping
                        {
                            CharValue = val[i],
                            RunElement = element,
                            TextElement = rChild,
                            LocalCharIndex = i
                        });
                    }
                }
                else if (fieldDepth == 0 && rChildName == "tab")
                {
                    list.Add(new CharMapping
                    {
                        CharValue = '\t',
                        RunElement = element,
                        TextElement = rChild,
                        LocalCharIndex = 0
                    });
                }
                else if (fieldDepth == 0 && (rChildName == "br" || rChildName == "cr"))
                {
                    list.Add(new CharMapping
                    {
                        CharValue = '\n',
                        RunElement = element,
                        TextElement = rChild,
                        LocalCharIndex = 0
                    });
                }
                else if (fieldDepth == 0 && rChildName == "noBreakHyphen")
                {
                    list.Add(new CharMapping
                    {
                        CharValue = '\u2011',
                        RunElement = element,
                        TextElement = rChild,
                        LocalCharIndex = 0
                    });
                }
            }
            return;
        }

        foreach (var child in element.Elements())
        {
            ProcessElementForCharMap(child, list, ref fieldDepth);
        }
    }

    private static void ApplySingleMatchReplacement(
        XElement pElem,
        List<CharMapping> charMap,
        int matchStart,
        int matchLen,
        string replacementText)
    {
        int matchEnd = matchStart + matchLen;
        var matchedChars = charMap.Skip(matchStart).Take(matchLen).ToList();
        if (matchedChars.Count == 0) return;

        var anchorChar = matchedChars[0];
        var anchorTextElem = anchorChar.TextElement;

        var lastChar = matchedChars[matchedChars.Count - 1];
        var lastTextElem = lastChar.TextElement;

        var textElemGroups = matchedChars
            .GroupBy(c => c.TextElement)
            .ToList();

        foreach (var group in textElemGroups)
        {
            var tElem = group.Key;
            if (tElem.Name.LocalName != "t") continue;

            string origVal = tElem.Value;

            if (tElem == anchorTextElem)
            {
                int prefixLen = anchorChar.LocalCharIndex;
                string prefix = origVal.Substring(0, prefixLen);

                string suffix = "";
                if (tElem == lastTextElem)
                {
                    int lastLocalEnd = lastChar.LocalCharIndex + 1;
                    if (lastLocalEnd < origVal.Length)
                    {
                        suffix = origVal.Substring(lastLocalEnd);
                    }
                }

                string newVal = prefix + replacementText + suffix;
                SetTextElementValue(tElem, newVal);
            }
            else if (tElem == lastTextElem)
            {
                int lastLocalEnd = lastChar.LocalCharIndex + 1;
                string suffix = (lastLocalEnd < origVal.Length) ? origVal.Substring(lastLocalEnd) : "";
                SetTextElementValue(tElem, suffix);
            }
            else
            {
                SetTextElementValue(tElem, "");
            }
        }
    }

    private static void SetTextElementValue(XElement tElem, string text)
    {
        tElem.Value = text;
        if (text.StartsWith(' ') || text.EndsWith(' '))
        {
            tElem.SetAttributeValue(XmlNs + "space", "preserve");
        }
    }

    private static void CleanRedundantEmptiedRuns(XElement pElem)
    {
        var runs = pElem.Descendants(WNs + "r").ToList();
        foreach (var r in runs)
        {
            var tNodes = r.Elements(WNs + "t").ToList();
            foreach (var t in tNodes)
            {
                if (string.IsNullOrEmpty(t.Value))
                {
                    t.Remove();
                }
            }

            if (IsSafelyRedundantRun(r))
            {
                r.Remove();
            }
        }
    }

    private static bool IsSafelyRedundantRun(XElement rElem)
    {
        foreach (var child in rElem.Elements())
        {
            string localName = child.Name.LocalName;
            if (localName == "rPr") continue;
            if (localName == "t" && string.IsNullOrEmpty(child.Value)) continue;

            return false;
        }

        return true;
    }

    private static void VerifyPackageIntegrity(string docxPath)
    {
        using var archive = ZipFile.OpenRead(docxPath);
        var entries = archive.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();

        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        foreach (var entry in entries)
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, xmlSettings);
            while (reader.Read()) { }
        }
    }

    private static void AtomicReplaceFile(string tempFilePath, string targetFilePath)
    {
        try
        {
            File.Replace(tempFilePath, targetFilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch
        {
            File.Copy(tempFilePath, targetFilePath, overwrite: true);
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
        }
    }
}
