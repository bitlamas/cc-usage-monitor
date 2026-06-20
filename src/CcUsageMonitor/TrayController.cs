using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;
using SkiaSharp;

namespace CcUsageMonitor;

/// <summary>
/// Manages system-tray icons, context menus, and the detail flyout.
/// Per spec §4.5: one TrayIcon per selected, present LimitKind.
/// </summary>
public class TrayController : IDisposable
{
    private readonly Poller _poller;
    private readonly IConfigStore _configStore;
    private readonly IAutostart _autostart;
    private readonly INotificationSink _notificationSink;
    private readonly AlertManager _alertManager;
    private readonly string _configDir;
    private readonly CancellationTokenSource _appCts = new();

    private readonly Dictionary<LimitKind, TrayIcon> _trayIcons = new();
    private readonly Dictionary<LimitKind, NativeMenuItem> _limitMenuItems = new();
    private DetailFlyout? _flyout;
    private readonly object _lock = new();
    private UsageSnapshot? _latestSnapshot;

    public TrayController(
        Poller poller,
        IConfigStore configStore,
        IAutostart autostart,
        INotificationSink notificationSink,
        AlertManager alertManager,
        string configDir)
    {
        _poller = poller;
        _configStore = configStore;
        _autostart = autostart;
        _notificationSink = notificationSink;
        _alertManager = alertManager;
        _configDir = configDir;

        // Wire AlertManager as subscriber BEFORE starting poller
        _poller.SnapshotPublished += _alertManager.OnSnapshot;

        // Wire this controller as subscriber
        _poller.SnapshotPublished += OnSnapshot;
    }

    /// <summary>
    /// Creates and shows the tray icons, then renders the current snapshot immediately.
    /// </summary>
    public void Initialize()
    {
        var config = _configStore.Load();
        SyncIcons(config);   // create the icons first...
        BuildMenu(config);   // ...so BuildMenu can assign the menu to them
        _autostart.IsEnabled(); // warm up
        // Render current snapshot if available
        if (_poller.Current != null)
            RenderSnapshot(_poller.Current);
    }

