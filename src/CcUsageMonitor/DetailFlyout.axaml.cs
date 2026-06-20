using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor;

/// <summary>Data item for a single limit row in the flyout.</summary>
public class FlyoutItem
{
    public string Label { get; set; } = string.Empty;
    public string PctDisplay { get; set; } = string.Empty;
    public string ResetDisplay { get; set; } = string.Empty;
    public string UpdatedDisplay { get; set; } = string.Empty;
}

/// <summary>
/// Detail flyout — borderless popup listing all selected limits with % display,
/// reset countdown, and last-updated time.
/// Per spec §4.9: ≤320×400, vertical stack, dismisses on focus loss.
/// </summary>
public partial class DetailFlyout : Window
{
    private readonly Poller _poller;
    private readonly IConfigStore _configStore;
    private readonly Action<UsageSnapshot> _snapshotHandler;

    // Parameterless ctor used only by the Avalonia XAML loader.
    // This silences AVLN3001 ("no public constructor").
    // The _poller/_configStore/_snapshotHandler fields are assigned by the DI ctor;
    // the parameterless ctor is a no-op placeholder for the XAML loader.
    #pragma warning disable CS8618
    public DetailFlyout() { }
    #pragma warning restore CS8618

    public DetailFlyout(Poller poller, IConfigStore configStore)
    {
        _poller = poller;
        _configStore = configStore;

        InitializeComponent();

        // Store the snapshot handler so we can subscribe/unsubscribe the same delegate
        _snapshotHandler = _ => Dispatcher.UIThread.Post(() => { if (_poller.Current != null) UpdateDisplay(_poller.Current); });

        // Dismiss on focus loss — Deactivated fires when the window loses activation.
        this.Deactivated += (_, _) => Close();

        // Subscribe to poller snapshots for live updates
        _poller.SnapshotPublished += _snapshotHandler;

        // Initial display
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
            if (snapshot.Limits.TryGetValue(kind, out var state))
            {
                var label = GetLimitLabel(kind);
                var pctDisplay = state.Pct.HasValue ? $"{state.Pct}%" : "--%";
                var resetDisplay = UsageText.Countdown(state.ResetsAt, now);
                var updatedDisplay = $"Updated {snapshot.UpdatedAt.LocalDateTime:HH:mm}";

                items.Add(new FlyoutItem
                {
                    Label = label,
                    PctDisplay = pctDisplay,
                    ResetDisplay = resetDisplay!,
                    UpdatedDisplay = updatedDisplay
                });
            }
            else
            {
                var label = GetLimitLabel(kind);
                items.Add(new FlyoutItem
                {
                    Label = label,
                    PctDisplay = "--%",
                    ResetDisplay = "Not available",
                    UpdatedDisplay = $"Updated {snapshot.UpdatedAt.LocalDateTime:HH:mm}"
                });
            }
        }

        // Bind to ItemsControl
        LimitsList!.ItemsSource = items;

        // Show stale indicator
        StaleText!.IsVisible = snapshot.Stale;
        if (snapshot.Stale)
        {
            var reason = string.IsNullOrEmpty(snapshot.Error) ? "Data may be stale" : $"Stale: {snapshot.Error}";
            StaleText!.Text = reason!;
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

    protected override void OnClosed(EventArgs e)
    {
        _poller.SnapshotPublished -= _snapshotHandler;
        base.OnClosed(e);
    }
}
