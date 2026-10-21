namespace DocumentIDE.Api.Security;

public interface ISessionTokenProvider
{
    string SessionToken { get; }

    /// <summary>
    /// Per-run token for the <c>/api/app/*</c> control surface. It is handed to
    /// the local auto-updater through <c>runtime.json</c> and is never accepted
    /// on document or mutation routes.
    /// </summary>
    string ControlToken { get; }

    bool ValidateToken(string? token);
    bool ValidateControlToken(string? token);
}
