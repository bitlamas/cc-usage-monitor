using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor;

public class App : Application
{
    private TrayController? _trayController;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Build the DI-like container
        var configDir = ResolveConfigDir();
        var executablePath = GetType().Assembly.Location;
        var exeDir = Path.GetDirectoryName(executablePath) ?? ".";
        var exeName = Path.GetFileNameWithoutExtension(executablePath);
        var exeFullPath = Path.Combine(exeDir, exeName + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : ""));

        var configStore = new ConfigStore(configDir);
        var credentialStore = new CredentialStore(); // uses default ProcessRunner
        var usageClient = new UsageClient(credentialStore);
        var clock = new SystemClock();
        var config = configStore.Load();
        var poller = new Poller(usageClient, clock, config.PollIntervalSeconds);
        var notificationSink = new SystemNotificationSink();
        var alertManager = new AlertManager(
            notificationSink,
            clock,
            config.AlertThreshold,
            config.WarnThreshold);
        var autostart = AutostartFactory.Create(exeFullPath);

        _trayController = new TrayController(poller, configStore, autostart, notificationSink, alertManager, configDir);

        // Initialize tray icons and render current snapshot
        _trayController.Initialize();

        // Start the poller
        _ = poller.StartAsync(CancellationToken.None);

        // Tray-only headless app — no main window.
        base.OnFrameworkInitializationCompleted();
    }

    private static string ResolveConfigDir()
    {
        var os = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        if (os.Contains("Windows"))
        {
            var appData = Environment.GetEnvironmentVariable("APPDATA")
                          ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "cc-usage-monitor");
        }
        if (os.Contains("Darwin"))
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            return Path.Combine(home!, "Library", "Application Support", "cc-usage-monitor");
        }
        // Linux
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var homeLinux = Environment.GetEnvironmentVariable("HOME");
        var configHome = !string.IsNullOrEmpty(xdg) ? xdg : Path.Combine(homeLinux!, ".config");
        return Path.Combine(configHome!, "cc-usage-monitor");
    }
}

/// <summary>
/// System notification sink — shows toasts using OS APIs.
/// Per spec §4.6: INotificationSink with platform-specific toast.
/// </summary>
public class SystemNotificationSink : INotificationSink
{
    public bool TryShow(string title, string body)
    {
        // v1.1: real OS toast (PowerShell on Windows, osascript on macOS, etc.)
        // Until then, return false — AlertManager handles an unavailable sink
        // correctly (records as fired, no re-fire, nothing shown).
        return false;
    }
}
