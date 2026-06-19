using System;
using System.IO;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Linux autostart via .desktop entry in ~/.config/autostart (or $XDG_CONFIG_HOME/autostart).
/// Per spec §4.8.
/// </summary>
public class LinuxAutostart : IAutostart
{
    private readonly string _desktopPath;
    private readonly string _executablePath;

    /// <param name="executablePath">Full path to the application binary.</param>
    public LinuxAutostart(string executablePath)
    {
        _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var autostartDir = !string.IsNullOrEmpty(xdgConfig)
            ? Path.Combine(xdgConfig, "autostart")
            : Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? throw new InvalidOperationException("HOME not set."), ".config", "autostart");
        Directory.CreateDirectory(autostartDir);
        _desktopPath = Path.Combine(autostartDir, "cc-usage-monitor.desktop");
    }

    public bool IsEnabled()
        => File.Exists(_desktopPath);

    public void Enable()
    {
        var desktop = $@"[Desktop Entry]
Type=Application
Name=cc-usage-monitor
Comment=Claude Code usage monitor
Exec={_executablePath}
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
";
        File.WriteAllText(_desktopPath, desktop);
    }

    public void Disable()
    {
        if (File.Exists(_desktopPath))
            File.Delete(_desktopPath);
    }
}
