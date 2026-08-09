namespace DocumentIDE.Core.Settings;

public class AppSettings
{
    public string Theme { get; set; } = "dark";
    public int AutoSaveIntervalSeconds { get; set; } = 30;
    public int MaxRecoveryRetentionDays { get; set; } = 30;
    public long RecoveryCapacityBudgetBytes { get; set; } = 5368709120L;
    public string DefaultView { get; set; } = "canvas";
    public bool TelemetryOptOut { get; set; } = true;
}
