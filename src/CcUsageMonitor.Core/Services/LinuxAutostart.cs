using System;
using System.IO;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Linux autostart via .desktop entry in ~/.config/autostart (or $XDG_CONFIG_HOME/autostart).
/// Per spec §4.8.
/// </summary>
public class LinuxAutostart : IAutostart
{
    private readonly string _executablePath;
    private readonly string _desktopPath;

    /// <param name="executablePath">Full path to the application binary.</param>
    /// <param name="autostartDir">Optional autostart directory for testing. When null, resolves via XDG_CONFIG_HOME or HOME.</param>
    public LinuxAutostart(string executablePath, string? autostartDir = null)
    {
        _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
        var resolvedDir = autostartDir ?? ResolveAutostartDir();
        Directory.CreateDirectory(resolvedDir);
        _desktopPath = Path.Combine(resolvedDir, "cc-usage-monitor.desktop");
    }

    private string ResolveAutostartDir()
    {
        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return !string.IsNullOrEmpty(xdgConfig)
            ? Path.Combine(xdgConfig, "autostart")
            : Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? throw new InvalidOperationException("HOME not set."), ".config", "autostart");
    }

    public bool IsEnabled()
        => File.Exists(_desktopPath);

    public void Enable()
    {
        var content =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=Claude Code Usage Monitor\n" +
            "Comment=Claude Code usage limits in your system tray\n" +
            $"Exec=\"{_executablePath}\"\n" +
            "Terminal=false\n" +
            "Hidden=false\n" +
            "NoDisplay=false\n" +
            "X-GNOME-Autostart-enabled=true\n";
        File.WriteAllText(_desktopPath, content);
    }

    public void Disable()
    {
        if (File.Exists(_desktopPath))
            File.Delete(_desktopPath);
    }
}
