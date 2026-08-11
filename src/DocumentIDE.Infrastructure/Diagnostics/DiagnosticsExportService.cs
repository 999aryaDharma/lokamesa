using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DocumentIDE.Core.Diagnostics;

namespace DocumentIDE.Infrastructure.Diagnostics
{
    public class DiagnosticsExportService : IDiagnosticsExportService
    {
        private readonly string _logsDirectory;

        public DiagnosticsExportService(string? logsDirectory = null)
        {
            _logsDirectory = logsDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DocumentIDE",
                "logs");
        }

        public async Task<DiagnosticsExportResult> CreateExportAsync()
        {
            var result = new DiagnosticsExportResult();

            if (!Directory.Exists(_logsDirectory))
            {
                result.RedactedLogsText = "No logs directory found.";
                return result;
            }

            var logFiles = Directory.GetFiles(_logsDirectory, "log-*.json")
                .OrderByDescending(Path.GetFileName)
                .Take(7) // Export last 7 days of logs
                .ToList();

            var combinedLogs = new StringBuilder();

            foreach (var logFile in logFiles)
            {
                result.LogFilesIncluded.Add(Path.GetFileName(logFile));
                var rawContent = await File.ReadAllTextAsync(logFile);
                var redacted = LogRedaction.RedactSensitiveData(rawContent);
                combinedLogs.AppendLine($"--- LOG FILE: {Path.GetFileName(logFile)} ---");
                combinedLogs.AppendLine(redacted);
            }

            result.RedactedLogsText = combinedLogs.ToString();
            return result;
        }
    }
}
