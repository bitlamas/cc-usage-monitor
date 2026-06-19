using System;
using System.Runtime.InteropServices;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Factory that selects the per-OS IAutostart implementation.
/// </summary>
public static class AutostartFactory
{
    /// <summary>Creates the IAutostart implementation for the current OS.</summary>
    /// <param name="executablePath">Full path to the application binary.</param>
    public static IAutostart Create(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
            throw new ArgumentNullException(nameof(executablePath));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsAutostart(executablePath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new MacAutostart(executablePath);

        return new LinuxAutostart(executablePath);
    }
}
