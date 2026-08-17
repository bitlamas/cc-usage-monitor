using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private readonly ILog _log;
    private readonly IUpdateClient _updateClient;
    private readonly CancellationTokenSource _appCts = new();

    private readonly Dictionary<LimitKind, TrayIcon> _trayIcons = new();
    private readonly Dictionary<LimitKind, NativeMenuItem> _limitMenuItems = new();
    private readonly List<LimitKind> _iconOrder = new();   // current tray registration order

    // Handle-leak bookkeeping (see SetIcon): the WindowIcon currently owned by each tray
    // icon, and the render key it was built from so identical re-renders can be skipped.
    private readonly Dictionary<LimitKind, WindowIcon> _ownedIcons = new();
    private readonly Dictionary<LimitKind, string> _iconKeys = new();

    private NativeMenu? _menu;
    private DetailFlyout? _flyout;
    private AboutWindow? _about;
    private readonly object _lock = new();
    private UsageSnapshot? _latestSnapshot;

    // Limits the user can choose, in default left-to-right display order.
    private static readonly LimitKind[] SelectableKinds =
    {
        LimitKind.Session5h,
        LimitKind.WeeklyAll,
        LimitKind.WeeklyFable,
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
        string configDir,
        ILog? log = null,
        IUpdateClient? updateClient = null)
    {
        _poller = poller;
        _configStore = configStore;
        _autostart = autostart;
        _notificationSink = notificationSink;
        _alertManager = alertManager;
        _configDir = configDir;
        _log = log ?? new NullLog();
        _updateClient = updateClient ?? new GitHubUpdateClient();

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
            try
            {
                RenderSnapshot(snapshot);
                UpdateLimitMenuItems();
            }
            catch (Exception ex)
            {
                _log.Exception(LogLevel.Error, "RenderSnapshot", ex);
            }
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

                // Limit absent from the snapshot — show the dim track.
                var state = snapshot.Limits.TryGetValue(kind, out var s)
                    ? s
                    : new LimitState(null, null, false);

                SetIcon(kind, trayIcon, state, showNumber, warnThreshold, alertThreshold);
                trayIcon.ToolTipText = UsageText.TrayTooltip(kind, state, snapshot.UpdatedAt, now, snapshot.ErrorKind, snapshot.ConsecutiveFailures);
            }
        }
    }

    /// <summary>
    /// Assigns a freshly rendered ring to <paramref name="trayIcon"/>, but only when the
    /// rendered content actually differs from what that icon already shows, and disposes
    /// the icon it replaces.
    ///
    /// Both halves matter. Every WindowIcon assignment makes the Win32 backend build a new
    /// HICON (a USER handle plus GDI bitmaps); nothing reclaims those until the WindowIcon
    /// that owns them is disposed. Re-assigning an identical ring on every poll therefore
    /// burned handles at a fixed rate until the process hit its 10,000-object quota — at
    /// which point CreateIconIndirect starts returning NULL and Avalonia throws a
    /// Win32Exception whose message is the useless "The operation completed successfully"
    /// (NULL without a last-error set). That was a hard crash roughly every 2-4 days of
    /// uptime. Caller must hold _lock.
    /// </summary>
    private void SetIcon(
        LimitKind kind,
        TrayIcon trayIcon,
        LimitState state,
        bool showNumber,
        int warnThreshold,
        int alertThreshold)
    {
        var key = TrayIconKey.For(state, showNumber, warnThreshold, alertThreshold);
        if (_iconKeys.TryGetValue(kind, out var previousKey) && previousKey == key)
            return;   // identical ring already on screen — assigning it again only leaks

        var icon = RenderRing(state, showNumber, warnThreshold, alertThreshold);
        trayIcon.Icon = icon;
        _iconKeys[kind] = key;

        // Release the handles behind the icon we just replaced. Ordering is deliberate:
        // the backend has already copied the new icon by the time we get here, so the old
        // one is no longer referenced by anything on screen.
        if (_ownedIcons.TryGetValue(kind, out var replaced))
            TryDisposeIcon(replaced);
        _ownedIcons[kind] = icon;
    }

    /// <summary>
    /// Reads WindowIcon.PlatformImpl, which is internal to Avalonia. Resolved once and
    /// cached; null if the property ever disappears, in which case icon handles are simply
    /// not reclaimed (see TryDisposeIcon) rather than the app failing.
    /// </summary>
    private static readonly Func<WindowIcon, object?>? PlatformImplAccessor = BuildPlatformImplAccessor();

    private static Func<WindowIcon, object?>? BuildPlatformImplAccessor()
    {
        var getter = typeof(WindowIcon)
            .GetProperty("PlatformImpl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetGetMethod(nonPublic: true);
        if (getter is null)
            return null;
        return icon => getter.Invoke(icon, null);
    }

    private bool _iconDisposalUnavailableLogged;

    /// <summary>
    /// Releases the native icon handle behind a WindowIcon. WindowIcon is not itself
    /// IDisposable — the handle lives in its platform impl (Win32: IconImpl, holding a
    /// Win32Icon/HICON), and nothing in Avalonia ever frees it for us. TrayIconImpl.UpdateIcon
    /// disposes only the transient scaled copy it makes per update, so every WindowIcon we
    /// construct and drop stranded one HICON plus its GDI bitmaps. Must never take the app down.
    /// </summary>
    private void TryDisposeIcon(WindowIcon icon)
    {
        try
        {
            if (PlatformImplAccessor is null)
            {
                if (!_iconDisposalUnavailableLogged)
                {
                    _iconDisposalUnavailableLogged = true;
                    _log.Write(LogLevel.Warn, "icon disposal unavailable: WindowIcon.PlatformImpl not found; icon handles will accumulate");
                }
                return;
            }

            if (PlatformImplAccessor(icon) is IDisposable disposable)
                disposable.Dispose();
        }
        catch (Exception ex)
        {
            _log.Exception(LogLevel.Error, "DisposeIcon", ex);
        }
    }

    private WindowIcon RenderRing(
        LimitState state,
        bool showNumber,
        int warnThreshold,
        int alertThreshold)
    {
        const int sizePx = 32;
        // All three intermediates hold unmanaged memory; WindowIcon copies what it needs
        // during construction, so none of them outlive this method.
        using var skBitmap = RingRenderer.Render(state.Pct, warnThreshold, alertThreshold, showNumber, sizePx);
        using var imageData = skBitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(imageData.ToArray());
        using var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
        return new WindowIcon(bitmap);
    }

    private void BuildMenu(AppConfig config)
    {
        var menu = new NativeMenu();

        // Checkable limit items — fixed selectable set in stable order.
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

        // Check for updates — opens About and reports the result there.
        var updateItem = new NativeMenuItem { Header = "Check for updates…" };
        updateItem.Click += (_, _) => ShowAbout(checkForUpdates: true);
        menu.Items.Add(updateItem);

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

    /// <summary>
    /// Brings the set of live tray icons in line with the selected limits, touching only
    /// what actually changed: kinds that are already showing keep their existing TrayIcon.
    ///
    /// That "touch only what changed" rule is what makes the icons draggable. Windows
    /// remembers a tray icon's position against the pair (executable path, notify-icon
    /// UID), and the Win32 backend hands out UIDs in creation order. Tearing every icon
    /// down and rebuilding them — which is what this method used to do on any change —
    /// minted fresh UIDs each time, so Windows saw unfamiliar icons, dropped them wherever
    /// it liked, and orphaned the positions the user had dragged them to.
    ///
    /// Display order is deliberately NOT managed here. This used to add icons in reverse
    /// with a 1.5 s gap between each to try to force a left-to-right order past Explorer's
    /// batching, which fought the user's own drag-and-drop and left icons stranded in
    /// separate batches. Ordering is the shell's job and the user's choice; we just add
    /// them and let Windows place them.
    /// </summary>
    private void SyncIcons(AppConfig config)
    {
        lock (_lock)
        {
            // Selected limits, filtered to the selectable kinds (drops any unknown kind).
            var desired = config.SelectedLimits
                .Where(SelectableKinds.Contains)
                .Distinct()
                .ToList();
            if (desired.Count == 0)
                desired = new List<LimitKind>(DefaultLimits);

            if (desired.SequenceEqual(_iconOrder))
                return;   // already showing exactly this set

            var icons = TrayIcon.GetIcons(Application.Current!);

            // Retire only the kinds that are no longer selected.
            foreach (var kind in _iconOrder.Where(k => !desired.Contains(k)).ToList())
                RemoveIcon(icons, kind);

            // Add only the kinds that aren't showing yet, in config order.
            foreach (var kind in desired.Where(k => !_trayIcons.ContainsKey(k)))
                AddIcon(icons, kind, config);

            _iconOrder.Clear();
            _iconOrder.AddRange(desired);
        }
    }

    /// <summary>Creates one tray icon and shows it. Caller must hold _lock.</summary>
    private void AddIcon(IList<TrayIcon>? icons, LimitKind kind, AppConfig config)
    {
        try
        {
            // Created hidden so the ring bitmap is in place before NIM_ADD fires: Windows
            // captures the icon for the Taskbar Settings list at add time and does not
            // reliably refresh that capture later, so an icon added blank stays a blank
            // square in Settings forever. The tooltip is seeded with the limit's name for
            // the same reason — it is what Settings records as the entry's label.
            var trayIcon = new TrayIcon
            {
                ToolTipText = GetLimitLabel(kind),
                IsVisible = false
            };
            trayIcon.Clicked += OnTrayIconClicked;
            if (_menu != null)
                trayIcon.Menu = _menu;   // BuildMenu covers icons that already exist; this covers later ones

            _trayIcons[kind] = trayIcon;

            // Seed with the latest known state, or the dim track if we haven't polled yet.
            var state = _latestSnapshot is not null && _latestSnapshot.Limits.TryGetValue(kind, out var s)
                ? s
                : new LimitState(null, null, false);
            SetIcon(kind, trayIcon, state, config.ShowNumberInRing, config.WarnThreshold, config.AlertThreshold);

            icons?.Add(trayIcon);
            trayIcon.IsVisible = true;
        }
        catch (Exception ex)
        {
            _log.Exception(LogLevel.Error, "AddIcon", ex);
        }
    }

    /// <summary>Removes one tray icon and releases its handles. Caller must hold _lock.</summary>
    private void RemoveIcon(IList<TrayIcon>? icons, LimitKind kind)
    {
        try
        {
            if (_trayIcons.TryGetValue(kind, out var ti))
            {
                icons?.Remove(ti);
                ti.Dispose();
                _trayIcons.Remove(kind);
            }
            if (_ownedIcons.TryGetValue(kind, out var owned))
            {
                TryDisposeIcon(owned);
                _ownedIcons.Remove(kind);
            }
            _iconKeys.Remove(kind);
        }
        catch (Exception ex)
        {
            _log.Exception(LogLevel.Error, "RemoveIcon", ex);
        }
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            _log.Exception(LogLevel.Error, "Flyout", ex);
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
            // the right. Any non-selectable kinds are kept, trailing, so toggling never
            // silently drops them from config.
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

    private void ShowAbout(bool checkForUpdates = false)
    {
        try
        {
            if (_about != null && _about.IsVisible)
            {
                _about.Activate();
                // Re-checking on an already-open window is what the user asked for.
                if (checkForUpdates)
                    _about.BeginUpdateCheck();
                return;
            }

            var about = new AboutWindow(_updateClient, _log);
            about.Closed += (_, _) => { lock (_lock) { _about = null; } };
            _about = about;
            about.Show();

            if (checkForUpdates)
                about.BeginUpdateCheck();
        }
        catch (Exception ex)
        {
            _log.Exception(LogLevel.Error, "ShowAbout", ex);
        }
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
        // Called directly, not by reflection. The old code did GetMethod("Shutdown") then
        // Invoke(lifetime, null), but the method is Shutdown(int exitCode = 0) — reflection
        // does not apply optional-argument defaults, so passing no arguments threw
        // TargetParameterCountException and every use of the Quit menu item crashed the app
        // instead of shutting it down cleanly.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static string GetLimitLabel(LimitKind kind) => kind switch
    {
        LimitKind.Session5h => "5-hour session",
        LimitKind.WeeklyAll => "Weekly (all models)",
        LimitKind.WeeklyFable => "Weekly (Fable)",
        _ => kind.ToString()
    };

    public void Dispose()
    {
        _appCts.Dispose();
        lock (_lock)
        {
            var icons = TrayIcon.GetIcons(Application.Current!);
            foreach (var kind in _trayIcons.Keys.ToList())
                RemoveIcon(icons, kind);
            _trayIcons.Clear();
            _ownedIcons.Clear();
            _iconKeys.Clear();
        }
    }
}
