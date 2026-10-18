using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Diagnostics
{
    public class DiagnosticsExportResult
    {
        public DateTime ExportTimestampUtc { get; set; } = DateTime.UtcNow;
        public string MachineName { get; set; } = Environment.MachineName;
        public string OsVersion { get; set; } = Environment.OSVersion.ToString();
        public List<string> LogFilesIncluded { get; set; } = new();
        public string RedactedLogsText { get; set; } = string.Empty;
        public string ExcludedContentSummary { get; set; } = "Source documents, DOCX binaries, and user files are excluded by default.";
    }

    public interface IDiagnosticsExportService
    {
        Task<DiagnosticsExportResult> CreateExportAsync();
    }
}
