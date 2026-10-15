using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Pdf;

/// <summary>
/// PDF-03 — streaming ordered PDF merge orchestrator.
/// <para>
/// Flow: explicit user order → (DOCX variant only) render each DOCX to a
/// temporary PDF via PDF-01's <see cref="PdfExportService"/> on the WORD-03
/// queue → append inputs sequentially in order → single merged PDF.
/// </para>
/// <para>
/// Merge library decision (spec 09 §11, license compatibility): BCL-only
/// hand-rolled sequential object-graph append — no third-party PDF package.
/// Rejected: iText (AGPL, copyleft incompatible with this product) and
/// adding any new external merge dependency (new license surface + supply
/// chain for a P1 that a bounded streaming appender satisfies). Object
/// bodies are carried over verbatim (vector content preserved, never
/// rasterized); cross-file references are renumbered; a fresh page tree,
/// catalog, and xref table are rebuilt.
/// </para>
/// <para>
/// Memory bounds (spec 09 §18): inputs are processed strictly one file at
/// a time with an 80 KB output buffer. Only the current input's bytes plus
/// small per-object dictionaries are ever resident; page dictionaries (also
/// small — content streams are separate objects) are the only cross-file
/// retention, and the xref offsets are 8 bytes per object. Peak managed
/// memory scales with the largest single input, never the total corpus.
/// </para>
/// <para>
/// Limitations (typed failures, never silent fallback):
/// object/compressed-reference streams (<c>/ObjStm</c>, cross-reference
/// streams) and encrypted PDFs are rejected with
/// <see cref="PdfExportStatus.ExportFailed"/>; only plain cross-reference
/// table PDFs (what Word writes) are merged.
/// </para>
/// </summary>
public sealed class PdfMergeService
{
    /// <summary>HRESULT for Win32 ERROR_DISK_FULL (0x70).</summary>
    public const int DiskFullHResult = unchecked((int)0x80070070);

    /// <summary>Bounded sequential copy buffer (bytes).</summary>
    public const int CopyBufferSize = 81920;

