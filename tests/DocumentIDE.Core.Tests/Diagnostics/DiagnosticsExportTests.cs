using System;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Infrastructure.Diagnostics;
using Xunit;

namespace DocumentIDE.Core.Tests.Diagnostics
{
    public class DiagnosticsExportTests
    {
        [Fact]
        public async Task CreateExportAsync_ExcludesSourceDocuments_AndRedactsSensitiveLogs()
        {
            var tempLogsDir = Path.Combine(Path.GetTempPath(), "DocIDELogTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempLogsDir);

            try
            {
                var sampleLogPath = Path.Combine(tempLogsDir, "log-20260811.json");
                var rawLogText = "2026-08-11T22:00:00 [INF] Batch operation 123 started with bearer_token: Bearer secret_1234567890abcdef and body: { \"documentBody\": \"User private text\" }";
                await File.WriteAllTextAsync(sampleLogPath, rawLogText);

                var exportService = new DiagnosticsExportService(tempLogsDir);
                var result = await exportService.CreateExportAsync();

                Assert.NotNull(result);
                Assert.Single(result.LogFilesIncluded);
                Assert.Contains("log-20260811.json", result.LogFilesIncluded);

                // Verify source document exclusion statement is explicitly included
                Assert.Contains("Source documents", result.ExcludedContentSummary);

                // Verify secrets and document body were redacted in export
                Assert.DoesNotContain("secret_1234567890abcdef", result.RedactedLogsText);
                Assert.DoesNotContain("User private text", result.RedactedLogsText);
                Assert.Contains("[REDACTED_SECRET]", result.RedactedLogsText);
                Assert.Contains("[REDACTED_DOCUMENT_BODY]", result.RedactedLogsText);
            }
            finally
            {
                if (Directory.Exists(tempLogsDir))
                {
                    Directory.Delete(tempLogsDir, true);
                }
            }
        }
    }
}
