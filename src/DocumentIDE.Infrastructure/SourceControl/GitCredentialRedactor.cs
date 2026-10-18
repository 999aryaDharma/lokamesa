using System;
using System.Text.RegularExpressions;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-13 — removes credential material from Git diagnostics before it can reach
/// an outcome message, an error or a log line (spec 13 §14.4/§20/§23).
///
/// Git echoes the remote URL in its push output (for example
/// <c>To https://user:password@host/repo.git</c> and
/// <c>fatal: unable to access 'https://user:password@host/repo.git/'</c>). A
/// remote URL may therefore carry URL <i>userinfo</i>, which is a credential.
/// Every string this type produces replaces the userinfo portion of any URL with
/// <c>***</c>, so a remote URL that contains userinfo can never appear in an
/// outcome message, error or log line.
///
/// Only the userinfo of an explicit <c>scheme://userinfo@host</c> URL is
/// removed; scp-style SSH locations (<c>user@host:path</c>) carry no password
/// and are left untouched.
/// </summary>
public static class GitCredentialRedactor
{
    private const string RedactedUserInfo = "${scheme}://***@";

    /// <summary>
    /// Matches the userinfo of a URI with an explicit scheme, up to (and
    /// including) the <c>@</c> that separates it from the host. The userinfo
    /// character class excludes <c>/</c> and whitespace, so it can never swallow
    /// the path or a following token - but it deliberately <i>does</i> allow
    /// <c>@</c> and is greedy, so a password that itself contains the separator
    /// (<c>https://user:p@ss@host/repo.git</c>) is consumed as one userinfo
    /// instead of leaving its tail behind.
    /// </summary>
    private static readonly Regex UrlUserInfoPattern = new(
        @"(?<scheme>[A-Za-z][A-Za-z0-9+.\-]*)://[^/\s]*@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns <paramref name="value"/> with the userinfo of every explicit-scheme
    /// URL replaced by <c>***</c>; <c>null</c>/empty values are returned as-is.
    /// </summary>
    public static string? Redact(string? value) =>
        string.IsNullOrEmpty(value) ? value : UrlUserInfoPattern.Replace(value, RedactedUserInfo);

    /// <summary>
    /// Returns a copy of a Git failure with its captured streams and message
    /// redacted, so a credential in a remote URL cannot be carried onto a typed
    /// outcome (and thence into a log line).
    /// </summary>
    public static GitProcessResult Sanitize(GitProcessResult failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure with
        {
            StandardOutput = Redact(failure.StandardOutput) ?? string.Empty,
            StandardError = Redact(failure.StandardError) ?? string.Empty,
            ErrorMessage = Redact(failure.ErrorMessage),
        };
    }
}
