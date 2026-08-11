using System;
using System.IO;
using System.Xml.Linq;
using Microsoft.Win32;

namespace DocumentIDE.Core.Security;

public record PrerequisiteCheckResult(
    bool IsWebView2Installed,
    string? InstalledVersion,
    string DiagnosticSummary
);

public static class PrerequisiteValidator
{
    private const string WebView2RegKeySys = @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    private const string WebView2RegKeyUser = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    public static PrerequisiteCheckResult ValidateWebView2Runtime()
    {
        string? version = GetWebView2InstalledVersion();
        bool installed = !string.IsNullOrEmpty(version);

        string summary = installed
            ? $"WebView2 Evergreen Runtime detected (Version: {version})."
            : "WebView2 Evergreen Runtime missing. Please install Microsoft Edge WebView2 Runtime before launching Document IDE.";

        return new PrerequisiteCheckResult(installed, version, summary);
    }

    public static string? GetWebView2InstalledVersion()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var sysKey = Registry.LocalMachine.OpenSubKey(WebView2RegKeySys);
                var sysVer = sysKey?.GetValue("pv")?.ToString();
                if (!string.IsNullOrEmpty(sysVer) && sysVer != "0.0.0.0")
                {
                    return sysVer;
                }

                using var userKey = Registry.CurrentUser.OpenSubKey(WebView2RegKeyUser);
                var userVer = userKey?.GetValue("pv")?.ToString();
                if (!string.IsNullOrEmpty(userVer) && userVer != "0.0.0.0")
                {
                    return userVer;
                }
            }
        }
        catch
        {
            // Ignore registry lookup exceptions and fallback
        }

        return null;
    }

    public static bool VerifyUninstallProtectionPolicy(string wxsFilePath)
    {
        if (!File.Exists(wxsFilePath))
        {
            return false;
        }

        try
        {
            string content = File.ReadAllText(wxsFilePath);

            // High-level safety policy: Installer manifests MUST NOT contain destructive targeting of user profile or documents
            string[] forbiddenPatterns = ["USERPROFILE", "PersonalFolder", "DesktopFolder", "*.docx", "*.md", "*.sqlite"];
            foreach (var pattern in forbiddenPatterns)
            {
                if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
