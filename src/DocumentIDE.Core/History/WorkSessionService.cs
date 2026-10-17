using System;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.History;

/// <summary>
/// SCM-09 — implementation of <see cref="IWorkSessionService"/>
/// (spec 13 §13/§24).
///
/// Deliberately Git-free: the service records Document IDE History and derives
/// commit correlations from ids the caller obtained after a successful commit.
/// It never invokes Git, so it cannot create an automatic commit, and it turns
/// a correlation-store failure into a typed <see cref="WorkSessionOutcome.StoreUnavailable"/>
/// result so an unrelated commit can never be invalidated by correlation.
/// </summary>
public sealed class WorkSessionService : IWorkSessionService
{
    private readonly IWorkSessionRepository _sessions;
    private readonly IDateTimeProvider _clock;

    public WorkSessionService(IWorkSessionRepository sessions, IDateTimeProvider? clock = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _clock = clock ?? new SystemDateTimeProvider();
    }

    public Task<WorkSessionResult> StartAsync(
        string? workspaceId,
        string? repositoryRoot,
        string? worktreeRoot,
        string? label,
        CancellationToken cancellationToken = default)
    {
        var session = new WorkSession
        {
            WorkspaceId = NullIfBlank(workspaceId),
            RepositoryRoot = NullIfBlank(repositoryRoot),
            WorktreeRoot = NullIfBlank(worktreeRoot),
            StartedAt = Now(),
            Label = NullIfBlank(label),
        };

        return ExecuteAsync(
            async token =>
            {
                await _sessions.CreateAsync(session, token);
                return new WorkSessionResult(WorkSessionOutcome.Succeeded, session, null);
            },
            cancellationToken);
    }

    public Task<WorkSessionResult> RecordChangedDocumentAsync(
        string? sessionId,
        string? documentId,
        string? path,
        CancellationToken cancellationToken = default)
    {
        string? id = NullIfBlank(sessionId);
        if (id is null)
        {
            return Rejected(
                WorkSessionOutcome.SessionRequired,
                "SessionRequired",
                "A work session id is required to record a changed-document association.");
        }

        string? document = NullIfBlank(documentId);
        if (document is null)
        {
            return Rejected(
                WorkSessionOutcome.DocumentRequired,
                "DocumentRequired",
                "A document id is required to record a changed-document association.");
        }

        return ExecuteAsync(
            async token =>
            {
                var association = new WorkSessionDocument
                {
                    SessionId = id,
                    DocumentId = document,
                    Path = NullIfBlank(path),
                    RecordedAt = Now(),
                };

                return await _sessions.RecordChangedDocumentAsync(association, token)
                    ? await LoadAsync(id, token)
                    : SessionNotFound(id);
            },
            cancellationToken);
    }

    public Task<WorkSessionResult> AttachCommitAsync(
        string? sessionId,
        string? commitId,
        string? shortId,
        string? summary,
        CancellationToken cancellationToken = default)
    {
        string? id = NullIfBlank(sessionId);
        if (id is null)
        {
            return Rejected(
                WorkSessionOutcome.SessionRequired,
                "SessionRequired",
                "A work session id is required to correlate a commit.");
        }

        string? commit = NullIfBlank(commitId);
        if (commit is null)
        {
            return Rejected(
                WorkSessionOutcome.CommitRequired,
                "CommitRequired",
                "A Git commit id is required to correlate a commit with a work session.");
        }

        return ExecuteAsync(
            async token =>
            {
                var correlation = new WorkSessionCommit
                {
                    SessionId = id,
                    CommitId = commit,
                    ShortId = NullIfBlank(shortId),
                    Summary = NullIfBlank(summary),
                    RecordedAt = Now(),
                };

                return await _sessions.AttachCommitAsync(correlation, token)
                    ? await LoadAsync(id, token)
                    : SessionNotFound(id);
            },
            cancellationToken);
    }

    public Task<WorkSessionResult> GetAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        string? id = NullIfBlank(sessionId);
        if (id is null)
        {
            return Rejected(
                WorkSessionOutcome.SessionRequired,
                "SessionRequired",
                "A work session id is required.");
        }

        return ExecuteAsync(token => LoadAsync(id, token), cancellationToken);
    }

    public Task<WorkSessionResult> DeleteAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        string? id = NullIfBlank(sessionId);
        if (id is null)
        {
            return Rejected(
                WorkSessionOutcome.SessionRequired,
                "SessionRequired",
                "A work session id is required.");
        }

        return ExecuteAsync(
            async token =>
            {
                if (!await _sessions.DeleteAsync(id, token))
                {
                    return SessionNotFound(id);
                }

                // Deriving metadata only: the Git commit the session described
                // is untouched and remains valid.
                return new WorkSessionResult(
                    WorkSessionOutcome.Succeeded,
                    null,
                    "Correlation data was deleted; the Git commit is unaffected.");
            },
            cancellationToken);
    }

    private async Task<WorkSessionResult> LoadAsync(string sessionId, CancellationToken cancellationToken)
    {
        WorkSession? session = await _sessions.GetByIdAsync(sessionId, cancellationToken);
        return session is null
            ? SessionNotFound(sessionId)
            : new WorkSessionResult(WorkSessionOutcome.Succeeded, session, null);
    }

    private async Task<WorkSessionResult> ExecuteAsync(
        Func<CancellationToken, Task<WorkSessionResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Correlation is optional derived metadata (spec 13 §24): a store
            // failure is typed and never silently swallowed, and it can never
            // invalidate the Git commit it failed to describe.
            return new WorkSessionResult(
                WorkSessionOutcome.StoreUnavailable,
                null,
                $"The Document IDE correlation store could not be used ({exception.GetType().Name}); " +
                "the Git commit is unaffected.");
        }
    }

    private static Task<WorkSessionResult> Rejected(WorkSessionOutcome outcome, string errorCode, string message) =>
        Task.FromResult(new WorkSessionResult(outcome, null, message, errorCode));

    private static WorkSessionResult SessionNotFound(string sessionId) =>
        new(
            WorkSessionOutcome.SessionNotFound,
            null,
            $"No work session '{sessionId}' is recorded in the correlation store.",
            "SessionNotFound");

    private string Now() => _clock.UtcNow.ToString("O");

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
