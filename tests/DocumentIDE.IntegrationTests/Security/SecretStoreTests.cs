using System;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.Security;
using DocumentIDE.Core.Settings;
using DocumentIDE.Infrastructure.Security;
using DocumentIDE.Infrastructure.Settings;
using Xunit;

namespace DocumentIDE.IntegrationTests.Security;

public class SecretStoreTests : IDisposable
{
    private readonly string _testDir;

    public SecretStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"docide_sec_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Round_Trip_Normal_Settings()
    {
        var settingsPath = Path.Combine(_testDir, "settings.json");
        var service = new SettingsService(settingsPath);

        var original = new AppSettings
        {
            Theme = "light",
            AutoSaveIntervalSeconds = 45,
            MaxRecoveryRetentionDays = 15,
            TelemetryOptOut = false
        };

        await service.SaveSettingsAsync(original);

        var loaded = await service.GetSettingsAsync();

        Assert.Equal("light", loaded.Theme);
        Assert.Equal(45, loaded.AutoSaveIntervalSeconds);
        Assert.Equal(15, loaded.MaxRecoveryRetentionDays);
        Assert.False(loaded.TelemetryOptOut);
    }

    [Fact]
    public async Task Secret_Can_Be_Stored_And_Read_By_Current_User_Context()
    {
        var secretsDir = Path.Combine(_testDir, "secrets");
        var secretStore = new WindowsSecretStore(secretsDir);

        var rawSecret = "ngrok_auth_token_secret_12345";
        await secretStore.SetSecretAsync("ngrok_token", new SecretValue(rawSecret));

        var retrieved = await secretStore.GetSecretAsync("ngrok_token");

        Assert.NotNull(retrieved);
        Assert.Equal(rawSecret, retrieved.Value);

        var keyFile = Path.Combine(secretsDir, "ngrok_token.bin");
        Assert.True(File.Exists(keyFile));
        var rawFileContent = await File.ReadAllTextAsync(keyFile);
        Assert.DoesNotContain(rawSecret, rawFileContent);
    }

    [Fact]
    public async Task Secret_Never_Appears_In_Logs_Or_ToString()
    {
        var secret = new SecretValue("super_secret_ai_key_999");

        Assert.Equal("[REDACTED]", secret.ToString());

        var json = System.Text.Json.JsonSerializer.Serialize(secret);
        Assert.DoesNotContain("super_secret_ai_key_999", json);
        Assert.Contains("[REDACTED]", json);
    }
}
