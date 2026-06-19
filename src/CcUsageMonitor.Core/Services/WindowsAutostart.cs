using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Windows autostart via Startup-folder shortcut.
/// Per spec §4.8 — chosen over HKCU\...\Run so it survives binary relocation.
/// </summary>
public class WindowsAutostart : IAutostart
{
    private readonly string _shortcutPath;
    private readonly string _executablePath;

    /// <param name="executablePath">Full path to the application binary.</param>
    /// <param name="startupFolder">Startup folder path (or null for CSIDL_STARTUP auto-resolve).</param>
    public WindowsAutostart(string executablePath, string? startupFolder = null)
    {
        _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
        var startup = startupFolder ?? GetStartupFolder();
        Directory.CreateDirectory(startup);
        _shortcutPath = Path.Combine(startup, "cc-usage-monitor.lnk");
    }

    public bool IsEnabled()
        => File.Exists(_shortcutPath);

    public void Enable()
    {
        CreateShortcut(_shortcutPath, _executablePath);
    }

    public void Disable()
    {
        if (File.Exists(_shortcutPath))
            File.Delete(_shortcutPath);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1416:Validate platform compatibility", Justification = "Windows-only type")]
    private static string GetStartupFolder()
        => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1416:Validate platform compatibility", Justification = "Windows-only type")]
    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        // Use WScript.Shell COM object (built-in Windows component)
        Type? wshType = Type.GetTypeFromProgID("WScript.Shell");
        var wshShell = wshType != null ? Activator.CreateInstance(wshType) : null;
        if (wshShell == null)
            throw new InvalidOperationException("WScript.Shell COM object not available.");

        var shortcut = wshShell.GetType().InvokeMember(
            "CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, wshShell,
            new object[] { shortcutPath })!
            ?? throw new InvalidOperationException("CreateShortcut returned null.");

        shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
        shortcut.GetType().InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "cc-usage-monitor" });
        shortcut.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);

        Marshal.FinalReleaseComObject(wshShell);
    }
}
