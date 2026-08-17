using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor;

/// <summary>
/// Small "About" popover — app name, version, and a clickable GitHub link.
/// Borderless dark window matching the detail flyout; dismisses on focus loss.
/// Also hosts the result of the "Check for updates" menu item.
/// </summary>
public partial class AboutWindow : Window
{
    private const string GithubUrl = "https://github.com/bitlamas/cc-usage-monitor";
    private static readonly IBrush UpdateAvailableBrush = SolidColorBrush.Parse("#D97757");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#857F75");

    private readonly IUpdateClient _updateClient;
    private readonly ILog _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly Version _running;

    /// <summary>Where the link button navigates; retargeted to a release when one is found.</summary>
    private string _linkUrl = GithubUrl;

    public AboutWindow() : this(null, null) { }

    public AboutWindow(IUpdateClient? updateClient, ILog? log)
    {
        InitializeComponent();

        _updateClient = updateClient ?? new GitHubUpdateClient();
        _log = log ?? new NullLog();

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        _running = v is null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        VersionText.Text = $"Version {_running.Major}.{_running.Minor}.{_running.Build}";

        GithubLink.Click += (_, _) => OpenUrl(_linkUrl);
        CloseButton.Click += (_, _) => Close();

        // Closing on deactivate is what makes this feel like a popover, but it would also
        // kill an in-flight update check the moment the user glanced elsewhere — cancel
        // explicitly so nothing tries to touch a closed window.
        Deactivated += (_, _) => Close();
        Closed += (_, _) =>
        {
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
            _cts.Dispose();
        };
    }

    /// <summary>
    /// Starts an update check and reports the outcome in the window. Safe to call before
    /// or after Show(); never throws, and never leaves the status line on "checking".
    /// </summary>
    public void BeginUpdateCheck()
    {
        UpdateStatusText.IsVisible = true;
        UpdateStatusText.Foreground = MutedBrush;
        UpdateStatusText.Text = UpdateText.Checking;

        _ = RunUpdateCheckAsync();
    }

    private async Task RunUpdateCheckAsync()
    {
        UpdateCheckResult result;
        try
        {
            result = await _updateClient.CheckAsync(_running, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // CheckAsync is documented not to throw; belt-and-braces so a future change
            // there cannot take the app down from a fire-and-forget task.
            _log.Exception(LogLevel.Error, "UpdateCheck", ex);
            result = UpdateCheckResult.Failed();
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsVisible)
                return;   // user dismissed the popover while we were waiting

            UpdateStatusText.Text = UpdateText.StatusLine(result, _running);
            UpdateStatusText.Foreground = result.Status == UpdateStatus.UpdateAvailable
                ? UpdateAvailableBrush
                : MutedBrush;

            GithubLink.Content = UpdateText.ActionLabel(result);
            if (result.Status == UpdateStatus.UpdateAvailable && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
                _linkUrl = result.ReleaseUrl!;
        });

        _log.Write(LogLevel.Info, LogFormat.UpdateCheck(result));
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // Opening a browser is best-effort.
        }
    }
}
