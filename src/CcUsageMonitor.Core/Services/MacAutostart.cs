using System;
using System.IO;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// macOS autostart via LaunchAgent plist.
/// Per spec §4.8 — writes a standard plist to ~/Library/LaunchAgents.
/// </summary>
public class MacAutostart : IAutostart
{
    private readonly string _plistPath;
    private readonly string _executablePath;

    /// <param name="executablePath">Full path to the application binary.</param>
    public MacAutostart(string executablePath)
    {
        _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
        var home = Environment.GetEnvironmentVariable("HOME")
                   ?? throw new InvalidOperationException("HOME environment variable not set.");
        var agentsDir = Path.Combine(home, "Library", "LaunchAgents");
        Directory.CreateDirectory(agentsDir);
        _plistPath = Path.Combine(agentsDir, "com.cc-usage-monitor.plist");
    }

    public bool IsEnabled()
        => File.Exists(_plistPath);

    public void Enable()
    {
        var plist = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>com.cc-usage-monitor</string>
    <key>ProgramArguments</key>
    <array>
        <string>{EscapeXml(_executablePath)}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>
";
        File.WriteAllText(_plistPath, plist);
    }

    public void Disable()
    {
        if (File.Exists(_plistPath))
            File.Delete(_plistPath);
    }

    private static string EscapeXml(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