    /// <summary>
    /// Called on each published snapshot.
    /// </summary>
    private void OnSnapshot(UsageSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        // Poller fires on a background thread; marshal all tray UI updates to the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            RenderSnapshot(snapshot);
            UpdateLimitMenuItems();
        });
    }

    private void RenderSnapshot(UsageSnapshot snapshot)
    {
        var config = _configStore.Load();
        var showNumber = config.ShowNumberInRing;
        var warnThreshold = config.WarnThreshold;
        var alertThreshold = config.AlertThreshold;
        var now = DateTimeOffset.Now;

        lock (_lock)
        {
            foreach (var kvp in _trayIcons)
            {
                var kind = kvp.Key;
                var trayIcon = kvp.Value;

                if (snapshot.Limits.TryGetValue(kind, out var state))
                {
                    var icon = RenderRing(state, showNumber, warnThreshold, alertThreshold);
                    trayIcon.Icon = icon;

                    var tooltip = UsageText.Tooltip(kind, state, snapshot.UpdatedAt, now);
                    if (snapshot.Stale)
                        tooltip = BuildStaleTooltip(kind, state, snapshot.UpdatedAt, now, snapshot.Error);
                    trayIcon.ToolTipText = tooltip;
                }
                else
                {
                    // Limit not present — show dim track
                    var dimState = new LimitState(null, null, false);
                    var icon = RenderRing(dimState, showNumber, warnThreshold, alertThreshold);
                    trayIcon.Icon = icon;
                    trayIcon.ToolTipText = UsageText.Tooltip(kind, dimState, snapshot.UpdatedAt, now);
                }
            }
        }
    }

    private WindowIcon RenderRing(
        LimitState state,
        bool showNumber,
        int warnThreshold,
        int alertThreshold)
    {
        const int sizePx = 32;
        var skBitmap = RingRenderer.Render(state.Pct, warnThreshold, alertThreshold, showNumber, sizePx);

        // Save SKBitmap to PNG in memory, then load as Avalonia Bitmap
        using var imageData = skBitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(imageData.ToArray());
        var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
        return new WindowIcon(bitmap);
    }

    private void BuildMenu(AppConfig config)
    {
        var menu = new NativeMenu();

        // LimitKind checkable items — in config.SelectedLimits order first, then remaining
        var selected = config.SelectedLimits.Distinct().ToList();
        var allKinds = Enum.GetValues(typeof(LimitKind)).Cast<LimitKind>().ToList();
        var remaining = allKinds.Except(selected).ToList();

        foreach (var kind in selected.Concat(remaining))
        {
            var item = new NativeMenuItem
            {
                Header = GetLimitLabel(kind),
                IsChecked = config.SelectedLimits.Contains(kind),
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsEnabled = true // will be updated per-snapshot
            };
            item.Click += (_, _) => ToggleLimit(kind);
            menu.Items.Add(item);
            _limitMenuItems[kind] = item;
        }

        menu.Items.Add(new NativeMenuItemSeparator());

        // Show % inside ring
        var showNumberItem = new NativeMenuItem
        {
            Header = "Show % inside ring",
            IsChecked = config.ShowNumberInRing,
            ToggleType = NativeMenuItemToggleType.CheckBox
        };
        showNumberItem.Click += (_, _) => ToggleShowNumber();
        menu.Items.Add(showNumberItem);

        // Alerts
        var alertsItem = new NativeMenuItem
        {
            Header = "Alerts",
            IsChecked = config.AlertsEnabled,
            ToggleType = NativeMenuItemToggleType.CheckBox
        };
        alertsItem.Click += (_, _) => ToggleAlerts();
        menu.Items.Add(alertsItem);

        // Start at login
        var startItem = new NativeMenuItem
        {
            Header = "Start at login",
            IsChecked = config.StartAtLogin,
            ToggleType = NativeMenuItemToggleType.CheckBox
        };
        startItem.Click += (_, _) => ToggleStartAtLogin();
        menu.Items.Add(startItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Refresh now
        var refreshItem = new NativeMenuItem { Header = "Refresh now" };
        refreshItem.Click += (_, _) => _ = Task.Run(() => _poller.ForceRefreshAsync());
        menu.Items.Add(refreshItem);

        // Open config file
        var openConfigItem = new NativeMenuItem { Header = "Open config file" };
        openConfigItem.Click += (_, _) => OpenConfigFile();
        menu.Items.Add(openConfigItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Quit
        var quitItem = new NativeMenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => Quit();
        menu.Items.Add(quitItem);

        // Assign menu to all tray icons
        lock (_lock)
        {
            foreach (var ti in _trayIcons.Values)
                ti.Menu = menu;
        }
    }

    private void SyncIcons(AppConfig config)
    {
        // Create icons for selected kinds, remove extras
        // Icons are registered in enum order (Session5h, WeeklyAll, WeeklySonnet, WeeklyOpus)
        // so 5-hour appears leftmost in the system tray on Windows.
        lock (_lock)
        {
            // Add missing icons — iterate in fixed priority order
            var priorityOrder = Enum.GetValues(typeof(LimitKind)).Cast<LimitKind>();
            foreach (var kind in priorityOrder)
            {
                if (config.SelectedLimits.Contains(kind) && !_trayIcons.ContainsKey(kind))
                {
                    var trayIcon = new TrayIcon
                    {
                        ToolTipText = "Loading…",
                        IsVisible = true
                    };
                    trayIcon.Clicked += OnTrayIconClicked;
                    _trayIcons[kind] = trayIcon;

                    var icons = TrayIcon.GetIcons(Application.Current!);
                    icons?.Add(trayIcon);
                }
            }

            // Remove icons for deselected kinds
            var toRemove = _trayIcons.Keys.Except(config.SelectedLimits).ToList();
            foreach (var kind in toRemove)
            {
                if (_trayIcons.TryGetValue(kind, out var ti))
                {
                    TrayIcon.GetIcons(Application.Current!)?.Remove(ti);
                    ti.Dispose();
                    _trayIcons.Remove(kind);
                }
            }
        }
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        // Left-click → toggle detail flyout
        if (_flyout != null && _flyout.IsVisible)
        {
            _flyout.Close();
        }
        else
        {
            _flyout = new DetailFlyout(_poller, _configStore);
            _flyout.Closed += (_, _) => { lock (_lock) { _flyout = null; } };
            _flyout.Show();
        }
    }

    private void ToggleLimit(LimitKind kind)
    {
        var config = _configStore.Load();
        var selected = new List<LimitKind>(config.SelectedLimits);
        var count = selected.Count;

        if (selected.Contains(kind))
        {
            // Min-one rule: don't remove the last one
            if (count <= 1)
                return;
            selected.Remove(kind);
        }
        else
        {
            selected.Add(kind);
        }

        var newConfig = new AppConfig(
            selected,
            config.ShowNumberInRing,
            config.AlertsEnabled,
            config.AlertThreshold,
            config.WarnThreshold,
            config.PollIntervalSeconds,
            config.StartAtLogin,
            config.Colors);

        _configStore.Save(newConfig);
        SyncIcons(newConfig);
        UpdateLimitMenuItems();
    }

    /// <summary>
    /// Disables checkable menu items for: (a) the sole remaining selected limit (min-one rule),
    /// and (b) limits whose latest snapshot shows Present = false (not yet returned by the API).
    /// Per spec §5: a limit with Present = false has its context-menu item disabled (greyed).
    /// Per spec §6.4: when exactly one limit is selected, that limit cannot be unchecked.
    /// </summary>
    private void UpdateLimitMenuItems()
    {
        var config = _configStore.Load();
        var count = config.SelectedLimits.Count;
        var presentSnapshot = _latestSnapshot;

        foreach (var kvp in _limitMenuItems)
        {
            var kind = kvp.Key;
            var item = kvp.Value;

            // Rule (a): Present = false → disabled (not yet returned by the API)
            bool isPresent = presentSnapshot?.Limits.TryGetValue(kind, out var state) == true
                             && state.Present;
            if (!isPresent)
            {
                item.IsEnabled = false;
                continue;
            }

            // Rule (b): min-one rule — sole remaining selected limit cannot be unchecked
            if (count <= 1 && config.SelectedLimits.Contains(kind))
            {
                item.IsEnabled = false;
            }
            else
            {
                item.IsEnabled = true;
            }
        }
    }

    private void ToggleShowNumber()
    {
        var config = _configStore.Load();
        var newConfig = new AppConfig(
            config.SelectedLimits,
            !config.ShowNumberInRing,
            config.AlertsEnabled,
            config.AlertThreshold,
            config.WarnThreshold,
            config.PollIntervalSeconds,
            config.StartAtLogin,
            config.Colors);
        _configStore.Save(newConfig);
    }

    private void ToggleAlerts()
    {
        var config = _configStore.Load();
        var newConfig = new AppConfig(
            config.SelectedLimits,
            config.ShowNumberInRing,
            !config.AlertsEnabled,
            config.AlertThreshold,
            config.WarnThreshold,
            config.PollIntervalSeconds,
            config.StartAtLogin,
            config.Colors);
        _configStore.Save(newConfig);
    }

    private void ToggleStartAtLogin()
    {
        var config = _configStore.Load();
        var newStart = !config.StartAtLogin;

        if (newStart)
            _autostart.Enable();
        else
            _autostart.Disable();

        var newConfig = new AppConfig(
            config.SelectedLimits,
            config.ShowNumberInRing,
            config.AlertsEnabled,
            config.AlertThreshold,
            config.WarnThreshold,
            config.PollIntervalSeconds,
            newStart,
            config.Colors);
        _configStore.Save(newConfig);
    }

    private void OpenConfigFile()
    {
        try
        {
            var configPath = Path.Combine(_configDir, "config.json");
            if (!File.Exists(configPath))
                return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = configPath,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"\"{configPath}\"");
            }
            else
            {
                Process.Start("xdg-open", $"\"{configPath}\"");
            }
        }
        catch
        {
            // Fail silently per spec §6.4
        }
    }

    private void Quit()
    {
        _appCts.Cancel();
        // Use dynamic to call Shutdown() on the desktop lifetime
        if (Application.Current?.ApplicationLifetime is IApplicationLifetime lifetime)
        {
            var shutdownMethod = lifetime.GetType().GetMethod("Shutdown");
            shutdownMethod?.Invoke(lifetime, null);
        }
    }

    private static string GetLimitLabel(LimitKind kind) => kind switch
    {
        LimitKind.Session5h => "5-hour session",
        LimitKind.WeeklyAll => "Weekly (all models)",
        LimitKind.WeeklySonnet => "Weekly (Sonnet)",
        LimitKind.WeeklyOpus => "Weekly (Opus)",
        _ => kind.ToString()
    };

    private string BuildStaleTooltip(LimitKind kind, LimitState state, DateTimeOffset updatedAt, DateTimeOffset now, string? error)
    {
        var baseTooltip = UsageText.Tooltip(kind, state, updatedAt, now);
        var reason = string.IsNullOrEmpty(error) ? "data may be stale" : error;
        return $"{baseTooltip} — stale ({reason})";
    }

    public void Dispose()
    {
        _appCts.Dispose();
        lock (_lock)
        {
            foreach (var kvp in _trayIcons)
            {
                TrayIcon.GetIcons(Application.Current!)?.Remove(kvp.Value);
                kvp.Value.Dispose();
            }
            _trayIcons.Clear();
        }
    }
}
