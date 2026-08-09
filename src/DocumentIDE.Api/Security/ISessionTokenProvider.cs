namespace DocumentIDE.Api.Security;

public interface ISessionTokenProvider
{
    string SessionToken { get; }
    bool ValidateToken(string? token);
}
