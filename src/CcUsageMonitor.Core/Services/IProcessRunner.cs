using System;
using System.Threading;
using System.Threading.Tasks;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Process runner seam — runs an external command with timeout.
/// Per spec §4.1: stdout/stderr discarded; returns exit code; -1 on timeout-kill.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs a process, discarding stdout and stderr.
    /// </summary>
    /// <param name="file">Executable path or name (e.g. "claude").</param>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="timeout">Maximum time to wait. If exceeded, the process is killed and -1 is returned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Exit code of the process, or -1 if killed due to timeout.</returns>
    Task<int> RunAsync(string file, string args, TimeSpan timeout, CancellationToken ct);
}
