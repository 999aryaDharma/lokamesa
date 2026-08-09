using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace DocumentIDE.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await WebView.EnsureCoreWebView2Async(env);

            string devUrl = Environment.GetEnvironmentVariable("VITE_DEV_SERVER_URL") ?? "http://localhost:5173";
            bool isDev = string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Environment.GetEnvironmentVariable("DOCUMENTIDE_ENV"), "Development", StringComparison.OrdinalIgnoreCase) ||
                         System.Diagnostics.Debugger.IsAttached;

            if (isDev)
            {
                WebView.Source = new Uri(devUrl);
            }
            else
            {
                string staticIndexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
                if (File.Exists(staticIndexPath))
                {
                    WebView.Source = new Uri(staticIndexPath);
                }
                else
                {
                    WebView.Source = new Uri(devUrl);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}