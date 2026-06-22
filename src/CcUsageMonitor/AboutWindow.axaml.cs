using System;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;

namespace CcUsageMonitor;

/// <summary>
/// Small "About" popover — app name, version, and a clickable GitHub link.
/// Borderless dark window matching the detail flyout; dismisses on focus loss.
/// </summary>
public partial class AboutWindow : Window
{
    private const string GithubUrl = "https://github.com/bitlamas/cc-usage-monitor";

    public AboutWindow()
    {
        InitializeComponent();

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v != null ? $"Version {v.Major}.{v.Minor}.{v.Build}" : "Version 1.0.0";

        GithubLink.Click += (_, _) => OpenUrl(GithubUrl);
        CloseButton.Click += (_, _) => Close();
        Deactivated += (_, _) => Close();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // Opening a browser is best-effort.
        }
    }
}
