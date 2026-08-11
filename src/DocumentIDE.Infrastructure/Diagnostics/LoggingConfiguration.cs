using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;
using System;
using System.IO;

namespace DocumentIDE.Infrastructure.Diagnostics
{
    /// <summary>
    /// Configures Serilog for structured, local, redacted diagnostics.
    /// </summary>
    public static class LoggingConfiguration
    {
        public static IHostBuilder UseDiagnostics(this IHostBuilder hostBuilder)
        {
            var logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DocumentIDE", "logs");

            Directory.CreateDirectory(logsDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.With<CorrelationIdEnricher>()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(logsDir, "log-.json"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:O} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .Filter.ByExcluding(log => LogRedaction.ContainsSensitiveData(log.MessageTemplate.Text))
                .CreateLogger();

            return hostBuilder.UseSerilog();
        }
    }

    /// <summary>
    /// Enricher that adds a CorrelationId property to every log event.
    /// </summary>
    internal class CorrelationIdEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var id = CorrelationIdProvider.CurrentId ?? Guid.NewGuid();
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", id));
        }
    }
}
