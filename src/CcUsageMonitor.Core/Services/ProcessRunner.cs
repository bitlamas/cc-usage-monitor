using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Real process runner. Discards stdout/stderr; kills on timeout; returns exit code or -1.
/// </summary>
public class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(string file, string args, TimeSpan timeout, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
            return -1;

        using var timeoutCts = new CancellationTokenSource();
        timeoutCts.CancelAfter(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            // Wait for process to exit or timeout
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            // Timeout — kill the process
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch
            {
                // Already exited or access denied — ignore
            }
            return -1;
        }
        finally
        {
            // Discard stdout/stderr
            try { process.StandardOutput.ReadToEnd(); } catch { }
            try { process.StandardError.ReadToEnd(); } catch { }
        }
    }
}
