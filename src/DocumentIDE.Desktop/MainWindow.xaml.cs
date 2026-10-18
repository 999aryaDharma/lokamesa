using System;
using System.IO;
using System.Windows;
using DocumentIDE.Desktop.WebView;
using Microsoft.Web.WebView2.Core;

namespace DocumentIDE.Desktop;

public partial class MainWindow : Window
{
    private WebAppHost? _appHost;
    private LocalSessionBridge? _sessionBridge;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _appHost = await WebAppHost.StartAsync();

            // WebView2 must not use its default profile location (next to the
            // executable): the app lives in Program Files, which a standard user
            // cannot write, and CreateAsync then fails with E_ACCESSDENIED before
            // any UI loads.
            string userDataFolder = WebViewUserDataFolder.Resolve();
            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: null);
            await WebView.EnsureCoreWebView2Async(env);

            string wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(wwwrootPath))
            {
                string repoWwwroot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DocumentIDE.Web", "dist"));
                if (Directory.Exists(repoWwwroot))
                {
                    wwwrootPath = repoWwwroot;
                }
                else
                {
                    // Never create an empty wwwroot under Program Files: it would
                    // either fail with E_ACCESSDENIED or produce a blank window.
                    throw new DirectoryNotFoundException(
                        $"The web assets were not found at '{wwwrootPath}'. Reinstall the application.");
                }
            }

            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "appassets.html2sav",
                wwwrootPath,
                CoreWebView2HostResourceAccessKind.Allow);

            _sessionBridge = new LocalSessionBridge(_appHost.SessionToken, _appHost.Port);
            // Await the pre-load script before navigating: otherwise the SPA can
            // boot without __DOCUMENTIDE_API_URL__ and every API call fails with
            // "Failed to fetch" (the packaged origin has no /api route).
            await _sessionBridge.AttachToWebViewAsync(WebView.CoreWebView2, _appHost.ApiUrl);

            WebView.NavigationStarting += (s, args) =>
            {
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var targetUri))
                {
                    if (!_sessionBridge.ShouldInjectHeader(targetUri, _appHost.Port) &&
                        !string.Equals(targetUri.Host, "appassets.html2sav", StringComparison.OrdinalIgnoreCase))
                    {
                    }
                }
            };

            bool isDev = string.Equals(Environment.GetEnvironmentVariable("DOCUMENTIDE_ENV"), "Development", StringComparison.OrdinalIgnoreCase);
            string devUrl = Environment.GetEnvironmentVariable("VITE_DEV_SERVER_URL") ?? "http://localhost:5173";

            if (isDev)
            {
                WebView.Source = new Uri(devUrl);
            }
            else
            {
                WebView.Source = new Uri("https://appassets.html2sav/index.html");
            }
        }
        catch (Exception ex)
        {
            ShowDiagnosticError($"Failed to initialize WebView2 host: {ex.Message}");
        }
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_appHost != null)
        {
            await _appHost.DisposeAsync();
            _appHost = null;
        }
    }

    private void ShowDiagnosticError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
    }
}