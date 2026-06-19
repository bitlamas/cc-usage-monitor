using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Pure per-OS path resolver for the config directory.
/// Per spec §4.7 — tested independently of disk.
/// </summary>
public static class ConfigPaths
{
    /// <summary>Resolve the config directory path for the given OS and environment.</summary>
    public static string Resolve(OSPlatform os, IDictionary<string, string> env)
    {
        var home = env.TryGetValue("HOME", out var h) ? h :
                   env.TryGetValue("USERPROFILE", out var up) ? up : string.Empty;

        if (os == OSPlatform.Windows)
            return Path.Combine(env.TryGetValue("APPDATA", out var a) ? a : Path.Combine(home, "AppData", "Roaming"), "cc-usage-monitor");

        if (os == OSPlatform.OSX)
            return Path.Combine(home, "Library", "Application Support", "cc-usage-monitor");

        // Linux
        if (env.TryGetValue("XDG_CONFIG_HOME", out var xdg) && !string.IsNullOrEmpty(xdg))
            return Path.Combine(xdg, "cc-usage-monitor");

        return Path.Combine(home, ".config", "cc-usage-monitor");
    }
}
