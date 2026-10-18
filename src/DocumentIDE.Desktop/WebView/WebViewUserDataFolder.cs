using System;
using System.IO;

namespace DocumentIDE.Desktop.WebView;

/// <summary>
/// Resolves the WebView2 user-data folder.
///
/// WebView2 defaults to creating its profile next to the executable. The
/// installer places the app in <c>Program Files</c>, which a standard user
/// cannot write to, so <c>CoreWebView2Environment.CreateAsync()</c> fails with
/// <c>0x80070005 (E_ACCESSDENIED)</c> and the window only shows
/// "Initialization Error" - the UI never loads. The profile must live in the
/// per-user writable location instead.
/// </summary>
public static class WebViewUserDataFolder
{
    public static string Resolve(string? localAppData = null)
    {
        string root = string.IsNullOrWhiteSpace(localAppData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppData;

        return Path.Combine(root, "DocumentIDE", "WebView2");
    }
}
