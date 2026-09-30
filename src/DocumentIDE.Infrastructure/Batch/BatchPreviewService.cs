using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain.Repositories;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentIDE.Core.Docx;
using DocumentIDE.Infrastructure.Docx;

namespace DocumentIDE.Infrastructure.Batch;

public sealed class BatchPreviewService(IBatchPlanRepository plans)
{
    // ponytail: serialize selection saves; per-plan locks if review throughput requires it.
    private readonly SemaphoreSlim _selectionLock = new(1, 1);

    public async Task SaveSelectionAsync(string planId, BatchPreviewSelection selection, BatchScopeResult scope)
    {
        await _selectionLock.WaitAsync();
        try
        {
            var plan = await plans.GetByIdAsync(planId) ?? throw new BatchPreviewConflictException("UnknownPlan");
            if (plan.IsExpired(DateTimeOffset.UtcNow)) throw new BatchPreviewConflictException("ExpiredPlan");
            if (plan.Status != BatchPlanStatus.Previewed) throw new BatchPreviewConflictException("PreviewIncomplete");
            if (selection.Files == null || selection.Files.Any(f => f == null || f.SelectedOccurrenceIds == null) || selection.Files.Select(f => f.DocumentId).Distinct(StringComparer.Ordinal).Count() != selection.Files.Count)
                throw new BatchPreviewConflictException("InvalidSelection");
            foreach (var change in selection.Files)
            {
                var file = plan.Files.SingleOrDefault(f => f.DocumentId == change.DocumentId) ?? throw new BatchPreviewConflictException("UnknownDocument");
                var payload = JsonSerializer.Deserialize<BatchPreviewFile>(file.PlanPayloadJson!)!;
                if (change.SelectedOccurrenceIds.Except(payload.Occurrences.Select(o => o.Id), StringComparer.Ordinal).Any())
                    throw new BatchPreviewConflictException("UnknownOccurrence");
                if (payload.FailureCode != null && (change.Selected || change.SelectedOccurrenceIds.Count > 0))
                    throw new BatchPreviewConflictException("FailedFile");
            }
            foreach (var file in plan.Files.Where(f => !string.IsNullOrEmpty(f.ExpectedFingerprint)))
            {
                var document = scope.Documents.SingleOrDefault(d => d.Id == file.DocumentId) ?? throw new BatchPreviewConflictException("StalePlan");
                try
                {
                    var bytes = await ReadBoundedAsync(document.CurrentPath, CancellationToken.None);
                    if (Convert.ToHexString(SHA256.HashData(bytes)) != file.ExpectedFingerprint) throw new BatchPreviewConflictException("StalePlan");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                { throw new BatchPreviewConflictException("StalePlan"); }
            }
            foreach (var change in selection.Files)
            {
                var file = plan.Files.Single(f => f.DocumentId == change.DocumentId);
                file.Selected = change.Selected;
                file.SelectedChangeIds = change.SelectedOccurrenceIds.Distinct(StringComparer.Ordinal).ToList();
            }
            await plans.SavePlanAsync(plan);
        }
        finally { _selectionLock.Release(); }
    }

    public async Task BuildAsync(string planId, BatchPreviewRequest request, BatchScopeResult scope, CancellationToken cancellationToken, Action<double>? reportProgress = null)
    {
        if (string.IsNullOrEmpty(request.Find) || request.Find.Length > 4096 || request.Replacement == null || request.Replacement.Length > 4096)
            throw new ArgumentException("InvalidParameters");
        var plan = await plans.GetByIdAsync(planId) ?? throw new InvalidOperationException("UnknownPlan");
        plan.Files.Clear();
        var completed = 0;
        foreach (var document in scope.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new BatchOperationPlanFile { DocumentId = document.Id, Selected = false };
            IReadOnlyList<BatchPreviewOccurrence> occurrences = Array.Empty<BatchPreviewOccurrence>();
            string? failure = null;
            try
            {
                var bytes = await ReadBoundedAsync(document.CurrentPath, cancellationToken);
                if (Path.GetExtension(document.CurrentPath).Equals(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = new MemoryStream(bytes, writable: false);
                    var preflight = new DocxPackagePreflight().ValidatePackage(stream);
                    if (!preflight.IsValid) failure = preflight.ErrorCode.ToString();
                    else occurrences = new LogicalTextMatcher(new DocxPackagePreflight()).Search(stream, request.Find, new() { MaxMatches = 10000, CancellationToken = cancellationToken })
                        .Select(m => new BatchPreviewOccurrence(m.OccurrenceId, m.Origin.ToString(), m.PreviewContext)).ToArray();
                }
                else
                {
                    var text = new UTF8Encoding(false, true).GetString(bytes);
                    var matches = new List<BatchPreviewOccurrence>();
                    for (var start = 0; start < text.Length;)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var index = text.IndexOf(request.Find, start, StringComparison.Ordinal);
                        if (index < 0) break;
                        if (matches.Count == 10000) throw new InvalidDataException("OccurrenceBudgetExceeded");
                        matches.Add(new($"body:{index}", "Body", text.Substring(Math.Max(0, index - 30), Math.Min(text.Length, index + request.Find.Length + 30) - Math.Max(0, index - 30))));
                        start = index + request.Find.Length;
                    }
                    occurrences = matches;
                }
                if (failure == null)
                {
                    if (occurrences.Count > 10000) throw new InvalidDataException("OccurrenceBudgetExceeded");
                    file.ExpectedFingerprint = Convert.ToHexString(SHA256.HashData(bytes));
                    file.MatchCount = occurrences.Count;
                    file.Selected = occurrences.Count > 0;
                    file.SelectedChangeIds = occurrences.Select(o => o.Id).ToList();
                }
            }
            catch (InvalidDataException) { failure = "ResourceLimit"; }
            catch (DecoderFallbackException) { failure = "InvalidEncoding"; }
            catch (UnauthorizedAccessException) { failure = "Unavailable"; }
            catch (IOException) { failure = "Unavailable"; }
            catch (InvalidOperationException) { failure = "InvalidPackage"; }
            if (failure != null) occurrences = Array.Empty<BatchPreviewOccurrence>();
            file.PlanPayloadJson = JsonSerializer.Serialize(new BatchPreviewFile(document.Id, Path.GetFileName(document.CurrentPath), file.Selected, "Unknown", failure, occurrences));
            plan.Files.Add(file);
            reportProgress?.Invoke(++completed * 100d / Math.Max(1, scope.Documents.Count));
        }
        cancellationToken.ThrowIfCancellationRequested();
        plan.Status = BatchPlanStatus.Previewed;
        await plans.SavePlanAsync(plan);
    }

    internal static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        for (string? component = path; component != null; component = Path.GetDirectoryName(component))
            if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0) throw new IOException("ReparsePoint");
        var limit = Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase) ? 100 * 1024 * 1024 : 10 * 1024 * 1024;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (stream.Length > limit) throw new InvalidDataException("FileTooLarge");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        if (stream.ReadByte() != -1) throw new IOException("SourceChanged");
        return bytes;
    }
}

public sealed class BatchPreviewConflictException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
