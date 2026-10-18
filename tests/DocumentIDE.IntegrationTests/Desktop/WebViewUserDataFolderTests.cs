using System.IO;
using DocumentIDE.Desktop.WebView;
using Xunit;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// Regression pin for the packaged-app startup failure: WebView2's default
/// profile location is next to the executable, and the installer puts the app
/// in Program Files. A standard user cannot write there, so CreateAsync failed
/// with E_ACCESSDENIED and the app only ever showed "Initialization Error".
/// </summary>
public sealed class WebViewUserDataFolderTests
{
    [Fact]
    public void Resolve_UsesThePerUserWritableLocation()
    {
        string path = WebViewUserDataFolder.Resolve(@"C:\Users\someone\AppData\Local");

        Assert.Equal(
            Path.Combine(@"C:\Users\someone\AppData\Local", "DocumentIDE", "WebView2"),
            path);
    }

    [Fact]
    public void Resolve_IsNotUnderTheApplicationDirectory()
    {
        string path = WebViewUserDataFolder.Resolve(@"C:\Users\someone\AppData\Local");

        Assert.False(
            path.StartsWith(@"C:\Program Files", System.StringComparison.OrdinalIgnoreCase),
            $"The WebView2 profile must not be written under Program Files: {path}");
    }
}
