using System;
using System.IO;
using CcUsageMonitor.Core.Services;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

[Collection("NonParallel")]
public class LinuxAutostartEnvTests
{
    [Fact]
    public void LinuxAutostart_NullAutostartDir_ResolvesViaXDG_CONFIG_HOME()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var exe = "/opt/ccum/CC-Usage-Monitor";
        var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempDir);
            var autostart = new LinuxAutostart(exe);
            autostart.Enable();
            Assert.True(File.Exists(Path.Combine(tempDir, "autostart", "cc-usage-monitor.desktop")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
            Directory.Delete(tempDir, true);
        }
    }
}
