using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DocumentIDE.Api.Lifecycle;
using DocumentIDE.Core.Diagnostics;
using DocumentIDE.Desktop.WebView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;

namespace DocumentIDE.Desktop;

public partial class MainWindow : Window
{
    private WebAppHost? _appHost;
    private LocalSessionBridge? _sessionBridge;
    private RestartCoordinator? _restartCoordinator;
    private RuntimeHandshake? _runtimeHandshake;

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
            var restartCoordinator = new RestartCoordinator();
            _restartCoordinator = restartCoordinator;
            _appHost = await WebAppHost.StartAsync(configureServices: services =>
                services.AddSingleton<IRestartFlushBridge>(restartCoordinator));
            _appHost.ShutdownRequested += OnShutdownRequested;

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

            // DEV-UPD: the page-side save bridge and the one-run handshake the
            // local watcher reads before it stops this process.
            restartCoordinator.FlushAsync = FlushViaWebViewAsync;
            BuildInfo buildInfo = BuildInfo.Load(AppContext.BaseDirectory);
            _runtimeHandshake = RuntimeHandshake.Write(
                RuntimeHandshake.DefaultPath,
                new RuntimeHandshakePayload(
                    Environment.ProcessId,
                    _appHost.Port,
                    buildInfo.Version,
                    buildInfo.Sha,
                    _appHost.ControlToken));

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
            _appHost.ShutdownRequested -= OnShutdownRequested;
            await _appHost.DisposeAsync();
            _appHost = null;
        }

        _runtimeHandshake?.Dispose();
        _runtimeHandshake = null;
    }

    private void OnShutdownRequested()
    {
        Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
    }

    private Task<RestartFlushResult> FlushViaWebViewAsync(CancellationToken cancellationToken) =>
        Dispatcher.InvokeAsync(() => FlushViaWebViewCoreAsync(cancellationToken)).Task.Unwrap();

    private async Task<RestartFlushResult> FlushViaWebViewCoreAsync(CancellationToken cancellationToken)
    {
        CoreWebView2? core = WebView.CoreWebView2;
        if (core is null)
        {
            return new RestartFlushResult(false, "webview-unavailable");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            await core.ExecuteScriptAsync(RestartCoordinator.StartScript);

            while (true)
            {
                string raw = await core.ExecuteScriptAsync(RestartCoordinator.PollScript);
                RestartFlushResult? result = RestartCoordinator.ParsePollResult(raw);
                if (result is not null)
                {
                    return result;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return new RestartFlushResult(false, "flush-timeout");
        }
        catch (Exception ex)
        {
            return new RestartFlushResult(false, ex.Message);
        }
    }

    private void ShowDiagnosticError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
    }
}