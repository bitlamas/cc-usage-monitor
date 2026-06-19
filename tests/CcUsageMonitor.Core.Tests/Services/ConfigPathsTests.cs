using CcUsageMonitor.Core.Services;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class ConfigPathsTests
{
    [Fact]
    public void ConfigPaths_Windows_UsesAppData()
    {
        var env = new Dictionary<string, string>
        {
            { "USERPROFILE", "C:\\Users\\test" },
            { "APPDATA", "C:\\Users\\test\\AppData\\Roaming" }
        };
        var path = ConfigPaths.Resolve(OSPlatform.Windows, env);
        Assert.Contains("cc-usage-monitor", path);
        Assert.EndsWith("cc-usage-monitor", path);
    }

    [Fact]
    public void ConfigPaths_MacOS_UsesApplicationSupport()
    {
        var env = new Dictionary<string, string> { { "HOME", "/Users/test" } };
        var path = ConfigPaths.Resolve(OSPlatform.OSX, env);
        Assert.EndsWith("cc-usage-monitor", path);
    }

    [Fact]
    public void ConfigPaths_Linux_XDGConfigHomeSet_UsesXDG()
    {
        var env = new Dictionary<string, string>
        {
            { "HOME", "/home/test" },
            { "XDG_CONFIG_HOME", "/custom/config" }
        };
        var path = ConfigPaths.Resolve(OSPlatform.Linux, env);
        Assert.EndsWith("cc-usage-monitor", path);
        Assert.Contains("custom", path);
    }

    [Fact]
    public void ConfigPaths_Linux_XDGConfigHomeNotSet_UsesDotConfig()
    {
        var env = new Dictionary<string, string> { { "HOME", "/home/test" } };
        var path = ConfigPaths.Resolve(OSPlatform.Linux, env);
        Assert.EndsWith("cc-usage-monitor", path);
        Assert.Contains(".config", path);
    }

    [Fact]
    public void ConfigPaths_Linux_XDGConfigHomeEmpty_UsesDotConfig()
    {
        var env = new Dictionary<string, string>
        {
            { "HOME", "/home/test" },
            { "XDG_CONFIG_HOME", "" }
        };
        var path = ConfigPaths.Resolve(OSPlatform.Linux, env);
        Assert.EndsWith("cc-usage-monitor", path);
        Assert.Contains(".config", path);
    }

    [Fact]
    public void ConfigPaths_Windows_FallbackUserProfile()
    {
        var env = new Dictionary<string, string> { { "USERPROFILE", "C:\\Users\\test" } };
        var path = ConfigPaths.Resolve(OSPlatform.Windows, env);
        Assert.Contains("cc-usage-monitor", path);
    }
}
