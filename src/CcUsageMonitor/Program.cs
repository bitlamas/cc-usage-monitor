using Avalonia;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CcUsageMonitor;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Resolve config dir once via the canonical resolver (same code path App uses).
        OSPlatform osPlatform;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            osPlatform = OSPlatform.Windows;
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            osPlatform = OSPlatform.OSX;
        else
            osPlatform = OSPlatform.Linux;

        var envDict = new Dictionary<string, string>();
        foreach (DictionaryEntry de in Environment.GetEnvironmentVariables())
            envDict[de.Key!.ToString()!] = de.Value!.ToString()!;
        var configDir = Core.Services.ConfigPaths.Resolve(osPlatform, envDict);
        Diagnostics.ConfigDir = configDir;

        // Build the log sink before Avalonia starts.
        var log = new Core.Services.FileLog(
            Core.Services.LogPaths.LogFile(configDir),
            Core.Services.LogPaths.RolledFile(configDir),
            5_000_000,
            new Core.Services.SystemClock());
        Diagnostics.Log = log;

        // Startup line.
        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0";
        log.Write(Core.Services.LogLevel.Info, $"startup v{version} pid={Environment.ProcessId}");

        // Install global exception handlers.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Diagnostics.Log.Exception(Core.Services.LogLevel.Fatal, "AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Diagnostics.Log.Exception(Core.Services.LogLevel.Error, "UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