    private static readonly Regex RefRegex =
        new(@"\b(\d+)\s+(\d+)\s+R\b", RegexOptions.Compiled);
    private static readonly Regex CatalogTypeRegex =
        new(@"/Type\s*/Catalog\b", RegexOptions.Compiled);
    private static readonly Regex PagesTypeRegex =
        new(@"/Type\s*/Pages\b", RegexOptions.Compiled);
    private static readonly Regex PageTypeRegex =
        new(@"/Type\s*/Page\b", RegexOptions.Compiled);
    private static readonly Regex NodePagesRefRegex =
        new(@"/Pages\s+(\d+)\s+(\d+)\s+R", RegexOptions.Compiled);
    private static readonly Regex KidsRegex =
        new(@"/Kids\s*\[(.*?)\]", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ParentRegex =
        new(@"/Parent\s+\d+\s+\d+\s+R", RegexOptions.Compiled);
    private static readonly Regex LengthRegex =
        new(@"/Length\s+(\d+)(\s+\d+\s+R)?", RegexOptions.Compiled);
    private static readonly Regex ObjStmRegex =
        new(@"/Type\s*/(ObjStm|XRef)\b", RegexOptions.Compiled);
    private static readonly Regex EncryptRegex =
        new(@"/Encrypt\b", RegexOptions.Compiled);

    private static readonly Encoding Latin1 = Encoding.Latin1;
    private static readonly byte[] PdfHeaderMarker = Encoding.ASCII.GetBytes("%PDF-");

    private readonly PdfExportService? _single;

    /// <summary>Creates a PDF-only merger (DOCX variant unavailable).</summary>
    public PdfMergeService()
    {
    }

    /// <summary>Creates a merger that can also render DOCX via PDF-01.</summary>
    public PdfMergeService(PdfExportService single)
    {
        _single = single ?? throw new ArgumentNullException(nameof(single));
    }

    /// <summary>
    /// Merges existing PDFs in exactly the requested order.
    /// The order is preserved verbatim; it is never sorted or inferred.
    /// </summary>
    public async Task<OrderedMergeResult> MergeOrderedAsync(
        OrderedMergeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        string targetPath;
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            targetPath = Path.GetFullPath(request.OutputPath);
        }
        catch (Exception ex)
        {
            return Fail(request, PdfExportStatus.ExportFailed, null,
                $"Could not resolve merge output path: {ex.Message}");
        }

        bool existedBefore = File.Exists(targetPath);
        if (existedBefore)
        {
            switch (request.CollisionBehavior)
            {
                case PdfCollisionBehavior.Fail:
                    // Never silently overwrite (spec 09 §8).
                    return Fail(request, PdfExportStatus.OutputCollision, targetPath,
                        $"Output PDF already exists: '{targetPath}'. Choose Overwrite or Rename explicitly.");
                case PdfCollisionBehavior.Overwrite:
                    break;
                case PdfCollisionBehavior.Rename:
                    try
                    {
                        targetPath = FindFreeSiblingPath(
                            Path.GetDirectoryName(targetPath)!,
                            Path.GetFileName(targetPath));
                    }
                    catch (Exception ex)
                    {
                        return Fail(request, PdfExportStatus.ExportFailed, targetPath,
                            $"Could not find a free merge output name: {ex.Message}");
                    }

                    existedBefore = false;
                    break;
                default:
                    return Fail(request, PdfExportStatus.ExportFailed, targetPath,
                        $"Unknown collision behavior: '{request.CollisionBehavior}'.");
            }
        }

        var pageDictsInOrder = new List<string>();
        var offsets = new List<(int Number, long Offset)>();
        int nextNumber = 1;

        try
        {
            using var output = new FileStream(
                targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
                CopyBufferSize, FileOptions.SequentialScan);
            WriteAscii(output, "%PDF-1.7\n");

            foreach (string sourcePath in request.OrderedSourcePaths)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    output.Dispose();
                    DeletePartialOutput(targetPath, existedBefore);
                    return Fail(request, PdfExportStatus.Cancelled, targetPath,
                        "PDF merge was cancelled before completion.");
                }

                if (!File.Exists(sourcePath))
                {
                    output.Dispose();
                    DeletePartialOutput(targetPath, existedBefore);
                    return Fail(request, PdfExportStatus.SourceNotFound, targetPath,
                        $"Merge input PDF not found: '{sourcePath}'.");
                }

                // Bounded residence: exactly one input file is read and parsed
                // at a time; its bytes are released before the next file starts.
                byte[] inputBytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken)
                    .ConfigureAwait(false);
                ParsedPdf parsed;
                try
                {
                    parsed = ParsedPdf.Parse(inputBytes, sourcePath);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    output.Dispose();
                    DeletePartialOutput(targetPath, existedBefore);
                    return Fail(request, PdfExportStatus.ExportFailed, targetPath,
                        $"Cannot merge '{sourcePath}': {ex.Message}");
                }

                var renumber = new Dictionary<int, int>(parsed.Objects.Count);
                foreach (int oldNumber in parsed.Objects.Keys.OrderBy(n => n))
                {
                    renumber[oldNumber] = nextNumber++;
                }

                foreach (int oldNumber in parsed.Objects.Keys.OrderBy(n => n))
                {
                    ParsedObject parsedObject = parsed.Objects[oldNumber];
                    string rewritten = RefRegex.Replace(parsedObject.Dictionary, match =>
                        renumber.TryGetValue(int.Parse(match.Groups[1].Value), out int mapped)
                            ? $"{mapped} 0 R"
                            : match.Value);

                    if (parsed.LeafPages.Contains(oldNumber))
                    {
                        // Page dictionaries are small (content streams are
                        // separate objects already written below); deferring
                        // them lets every other object stream straight to disk.
                        pageDictsInOrder.Add(rewritten);
                    }
                    else
                    {
                        offsets.Add((renumber[oldNumber], output.Position));
                        WriteObject(output, renumber[oldNumber], rewritten,
                            parsedObject.StreamData);
                    }
                }

