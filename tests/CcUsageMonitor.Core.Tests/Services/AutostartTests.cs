using System;
using System.IO;
using System.Runtime.InteropServices;
using CcUsageMonitor.Core.Services;
using CcUsageMonitor.Core.Tests.Fakes;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class AutostartTests
{
    // --- FakeAutostart round-trip tests ---

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
            try
            {
                autostart.Enable();
                Assert.True(autostart.IsEnabled());
                Assert.True(File.Exists(autostart.GetType().GetField("_shortcutPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(autostart) as string));
            }
            catch (InvalidOperationException)
            {
                // WScript.Shell unavailable
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
            // WScript.Shell unavailable
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
            var autostart = new MacAutostart(exe);
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
        var prevHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            Environment.SetEnvironmentVariable("HOME", tempDir);
            var autostart = new MacAutostart(exe);
            autostart.Enable();
            Assert.True(autostart.IsEnabled());
            var plistPath = autostart.GetType().GetField("_plistPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(autostart) as string;
            Assert.True(File.Exists(plistPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", prevHome);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MacAutostart_EnablePlistContainsExecutablePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor");
        var prevHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            Environment.SetEnvironmentVariable("HOME", tempDir);
            var autostart = new MacAutostart(exe);
            autostart.Enable();
            var plistPath = autostart.GetType().GetField("_plistPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(autostart) as string;
            var plist = File.ReadAllText(plistPath!);
            Assert.Contains(exe, plist);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", prevHome);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MacAutostart_DisableRemovesPlist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var exe = Path.Combine(tempDir, "cc-usage-monitor");
        var prevHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            Environment.SetEnvironmentVariable("HOME", tempDir);
            var autostart = new MacAutostart(exe);
            autostart.Enable();
            Assert.True(autostart.IsEnabled());
            autostart.Disable();
            Assert.False(autostart.IsEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", prevHome);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MacAutostart_NullExecutable_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MacAutostart(null!));
    }

    // --- LinuxAutostart: real-class tests with injected dir ---

    [Fact]
    public void LinuxAutostart_IsEnabledReturnsFalseWhenNoDesktop()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var exe = "/opt/ccum/CC-Usage-Monitor";

        try
        {
            var autostart = new LinuxAutostart(exe, tempDir);
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
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var exe = "/opt/ccum/CC-Usage-Monitor";

        try
        {
            var autostart = new LinuxAutostart(exe, tempDir);
            autostart.Enable();
            Assert.True(autostart.IsEnabled());
            Assert.True(File.Exists(Path.Combine(tempDir, "cc-usage-monitor.desktop")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LinuxAutostart_DisableRemovesDesktopFile()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var exe = "/opt/ccum/CC-Usage-Monitor";

        try
        {
            var autostart = new LinuxAutostart(exe, tempDir);
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
    public void LinuxAutostart_Enable_WritesExactDesktopFile()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var exe = "/opt/ccum/CC-Usage-Monitor";

        try
        {
            new LinuxAutostart(exe, tempDir).Enable();
            var actual = File.ReadAllText(Path.Combine(tempDir, "cc-usage-monitor.desktop"));
            var expected =
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=Claude Code Usage Monitor\n" +
                "Comment=Claude Code usage limits in your system tray\n" +
                $"Exec=\"{exe}\"\n" +
                "Terminal=false\n" +
                "Hidden=false\n" +
                "NoDisplay=false\n" +
                "X-GNOME-Autostart-enabled=true\n";
            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LinuxAutostart_Enable_QuotesSpaceInPath()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var exe = "/home/u/my apps/CC-Usage-Monitor";

        try
        {
            new LinuxAutostart(exe, tempDir).Enable();
            var actual = File.ReadAllText(Path.Combine(tempDir, "cc-usage-monitor.desktop"));
            var expected =
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=Claude Code Usage Monitor\n" +
                "Comment=Claude Code usage limits in your system tray\n" +
                $"Exec=\"{exe}\"\n" +
                "Terminal=false\n" +
                "Hidden=false\n" +
                "NoDisplay=false\n" +
                "X-GNOME-Autostart-enabled=true\n";
            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LinuxAutostart_NullExecutable_Throws()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Throws<ArgumentNullException>(() => new LinuxAutostart(null!, tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LinuxAutostart_CreatesMissingDir()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var exe = "/opt/ccum/CC-Usage-Monitor";
        var nestedDir = Path.Combine(tempDir, "sub", "autostart");

        try
        {
            var autostart = new LinuxAutostart(exe, nestedDir);
            autostart.Enable();
            Assert.True(File.Exists(Path.Combine(nestedDir, "cc-usage-monitor.desktop")));
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
            return;

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
            return;

        var autostart = AutostartFactory.Create("/usr/local/bin/cc-usage-monitor");
        Assert.IsType<MacAutostart>(autostart);
    }

    [Fact]
    public void AutostartFactory_Create_UsesLinuxAutostart_OnLinux()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var autostart = AutostartFactory.Create("/usr/bin/cc-usage-monitor");
        Assert.IsType<LinuxAutostart>(autostart);
    }

    [Fact]
    public void AutostartFactory_Create_NullExecutable_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AutostartFactory.Create(null!));
        Assert.Throws<ArgumentNullException>(() => AutostartFactory.Create(string.Empty));
    }
}
