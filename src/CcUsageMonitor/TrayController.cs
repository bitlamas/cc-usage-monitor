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
    private readonly List<LimitKind> _iconOrder = new();   // current tray registration order
    private CancellationTokenSource? _addCts;              // cancels an in-flight staggered add
    private NativeMenu? _menu;
    private DetailFlyout? _flyout;
    private AboutWindow? _about;
    private readonly object _lock = new();
    private UsageSnapshot? _latestSnapshot;

    // Limits the user can choose in v1, in default left-to-right display order.
    // WeeklyOpus is intentionally omitted — the API doesn't expose it yet, so it
    // stays hidden until a future version (Core still accepts it; we filter here).
    private static readonly LimitKind[] SelectableKinds =
    {
        LimitKind.Session5h,
        LimitKind.WeeklyAll,
        LimitKind.WeeklySonnet,
    };

    private static readonly List<LimitKind> DefaultLimits = new()
    {
        LimitKind.Session5h,
        LimitKind.WeeklyAll,
    };

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
        ReconcileAutostart(config.StartAtLogin);
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

        // Checkable limit items — fixed v1 set in stable order (WeeklyOpus hidden).
        _limitMenuItems.Clear();
        foreach (var kind in SelectableKinds)
        {
            var item = new NativeMenuItem
            {
                Header = GetLimitLabel(kind),
                IsChecked = config.SelectedLimits.Contains(kind),
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsEnabled = true // updated per-snapshot
            };
            item.Click += (_, _) => ToggleLimit(kind);
            menu.Items.Add(item);
            _limitMenuItems[kind] = item;
        }

        menu.Items.Add(new NativeMenuItemSeparator());

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

        // About
        var aboutItem = new NativeMenuItem { Header = "About" };
        aboutItem.Click += (_, _) => ShowAbout();
        menu.Items.Add(aboutItem);

        // Quit
        var quitItem = new NativeMenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => Quit();
        menu.Items.Add(quitItem);

        // Assign menu to all tray icons (and remember it for icon rebuilds).
        _menu = menu;
        lock (_lock)
        {
            foreach (var ti in _trayIcons.Values)
                ti.Menu = menu;
        }
    }

    private void SyncIcons(AppConfig config)
    {
        List<LimitKind> addOrder;
        lock (_lock)
        {
            // Desired left-to-right display order = config.SelectedLimits order,
            // filtered to the v1-selectable kinds (drops a stray WeeklyOpus etc.).
            var desired = config.SelectedLimits
                .Where(SelectableKinds.Contains)
                .Distinct()
                .ToList();
            if (desired.Count == 0)
                desired = new List<LimitKind>(DefaultLimits);

            // Already showing exactly this set in this order → nothing to do.
            if (desired.SequenceEqual(_iconOrder))
                return;

            // Abandon any staggered add still in flight from a previous call.
            _addCts?.Cancel();

            var icons = TrayIcon.GetIcons(Application.Current!);
            foreach (var kind in _iconOrder)
            {
                if (_trayIcons.TryGetValue(kind, out var ti))
                {
                    icons?.Remove(ti);
                    ti.Dispose();
                }
            }
            _trayIcons.Clear();
            _iconOrder.Clear();
            _iconOrder.AddRange(desired);

            // Windows 11 appends each newly-added tray icon to the RIGHT of the existing
            // ones and persists its position per icon. Icons added in the same instant get
            // an arbitrary (sticky) order, so we add in `desired` order WITH a delay
            // between each: desired[0] (the leftmost we want, e.g. 5-hour) is added first
            // → oldest → leftmost; later limits land to its right. Windows then persists
            // this position per-exe in HKCU\Control Panel\NotifyIconSettings across runs.
            // (No API forces position; a user's manual drag still wins and is preserved.)
            addOrder = new List<LimitKind>(desired);
        }

        _addCts = new CancellationTokenSource();
        _ = AddIconsStaggeredAsync(addOrder, _addCts.Token);
    }

    /// <summary>
    /// Adds the tray icons one at a time with a delay between each. On Windows 11 the gap
    /// makes add-TIME the deciding factor for position; combined with adding in `desired`
    /// order, the earliest-added limit sits leftmost (see SyncIcons). Without the gap,
    /// same-instant adds get an arbitrary, sticky order.
    /// Fire-and-forget from SyncIcons; runs on the UI thread (Avalonia sync context).
    /// </summary>
    private async Task AddIconsStaggeredAsync(List<LimitKind> addOrder, CancellationToken ct)
    {
        const int gapMs = 350;   // long enough for Windows to record distinct add-times
        var icons = TrayIcon.GetIcons(Application.Current!);

        try
        {
            for (int i = 0; i < addOrder.Count; i++)
            {
                if (ct.IsCancellationRequested)
                    return;

                if (i > 0)
                    await Task.Delay(gapMs, ct);

                var kind = addOrder[i];
                var trayIcon = new TrayIcon
                {
                    ToolTipText = "Loading…",
                    IsVisible = true
                };
                trayIcon.Clicked += OnTrayIconClicked;

                lock (_lock)
                {
                    if (ct.IsCancellationRequested)
                    {
                        trayIcon.Dispose();
                        return;
                    }
                    if (_menu != null)
                        trayIcon.Menu = _menu;   // BuildMenu assigns to already-added icons; this covers later ones
                    _trayIcons[kind] = trayIcon;
                }

                icons?.Add(trayIcon);

                // Paint immediately from the latest snapshot so it isn't blank mid-stagger.
                var snap = _latestSnapshot;
                if (snap != null)
                    RenderSnapshot(snap);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer SyncIcons — nothing to do.
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
            _flyout = new DetailFlyout(_poller, _configStore) { Anchor = GetCursorPosition() };
            _flyout.Closed += (_, _) => { lock (_lock) { _flyout = null; } };
            _flyout.Show();
        }
    }

    private static PixelPoint? GetCursorPosition()
    {
        if (OperatingSystem.IsWindows() && GetCursorPos(out var p))
            return new PixelPoint(p.X, p.Y);
        return null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

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
            // Re-order to the canonical left-to-right sequence (SelectableKinds) so a
            // re-enabled limit returns to its proper slot instead of being appended on
            // the right. Any non-selectable kinds (e.g. a stray WeeklyOpus) are kept,
            // trailing, so toggling never silently drops them from config.
            selected = SelectableKinds.Where(selected.Contains)
                .Concat(selected.Where(k => !SelectableKinds.Contains(k)))
                .ToList();
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
        if (_poller.Current != null)
            RenderSnapshot(_poller.Current);   // freshly (re)built icons start blank
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

    private void ReconcileAutostart(bool desired)
    {
        // Make the real OS autostart entry match the saved preference so the menu
        // checkbox never lies (first-run default, or a manually removed shortcut).
        try
        {
            if (desired && !_autostart.IsEnabled())
                _autostart.Enable();
            else if (!desired && _autostart.IsEnabled())
                _autostart.Disable();
        }
        catch
        {
            // Non-fatal — autostart is best-effort.
        }
    }

    private void ShowAbout()
    {
        if (_about != null && _about.IsVisible)
        {
            _about.Activate();
            return;
        }
        _about = new AboutWindow();
        _about.Closed += (_, _) => { lock (_lock) { _about = null; } };
        _about.Show();
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