                // Release the current input before the next file starts so
                // peak memory tracks one input, not the corpus (parsed
                // objects are small dictionaries; stream bytes of prior
                // files are already on disk in the output).
            }

            int pagesRootNumber = nextNumber++;
            int catalogNumber = nextNumber++;
            int firstPageNumber = nextNumber;
            for (int pageIndex = 0; pageIndex < pageDictsInOrder.Count; pageIndex++)
            {
                int pageNumber = firstPageNumber + pageIndex;
                string withParent = FixParent(pageDictsInOrder[pageIndex], pagesRootNumber);
                offsets.Add((pageNumber, output.Position));
                WriteObject(output, pageNumber, withParent, null);
            }

            nextNumber = firstPageNumber + pageDictsInOrder.Count;
            string kids = string.Join(" ", Enumerable.Range(
                firstPageNumber, pageDictsInOrder.Count)
                .Select(n => $"{n} 0 R"));
            offsets.Add((pagesRootNumber, output.Position));
            WriteAscii(output,
                $"{pagesRootNumber} 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pageDictsInOrder.Count} >>\nendobj\n");
            offsets.Add((catalogNumber, output.Position));
            WriteAscii(output,
                $"{catalogNumber} 0 obj\n<< /Type /Catalog /Pages {pagesRootNumber} 0 R >>\nendobj\n");

            long xrefAt = output.Position;
            WriteAscii(output, $"xref\n0 {nextNumber}\n0000000000 65535 f \n");
            foreach ((int number, long offset) in offsets.OrderBy(o => o.Number))
            {
                WriteAscii(output, $"{offset:0000000000} 00000 n \n");
            }

            WriteAscii(output,
                $"trailer\n<< /Size {nextNumber} /Root {catalogNumber} 0 R >>\nstartxref\n{xrefAt}\n%%EOF");
        }
        catch (OperationCanceledException)
        {
            DeletePartialOutput(targetPath, existedBefore);
            return Fail(request, PdfExportStatus.Cancelled, targetPath,
                "PDF merge was cancelled before completion.");
        }
        catch (IOException ex) when (ex.HResult == DiskFullHResult)
        {
            DeletePartialOutput(targetPath, existedBefore);
            return Fail(request, PdfExportStatus.DiskFull, targetPath,
                $"Disk full while writing merged PDF: {ex.Message}");
        }
        catch (Exception ex)
        {
            DeletePartialOutput(targetPath, existedBefore);
            return Fail(request, PdfExportStatus.ExportFailed, targetPath,
                $"PDF merge failed: {ex.Message}");
        }

        if (!File.Exists(targetPath))
        {
            return Fail(request, PdfExportStatus.ExportFailed, targetPath,
                "Merge completed but no PDF was written.");
        }

        return new OrderedMergeResult(
            PdfExportStatus.Succeeded, targetPath, pageDictsInOrder.Count,
            request.OrderedSourcePaths);
    }

    /// <summary>
    /// Renders each DOCX to a temporary PDF via PDF-01/PDF-02 services
    /// (WORD-03 queue, concurrency 1), merges the intermediates in the
    /// explicit request order, and always cleans the per-job temp directory
    /// (spec 09 §10) on success and failure.
    /// </summary>
    public async Task<OrderedMergeResult> MergeDocumentsOrderedAsync(
        OrderedMergeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        if (_single is null)
        {
            throw new InvalidOperationException(
                "Merging DOCX documents requires a PdfExportService-backed PdfMergeService.");
        }

        string jobDir = CreateJobDirectory();
        try
        {
            var intermediates = new List<string>(request.OrderedSourcePaths.Count);
            for (int index = 0; index < request.OrderedSourcePaths.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Fail(request, PdfExportStatus.Cancelled, null,
                        "PDF merge was cancelled before completion.", jobDir);
                }

                string source = request.OrderedSourcePaths[index];
                PdfExportResult rendered = await _single.ExportSingleAsync(
                    new PdfExportRequest(
                        source,
                        jobDir,
                        $"merge-part-{index:00000}.pdf",
                        PdfCollisionBehavior.Overwrite),
                    cancellationToken).ConfigureAwait(false);

                if (!rendered.IsSuccess)
                {
                    return Fail(request, rendered.Status, null,
                        $"Rendering '{source}' to PDF failed: {rendered.ErrorMessage ?? rendered.Status.ToString()}.",
                        jobDir);
                }

                intermediates.Add(rendered.OutputPath!);
            }

            OrderedMergeResult merged = await MergeOrderedAsync(
                new OrderedMergeRequest(intermediates, request.OutputPath, request.CollisionBehavior),
                cancellationToken).ConfigureAwait(false);

            // Report the user-facing order, not the temp intermediate names.
            return merged with
            {
                OrderedSources = request.OrderedSourcePaths,
                TempJobDirectory = jobDir,
            };
        }
        finally
        {
            DeleteDirectoryBestEffort(jobDir);
        }
    }

    /// <summary>
    /// Creates a fresh per-job temp directory (spec 09 §10):
    /// <c>%LOCALAPPDATA%\DocumentIDE\temp\pdf-jobs\&lt;job-id&gt;\</c>.
    /// </summary>
    public static string CreateJobDirectory()
    {
        string jobDir = Path.Combine(GetPdfJobsRoot(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobDir);
        return jobDir;
    }

    /// <summary>Root for PDF merge job directories (spec 09 §10).</summary>
    public static string GetPdfJobsRoot()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string root = string.IsNullOrWhiteSpace(localAppData)
            ? Path.Combine(Path.GetTempPath(), "DocumentIDE", "pdf-jobs")
            : Path.Combine(localAppData, "DocumentIDE", "temp", "pdf-jobs");
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// Runs <paramref name="operation"/> while sampling live managed memory,
    /// returning peak additional bytes above the pre-run baseline. Used by the
    /// §18 peak-memory regression test.
    /// </summary>
    public async Task<long> MeasurePeakAdditionalBytesAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        long baseline = GC.GetTotalMemory(forceFullCollection: true);
        long peak = baseline;
        using var samplerControl = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!samplerControl.Token.IsCancellationRequested)
            {
                long current = GC.GetTotalMemory(forceFullCollection: true);
                if (current > peak)
                {
                    peak = current;
                }

                try
                {
                    await Task.Delay(5, samplerControl.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, CancellationToken.None);

        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            samplerControl.Cancel();
            await sampler.ConfigureAwait(false);
        }

        return Math.Max(0, peak - baseline);
    }

    private static void ValidateRequest(OrderedMergeRequest request)
    {
        if (request.OrderedSourcePaths is null || request.OrderedSourcePaths.Count == 0)
        {
            throw new ArgumentException(
                "PDF merge requires an explicit non-empty ordered source list; order is never inferred.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new ArgumentException("PDF merge requires an explicit output path.", nameof(request));
        }
    }

    private static OrderedMergeResult Fail(
        OrderedMergeRequest request,
        PdfExportStatus status,
        string? outputPath,
        string message,
        string? jobDir = null)
    {
        return new OrderedMergeResult(
            status, outputPath, 0, request.OrderedSourcePaths, message, jobDir);
    }

    private static string FixParent(string pageDictionary, int pagesRootNumber)
    {
        string replacement = $"/Parent {pagesRootNumber} 0 R";
        if (ParentRegex.IsMatch(pageDictionary))
        {
            return ParentRegex.Replace(pageDictionary, replacement);
        }

        int close = pageDictionary.LastIndexOf(">>", StringComparison.Ordinal);
        return close < 0
            ? pageDictionary + " " + replacement
            : pageDictionary[..close] + " " + replacement + " " + pageDictionary[close..];
    }

    private static void WriteObject(FileStream output, int number, string dictionary, byte[]? streamData)
    {
        WriteAscii(output, $"{number} 0 obj\n{dictionary}\n");
        if (streamData is not null)
        {
            WriteAscii(output, "stream\r\n");
            output.Write(streamData, 0, streamData.Length);
            WriteAscii(output, "\r\nendstream\n");
        }

        WriteAscii(output, "endobj\n");
    }

    private static void WriteAscii(FileStream output, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
    }

    private static string FindFreeSiblingPath(string directory, string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int counter = 1; counter < 10000; counter++)
        {
            string candidate = Path.GetFullPath(
                Path.Combine(directory, $"{stem} ({counter}){extension}"));
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find a free output name for '{fileName}'.");
    }

    private static void DeletePartialOutput(string targetPath, bool existedBefore)
    {
        // Never delete a pre-existing user PDF; only clean artifacts this
        // call may have created.
        if (existedBefore)
        {
            return;
        }

        try
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }
        catch
        {
            // Best effort: cleanup must not mask the typed failure outcome.
        }
    }

    private static void DeleteDirectoryBestEffort(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best effort: cleanup must not mask the merge outcome.
        }
    }

    /// <summary>
    /// One parsed input PDF: all plain objects plus the page leaves in
    /// page-tree order. Only dictionary text is decoded; stream bytes stay
    /// as slices of the single input buffer released after each file.
    /// </summary>
    private sealed class ParsedPdf
    {
        public Dictionary<int, ParsedObject> Objects { get; } = new();

        public HashSet<int> LeafPages { get; } = new();

        public static ParsedPdf Parse(byte[] bytes, string sourcePath)
        {
            if (bytes.Length < PdfHeaderMarker.Length ||
                !bytes.AsSpan(0, PdfHeaderMarker.Length).SequenceEqual(PdfHeaderMarker))
            {
                throw new InvalidDataException($"'{sourcePath}' is not a PDF file (missing %PDF- header).");
            }

            var parsed = new ParsedPdf();
            foreach (ObjectSpan span in ScanObjects(bytes))
            {
                // Last occurrence wins (incremental-update semantics).
                parsed.Objects[span.Number] = new ParsedObject(
                    Latin1.GetString(bytes, span.DictionaryStart, span.DictionaryLength),
                    span.StreamStart.HasValue && span.StreamEnd.HasValue
                        ? bytes[span.StreamStart.Value..span.StreamEnd.Value]
                        : null);
            }

            if (parsed.Objects.Count == 0)
            {
                throw new InvalidDataException($"'{sourcePath}' contains no readable PDF objects.");
            }

            foreach (ParsedObject obj in parsed.Objects.Values)
            {
                if (ObjStmRegex.IsMatch(obj.Dictionary))
                {
                    throw new NotSupportedException(
                        $"Cannot merge '{sourcePath}': compressed object streams are not supported.");
                }

                if (EncryptRegex.IsMatch(obj.Dictionary))
                {
                    throw new NotSupportedException(
                        $"Cannot merge '{sourcePath}': encrypted PDFs are not supported.");
                }
            }

            KeyValuePair<int, ParsedObject>? catalog = null;
            foreach (var entry in parsed.Objects)
            {
                if (CatalogTypeRegex.IsMatch(entry.Value.Dictionary))
                {
                    catalog = entry;
                }
            }

            if (catalog is null)
            {
                throw new InvalidDataException($"'{sourcePath}' has no document catalog.");
            }

            Match pagesRef = NodePagesRefRegex.Match(catalog.Value.Value.Dictionary);
            if (!pagesRef.Success)
            {
                throw new InvalidDataException($"'{sourcePath}' catalog has no page tree reference.");
            }

            var visiting = new HashSet<int>();
            CollectLeaves(parsed, sourcePath, int.Parse(pagesRef.Groups[1].Value), visiting);

            if (parsed.LeafPages.Count == 0)
            {
                throw new InvalidDataException($"'{sourcePath}' page tree contains no pages.");
            }

            return parsed;
        }

        private static void CollectLeaves(
            ParsedPdf parsed, string sourcePath, int nodeNumber, HashSet<int> visiting)
        {
            if (!visiting.Add(nodeNumber))
            {
                throw new InvalidDataException($"'{sourcePath}' page tree has a cycle at object {nodeNumber}.");
            }

            try
            {
                if (!parsed.Objects.TryGetValue(nodeNumber, out ParsedObject? node))
                {
                    throw new NotSupportedException(
                        $"Cannot merge '{sourcePath}': page tree references missing object {nodeNumber} " +
                        "(possibly compressed object streams).");
                }

                Match kids = KidsRegex.Match(node.Dictionary);
                if (kids.Success)
                {
                    foreach (Match kid in RefRegex.Matches(kids.Groups[1].Value))
                    {
                        CollectLeaves(parsed, sourcePath, int.Parse(kid.Groups[1].Value), visiting);
                    }

                    return;
                }

                if (PageTypeRegex.IsMatch(node.Dictionary) ||
                    node.Dictionary.Contains("/MediaBox", StringComparison.Ordinal) ||
                    node.Dictionary.Contains("/Resources", StringComparison.Ordinal))
                {
                    parsed.LeafPages.Add(nodeNumber);
                    return;
                }

                throw new InvalidDataException(
                    $"'{sourcePath}' page tree node {nodeNumber} is neither a page nor a pages node.");
            }
            finally
            {
                visiting.Remove(nodeNumber);
            }
        }

        private static List<ObjectSpan> ScanObjects(byte[] bytes)
        {
            var spans = new List<ObjectSpan>();
            int i = 0;
            while (i < bytes.Length)
            {
                if (bytes[i] == (byte)'%')
                {
                    // Comment: skip to end of line.
                    while (i < bytes.Length && bytes[i] != (byte)'\n')
                    {
                        i++;
                    }

                    continue;
                }

                if (IsLineKeyword(bytes, i, "xref"))
                {
                    // Classic cross-reference table: no objects inside; skip
                    // to the trailer to avoid false header matches on offsets.
                    int trailerAt = IndexOfKeyword(bytes, i, "trailer");
                    i = trailerAt < 0 ? bytes.Length : trailerAt;
                    continue;
                }

                if (bytes[i] >= (byte)'0' && bytes[i] <= (byte)'9' &&
                    TryParseHeader(bytes, i, out int number, out int _, out int bodyStart))
                {
                    ObjectSpan? span = TryParseBody(bytes, bodyStart, number);
                    if (span is not null)
                    {
                        spans.Add(span);
                        i = span.ObjectEnd;
                        continue;
                    }
                }

                i++;
            }

            return spans;
        }

        private static bool TryParseHeader(byte[] bytes, int start, out int number, out int generation, out int bodyStart)
        {
            number = 0;
            generation = 0;
            bodyStart = 0;

            int i = start;
            int digits = 0;
            while (i < bytes.Length && bytes[i] >= (byte)'0' && bytes[i] <= (byte)'9')
            {
                number = number * 10 + (bytes[i] - (byte)'0');
                digits++;
                i++;
            }

            if (digits == 0 || digits > 10 || !SkipWhitespace(bytes, ref i))
            {
                return false;
            }

            digits = 0;
            while (i < bytes.Length && bytes[i] >= (byte)'0' && bytes[i] <= (byte)'9')
            {
                generation = generation * 10 + (bytes[i] - (byte)'0');
                digits++;
                i++;
            }

            if (digits == 0 || digits > 10 || !SkipWhitespace(bytes, ref i))
            {
                return false;
            }

            if (!MatchKeyword(bytes, i, "obj"))
            {
                return false;
            }

            i += 3;
            if (i < bytes.Length && !IsDelimiter(bytes[i]))
            {
                return false;
            }

            bodyStart = i;
            return true;
        }

        private static ObjectSpan? TryParseBody(byte[] bytes, int bodyStart, int number)
        {
            int i = bodyStart;
            while (i < bytes.Length)
            {
                if (MatchKeyword(bytes, i, "endobj"))
                {
                    return new ObjectSpan(number, bodyStart, i - bodyStart, null, null, i + 6);
                }

                if (MatchKeyword(bytes, i, "stream"))
                {
                    int afterKeyword = i + 6;
                    int dataStart = afterKeyword;
                    if (dataStart < bytes.Length && bytes[dataStart] == (byte)'\r')
                    {
                        dataStart++;
                    }

                    if (dataStart < bytes.Length && bytes[dataStart] == (byte)'\n')
                    {
                        dataStart++;
                    }
                    else
                    {
                        // "stream" not followed by EOL: false positive inside
                        // a string; keep scanning.
                        i++;
                        continue;
                    }

                    string dictionary = Latin1.GetString(bytes, bodyStart, i - bodyStart);
                    int? dataEnd = ResolveStreamEnd(bytes, dataStart, dictionary);
                    if (dataEnd is null)
                    {
                        return null;
                    }

                    int endObjAt = IndexOfKeyword(bytes, dataEnd.Value, "endobj");
                    if (endObjAt < 0)
                    {
                        return null;
                    }

                    return new ObjectSpan(
                        number, bodyStart, i - bodyStart, dataStart, dataEnd.Value, endObjAt + 6);
                }

                i++;
            }

            return null;
        }

        private static int? ResolveStreamEnd(byte[] bytes, int dataStart, string dictionary)
        {
            Match length = LengthRegex.Match(dictionary);
            if (length.Success && string.IsNullOrEmpty(length.Groups[2].Value) &&
                int.TryParse(length.Groups[1].Value, out int directLength) &&
                directLength >= 0 && (long)dataStart + directLength <= bytes.Length)
            {
                return dataStart + directLength;
            }

            int endStreamAt = IndexOfKeyword(bytes, dataStart, "endstream");
            return endStreamAt < 0 ? null : endStreamAt;
        }

        private static bool SkipWhitespace(byte[] bytes, ref int i)
        {
            int start = i;
            while (i < bytes.Length && IsWhitespace(bytes[i]))
            {
                i++;
            }

            return i > start;
        }

        private static bool IsWhitespace(byte b) =>
            b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or 0;

        private static bool IsDelimiter(byte b) =>
            IsWhitespace(b) || b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
                or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

        private static bool MatchKeyword(byte[] bytes, int pos, string keyword)
        {
            if (pos < 0 || (long)pos + keyword.Length > bytes.Length)
            {
                return false;
            }

            if (pos > 0 && !IsDelimiter(bytes[pos - 1]))
            {
                return false;
            }

            for (int k = 0; k < keyword.Length; k++)
            {
                if (bytes[pos + k] != (byte)keyword[k])
                {
                    return false;
                }
            }

            int after = pos + keyword.Length;
            return after >= bytes.Length || IsDelimiter(bytes[after]);
        }

        private static bool IsLineKeyword(byte[] bytes, int pos, string keyword)
        {
            if (!MatchKeyword(bytes, pos, keyword))
            {
                return false;
            }

            // Must start at a line boundary (classic xref sections do).
            int back = pos - 1;
            while (back >= 0 && (bytes[back] == (byte)' ' || bytes[back] == (byte)'\t'))
            {
                back--;
            }

            return back < 0 || bytes[back] == (byte)'\n' || bytes[back] == (byte)'\r';
        }

        private static int IndexOfKeyword(byte[] bytes, int from, string keyword)
        {
            for (int i = from; i < bytes.Length; i++)
            {
                if (MatchKeyword(bytes, i, keyword))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    private sealed record ObjectSpan(
        int Number,
        int DictionaryStart,
        int DictionaryLength,
        int? StreamStart,
        int? StreamEnd,
        int ObjectEnd);

    private sealed record ParsedObject(string Dictionary, byte[]? StreamData);
}
