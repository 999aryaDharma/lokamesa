using DocumentIDE.Desktop.WebView;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// DEV-UPD — <c>runtime.json</c> is the only handshake between the app and the
/// local watcher: pid, port, build identity and the per-run control token.
/// </summary>
public sealed class RuntimeHandshakeTests
{
    [Fact]
    public void Writes_camel_case_payload_and_deletes_it_on_dispose()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string path = Path.Combine(dir, "runtime.json");

        using (RuntimeHandshake.Write(
            path,
            new RuntimeHandshakePayload(1, 2, "1.0.2.0", "abc", "tok")))
        {
            Assert.True(File.Exists(path));
            string json = File.ReadAllText(path);
            Assert.Contains("\"pid\":1", json);
            Assert.Contains("\"port\":2", json);
            Assert.Contains("\"version\":\"1.0.2.0\"", json);
            Assert.Contains("\"sha\":\"abc\"", json);
            Assert.Contains("\"controlToken\":\"tok\"", json);
        }

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Default_path_lives_under_local_app_data()
    {
        string path = RuntimeHandshake.DefaultPath;

        Assert.EndsWith(Path.Combine("DocumentIDE", "runtime.json"), path);
    }
}
