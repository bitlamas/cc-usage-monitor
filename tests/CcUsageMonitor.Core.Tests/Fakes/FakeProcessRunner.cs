using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CcUsageMonitor.Core.Tests.Fakes;

/// <summary>
/// Fake IProcessRunner for deterministic testing.
/// Allows configuring return values and simulating timeouts.
/// </summary>
public class FakeProcessRunner : CcUsageMonitor.Core.Services.IProcessRunner
{
    private readonly Dictionary<(string file, string args), int> _exitCodes = new();
    private bool _simulateTimeout = false;
    private int? _forcedExitCode;
    private Action? _onRun;

    /// <summary>Set a forced exit code for all runs.</summary>
    public void SetExitCode(int code)
    {
        _forcedExitCode = code;
    }

    /// <summary>Set a specific exit code for a specific file+args combo.</summary>
    public void SetExitCode(string file, string args, int code)
    {
        _exitCodes[(file, args)] = code;
    }

    /// <summary>Simulate a timeout (returns -1).</summary>
    public void SimulateTimeout()
    {
        _simulateTimeout = true;
    }

    /// <summary>Clear timeout simulation.</summary>
    public void ClearTimeout()
    {
        _simulateTimeout = false;
    }

    /// <summary>Callback invoked when RunAsync is called.</summary>
    public void SetOnRun(Action callback)
    {
        _onRun = callback;
    }

    /// <summary>Count of how many times RunAsync was called.</summary>
    public int RunCount { get; private set; }

    public Task<int> RunAsync(string file, string args, TimeSpan timeout, CancellationToken ct)
    {
        _onRun?.Invoke();
        RunCount++;

        if (_simulateTimeout)
            return Task.FromResult(-1);

        if (_forcedExitCode.HasValue)
            return Task.FromResult(_forcedExitCode.Value);

        if (_exitCodes.TryGetValue((file, args), out var code))
            return Task.FromResult(code);

        return Task.FromResult(0);
    }
}
