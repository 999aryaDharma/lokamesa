using System;
using System.Collections.Generic;
using System.Threading;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-01 — one controlled Git invocation.
///
/// Arguments are passed as a structured argument list, never as a concatenated
/// shell command string (spec 13 §18/§23). The runner never invokes a shell and
/// never interprets path syntax; callers that pass file paths must place
/// <c>--</c> before them so Git cannot treat a path as an option.
/// </summary>
/// <param name="Arguments">
/// Git arguments, e.g. <c>["status", "--porcelain=v2", "-z", "--branch"]</c>.
/// </param>
/// <param name="WorkingDirectory">
/// Directory the Git process runs in. Must exist; the runner does not fall back
/// to another directory.
/// </param>
/// <param name="Timeout">
/// Optional timeout override. <c>null</c> (default) uses the runner's configured
/// policy; <see cref="Timeout.InfiniteTimeSpan"/> disables the timeout for this
/// invocation (e.g. long remote operations).
/// </param>
/// <param name="EnvironmentOverrides">
/// Extra environment variables for this invocation. Keys in
/// <c>GitProcessRunnerOptions.ProtectedEnvironment</c> cannot be overridden.
/// </param>
public sealed record GitProcessRequest(
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan? Timeout = null,
    IReadOnlyDictionary<string, string>? EnvironmentOverrides = null)
{
    /// <summary>
    /// SCM-05 — capture stdout as raw bytes (<see cref="GitProcessResult.StandardOutputBytes"/>)
    /// instead of a text-only read. Required when the requested Git object is
    /// binary content (e.g. a DOCX blob read for a document-aware diff):
    /// UTF-8 text decoding is lossy for binary bytes, so the bytes are captured
    /// verbatim and <see cref="GitProcessResult.StandardOutput"/> additionally
    /// carries a lenient UTF-8 decode for diagnostics only.
    /// </summary>
    public bool CaptureBinaryOutput { get; init; }
}
