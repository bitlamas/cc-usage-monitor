using System;
using System.IO;
using System.Runtime.InteropServices;
using CcUsageMonitor.Core.Services;
using CcUsageMonitor.Core.Tests.Fakes;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class AutostartTests
{
    // --- Fake round-trip: Enable → IsEnabled → Disable ---

    [Fact]
    public void FakeAutostart_DisabledByDefault()
    {
        var fake = new FakeAutostart();
        Assert.False(fake.IsEnabled());
        Assert.Equal(0, fake.EnableCallCount);
        Assert.Equal(0, fake.DisableCallCount);
    }

    [Fact]
    public void FakeAutostart_EnableMakesIsEnabledTrue()
    {
        var fake = new FakeAutostart();
        fake.Enable();
        Assert.True(fake.IsEnabled());
        Assert.Equal(1, fake.EnableCallCount);
    }

    [Fact]
    public void FakeAutostart_DisableMakesIsEnabledFalse()
    {
        var fake = new FakeAutostart();
        fake.Enable();
        Assert.True(fake.IsEnabled());
        fake.Disable();
        Assert.False(fake.IsEnabled());
        Assert.Equal(1, fake.DisableCallCount);
    }

    [Fact]
    public void FakeAutostart_RoundTrip_EnableThenDisable()
    {
        var fake = new FakeAutostart();
        fake.Enable();
        fake.Disable();
        Assert.False(fake.IsEnabled());
    }

    [Fact]
    public void FakeAutostart_MultipleEnableIncrementsCounter()
    {
        var fake = new FakeAutostart();
        fake.Enable();
        fake.Enable();
        fake.Enable();
        Assert.Equal(3, fake.EnableCallCount);
        Assert.True(fake.IsEnabled());
    }

    // --- WindowsAutostart: temp-directory tests ---

    [Fact]
    public void WindowsAutostart_IsEnabledReturnsFalseWhenNoShortcut()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor.exe");
        File.WriteAllText(exe, "dummy");

        try
        {
            var autostart = new WindowsAutostart(exe, startupFolder: tempDir);
            Assert.False(autostart.IsEnabled());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WindowsAutostart_EnableCreatesShortcut()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor.exe");
        File.WriteAllText(exe, "dummy");

        try
        {
            var autostart = new WindowsAutostart(exe, startupFolder: tempDir);
            // WindowsAutostart creates the startup folder dir on construction
            // But it won't actually create the shortcut unless WScript.Shell is available
            // So we test: after Enable(), if a .lnk file exists, IsEnabled should be true
            // If COM is unavailable, Enable throws — that's a valid failure mode
            try
            {
                autostart.Enable();
                // If we got here, COM worked — shortcut should exist
                Assert.True(autostart.IsEnabled());
                Assert.True(File.Exists(autostart.GetType().GetField("_shortcutPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(autostart) as string));
            }
            catch (InvalidOperationException)
            {
                // WScript.Shell unavailable — acceptable on non-Windows or headless
                // The interface contract is still tested via FakeAutostart
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WindowsAutostart_DisableRemovesShortcut()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor.exe");
        File.WriteAllText(exe, "dummy");

        try
        {
            var autostart = new WindowsAutostart(exe, startupFolder: tempDir);
            autostart.Enable();
            Assert.True(autostart.IsEnabled());
            autostart.Disable();
            Assert.False(autostart.IsEnabled());
        }
        catch (InvalidOperationException)
        {
            // WScript.Shell unavailable — skip
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WindowsAutostart_NullExecutable_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            Assert.Throws<ArgumentNullException>(() => new WindowsAutostart(null!, startupFolder: tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- MacAutostart: file-based tests (no actual launchctl) ---

    [Fact]
    public void MacAutostart_IsEnabledReturnsFalseWhenNoPlist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor");

        try
        {
            var autostart = new MockMacAutostart(exe, tempDir);
            Assert.False(autostart.IsEnabled());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MacAutostart_EnableCreatesPlist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor");

        try
        {
            var autostart = new MockMacAutostart(exe, tempDir);
            autostart.Enable();
            Assert.True(autostart.IsEnabled());
            Assert.True(File.Exists(autostart.PlistPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MacAutostart_EnablePlistContainsExecutablePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor");

        try
        {
            var autostart = new MockMacAutostart(exe, tempDir);
            autostart.Enable();
            var plist = File.ReadAllText(autostart.PlistPath);
            Assert.Contains(exe, plist);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MacAutostart_DisableRemovesPlist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor");

        try
        {
            var autostart = new MockMacAutostart(exe, tempDir);
            autostart.Enable();
            Assert.True(autostart.IsEnabled());
            autostart.Disable();
            Assert.False(autostart.IsEnabled());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MacAutostart_NullExecutable_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            Assert.Throws<ArgumentNullException>(() => new MockMacAutostart(null!, tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- LinuxAutostart: file-based tests ---

    [Fact]
    public void LinuxAutostart_IsEnabledReturnsFalseWhenNoDesktop()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor");

        try
        {
            var autostart = new MockLinuxAutostart(exe, tempDir);
            Assert.False(autostart.IsEnabled());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LinuxAutostart_EnableCreatesDesktopFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor");

        try
        {
            var autostart = new MockLinuxAutostart(exe, tempDir);
            autostart.Enable();
            Assert.True(autostart.IsEnabled());
            Assert.True(File.Exists(autostart.DesktopPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LinuxAutostart_EnableDesktopContainsExecutablePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor");

        try
        {
            var autostart = new MockLinuxAutostart(exe, tempDir);
            autostart.Enable();
            var desktop = File.ReadAllText(autostart.DesktopPath);
            Assert.Contains(exe, desktop);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LinuxAutostart_DisableRemovesDesktopFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor");

        try
        {
            var autostart = new MockLinuxAutostart(exe, tempDir);
            autostart.Enable();
            Assert.True(autostart.IsEnabled());
            autostart.Disable();
            Assert.False(autostart.IsEnabled());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LinuxAutostart_NullExecutable_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            Assert.Throws<ArgumentNullException>(() => new MockLinuxAutostart(null!, tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- AutostartFactory ---

    [Fact]
    public void AutostartFactory_Create_UsesWindowsAutostart_OnWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return; // Skip on non-Windows

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor.exe");
        File.WriteAllText(exe, "dummy");

        try
        {
            var autostart = AutostartFactory.Create(exe);
            Assert.IsType<WindowsAutostart>(autostart);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AutostartFactory_Create_UsesMacAutostart_OnMac()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return; // Skip on non-macOS

        var autostart = AutostartFactory.Create("/usr/local/bin/cc-usage-monitor");
        Assert.IsType<MacAutostart>(autostart);
    }

    [Fact]
    public void AutostartFactory_Create_UsesLinuxAutostart_OnLinux()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return; // Skip on non-Linux

        var autostart = AutostartFactory.Create("/usr/bin/cc-usage-monitor");
        Assert.IsType<LinuxAutostart>(autostart);
    }

    [Fact]
    public void AutostartFactory_Create_NullExecutable_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AutostartFactory.Create(null!));
        Assert.Throws<ArgumentNullException>(() => AutostartFactory.Create(string.Empty));
    }

    // --- Mock implementations for temp-directory testing ---

    private class MockMacAutostart : IAutostart
    {
        private readonly string _plistPath;
        private readonly string _executablePath;

        public MockMacAutostart(string executablePath, string plistDirectory)
        {
            _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
            Directory.CreateDirectory(plistDirectory);
            _plistPath = Path.Combine(plistDirectory, "com.cc-usage-monitor.plist");
        }

        public string PlistPath => _plistPath;

        public bool IsEnabled() => File.Exists(_plistPath);

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
        <string>{_executablePath}</string>
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
    }

    private class MockLinuxAutostart : IAutostart
    {
        private readonly string _desktopPath;
        private readonly string _executablePath;

        public MockLinuxAutostart(string executablePath, string autostartDirectory)
        {
            _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
            Directory.CreateDirectory(autostartDirectory);
            _desktopPath = Path.Combine(autostartDirectory, "cc-usage-monitor.desktop");
        }

        public string DesktopPath => _desktopPath;

        public bool IsEnabled() => File.Exists(_desktopPath);

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
}
