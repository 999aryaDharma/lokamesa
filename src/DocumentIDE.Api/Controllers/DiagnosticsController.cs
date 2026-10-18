using DocumentIDE.Core.Diagnostics;
using DocumentIDE.Infrastructure.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DocumentIDE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DiagnosticsController : ControllerBase
    {
        private readonly IDiagnosticsExportService _exportService;
        private static readonly string LogsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DocumentIDE",
            "logs");

        public DiagnosticsController(IDiagnosticsExportService? exportService = null)
        {
            _exportService = exportService ?? new DiagnosticsExportService();
        }

        // Owner‑only endpoint that returns the latest log file (redacted)
        [HttpGet("latest")]
        [Authorize(Roles = "Owner")]
        public IActionResult GetLatestLog()
        {
            if (!Directory.Exists(LogsDirectory))
            {
                return NotFound("Log directory not found.");
            }

            var logFiles = Directory.GetFiles(LogsDirectory, "log-*.json");
            if (logFiles.Length == 0)
            {
                return NotFound("No log files available.");
            }

            var latestFile = logFiles.OrderByDescending(Path.GetFileName).First();
            var rawContent = System.IO.File.ReadAllText(latestFile);
            var redactedContent = LogRedaction.RedactSensitiveData(rawContent);
            return Content(redactedContent, "application/json");
        }

        // Owner-only endpoint that exports redacted system diagnostics excluding source documents by default
        [HttpPost("export")]
        [HttpGet("export")]
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> ExportDiagnostics()
        {
            var export = await _exportService.CreateExportAsync();
            return Ok(export);
        }
    }
}
