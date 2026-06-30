using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor;

/// <summary>Data item for a single limit row in the flyout.</summary>
public class FlyoutItem
{
    public string Label { get; set; } = string.Empty;
    public string PctText { get; set; } = string.Empty;
    public double BarFillWidth { get; set; }
    public IBrush BandBrush { get; set; } = Brushes.Gray;
    public string ResetText { get; set; } = string.Empty;
}

/// <summary>
/// Detail flyout — a dark popover listing all selected limits with a usage bar,
/// percentage, and reset info. Per spec §4.9: ≤320×400, dismisses on focus loss.
/// </summary>
public partial class DetailFlyout : Window
{
    // Width of the usage-bar track: window 300 − 2×16 padding − 2×1 border ≈ 266px.
    private const double BarTrackWidth = 266;

    private readonly Poller _poller;
    private readonly IConfigStore _configStore;
    private readonly Action<UsageSnapshot> _snapshotHandler;

    /// <summary>Screen-pixel point (the cursor at click time) to anchor the flyout near.</summary>
    public PixelPoint? Anchor { get; set; }

    // Parameterless ctor used only by the Avalonia XAML loader (silences AVLN3001).
    #pragma warning disable CS8618
    public DetailFlyout() { }
    #pragma warning restore CS8618

    public DetailFlyout(Poller poller, IConfigStore configStore)
    {
        _poller = poller;
        _configStore = configStore;

        InitializeComponent();

        // Same delegate instance for subscribe/unsubscribe.
        _snapshotHandler = _ => Dispatcher.UIThread.Post(() => { if (_poller.Current != null) UpdateDisplay(_poller.Current); });

        // Dismiss on focus loss.
        this.Deactivated += (_, _) => Close();

        _poller.SnapshotPublished += _snapshotHandler;

        if (_poller.Current != null)
            UpdateDisplay(_poller.Current);
    }

    private void UpdateDisplay(UsageSnapshot snapshot)
    {
        var config = _configStore.Load();
        var now = DateTimeOffset.Now;

        var items = new List<FlyoutItem>();
        foreach (var kind in config.SelectedLimits)
        {
            var state = snapshot.Limits.TryGetValue(kind, out var s)
                ? s
                : new LimitState(null, null, false);

            var band = Bands.Select(state.Pct, config.WarnThreshold, config.AlertThreshold);
            var clamped = Math.Max(0, Math.Min(100, state.Pct ?? 0));

            items.Add(new FlyoutItem
            {
                Label = GetLimitLabel(kind),
                PctText = state.Pct.HasValue ? $"{state.Pct}%" : "--%",
                BarFillWidth = clamped / 100.0 * BarTrackWidth,
                BandBrush = BandBrush(band),
                ResetText = UsageText.ResetPhrase(state.ResetsAt, now) ?? "—", // em dash when no reset
            });
        }

        LimitsList!.ItemsSource = items;
        UpdatedText!.Text = $"updated {snapshot.UpdatedAt.LocalDateTime:HH:mm}";

        var line = UsageText.FlyoutErrorLine(snapshot.ErrorKind);
        StaleText!.IsVisible = line is not null;
        StaleText!.Text = line ?? string.Empty;
    }

    private static IBrush BandBrush(BandType band) => band switch
    {
        BandType.Green => new SolidColorBrush(Color.Parse("#4CAF50")),
        BandType.Yellow => new SolidColorBrush(Color.Parse("#FFC107")),
        BandType.Red => new SolidColorBrush(Color.Parse("#F44336")),
        _ => new SolidColorBrush(Color.Parse("#6B6862")),
    };

    private static string GetLimitLabel(LimitKind kind) => kind switch
    {
        LimitKind.Session5h => "5-hour session",
        LimitKind.WeeklyAll => "Weekly (all models)",
        LimitKind.WeeklySonnet => "Weekly (Sonnet)",
        LimitKind.WeeklyOpus => "Weekly (Opus)",
        _ => kind.ToString()
    };

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Anchor is not PixelPoint anchor)
            return;

        var scale = RenderScaling;
        int w = (int)Math.Ceiling(ClientSize.Width * scale);
        int h = (int)Math.Ceiling(ClientSize.Height * scale);

        var screen = Screens.ScreenFromPoint(anchor) ?? Screens.Primary;
        var wa = screen is { } s ? s.WorkingArea : new PixelRect(anchor.X - w, anchor.Y - h, w, h);
        int left = wa.X, top = wa.Y, right = wa.X + wa.Width, bottom = wa.Y + wa.Height;

        // Above-left of the cursor (clears a bottom/edge taskbar), clamped on-screen.
        int x = Math.Clamp(anchor.X - w, left, Math.Max(left, right - w));
        int y = Math.Clamp(anchor.Y - h, top, Math.Max(top, bottom - h));
        Position = new PixelPoint(x, y);
    }

    protected override void OnClosed(EventArgs e)
    {
        _poller.SnapshotPublished -= _snapshotHandler;
        base.OnClosed(e);
    }
}
