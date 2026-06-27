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
        // Environment.ProcessPath is the actual running executable — correct for a
        // single-file publish (where Assembly.Location is empty) and for normal builds.
        var exeFullPath = Environment.ProcessPath!;

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

        // Start the poller FIRST so a usage snapshot is in flight before the tray icons
        // are built. Initialize()'s staggered add waits briefly for that first snapshot
        // so each icon is added carrying a real ring (see TrayController add-time note).
        _ = poller.StartAsync(CancellationToken.None);

        // Initialize tray icons and render current snapshot
        _trayController.Initialize();

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
