using System;
using System.Threading;
using System.Threading.Tasks;
using CcUsageMonitor.Core.Services;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

/// <summary>
/// Tests for ProcessRunner — the real implementation.
/// Uses echo/dummy commands where possible; primarily validates the FakeProcessRunner path.
/// </summary>
public class ProcessRunnerTests
{
    [Fact]
    public async Task ProcessRunner_FakeRunner_ReturnsConfiguredExitCode()
    {
        // Arrange — use the fake to validate the interface contract
        var fake = new CcUsageMonitor.Core.Tests.Fakes.FakeProcessRunner();
        fake.SetExitCode(42);
        var sut = fake;

        // Act
        var exitCode = await sut.RunAsync("test.exe", "arg1 arg2", TimeSpan.FromSeconds(30), CancellationToken.None);

        // Assert
        Assert.Equal(42, exitCode);
    }

    [Fact]
    public async Task ProcessRunner_FakeRunner_Timeout_ReturnsNegativeOne()
    {
        // Arrange
        var fake = new CcUsageMonitor.Core.Tests.Fakes.FakeProcessRunner();
        fake.SimulateTimeout();
        var sut = fake;

        // Act
        var exitCode = await sut.RunAsync("claude", "-p hi", TimeSpan.FromSeconds(30), CancellationToken.None);

        // Assert
        Assert.Equal(-1, exitCode);
    }

    [Fact]
    public async Task ProcessRunner_FakeRunner_ExactArgsMatch_ReturnsCorrectCode()
    {
        // Arrange
        var fake = new CcUsageMonitor.Core.Tests.Fakes.FakeProcessRunner();
        fake.SetExitCode("claude", "-p hi --max-budget-usd 0.01", 0);
        fake.SetExitCode("claude", "other-args", 1);
        var sut = fake;

        // Act
        var exitCode1 = await sut.RunAsync("claude", "-p hi --max-budget-usd 0.01", TimeSpan.FromSeconds(30), CancellationToken.None);
        var exitCode2 = await sut.RunAsync("claude", "other-args", TimeSpan.FromSeconds(30), CancellationToken.None);

        // Assert
        Assert.Equal(0, exitCode1);
        Assert.Equal(1, exitCode2);
    }

    [Fact]
    public async Task ProcessRunner_FakeRunner_RunCount_TracksCalls()
    {
        // Arrange
        var fake = new CcUsageMonitor.Core.Tests.Fakes.FakeProcessRunner();
        var sut = fake;

        // Act
        await sut.RunAsync("a", "b", TimeSpan.FromSeconds(10), CancellationToken.None);
        await sut.RunAsync("a", "b", TimeSpan.FromSeconds(10), CancellationToken.None);
        await sut.RunAsync("c", "d", TimeSpan.FromSeconds(10), CancellationToken.None);

        // Assert
        Assert.Equal(3, fake.RunCount);
    }

    [Fact]
    public async Task ProcessRunner_FakeRunner_OnRunCallback_Invoked()
    {
        // Arrange
        var fake = new CcUsageMonitor.Core.Tests.Fakes.FakeProcessRunner();
        var invoked = false;
        fake.SetOnRun(() => { invoked = true; });

        // Act
        await fake.RunAsync("test", "args", TimeSpan.FromSeconds(10), CancellationToken.None);

        // Assert
        Assert.True(invoked);
    }

    [Fact]
    public async Task ProcessRunner_FakeRunner_ForcedExitCode_AppliesGlobally()
    {
        // Arrange
        var fake = new CcUsageMonitor.Core.Tests.Fakes.FakeProcessRunner();
        fake.SetExitCode(-1); // forced global

        // Act
        var code = await fake.RunAsync("anything", "any args", TimeSpan.FromSeconds(30), CancellationToken.None);

        // Assert — forced should take precedence
        Assert.Equal(-1, code);
    }

    [Fact]
    public async Task ProcessRunner_RealRunner_Timeout_KillsProcessAndReturnsNegativeOne()
    {
        // Arrange — real ProcessRunner with a command that outlives the timeout
        // Uses `sleep 5` (GNU/coreutils sleep, available in MSYS2/WSL/Unix) which
        // blocks for 5 seconds. ProcessRunner timeout is 1 second.
        var runner = new ProcessRunner();
        var timeout = TimeSpan.FromSeconds(1);
        var file = "sleep";
        var args = "5";

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var exitCode = await runner.RunAsync(file, args, timeout, CancellationToken.None);
        stopwatch.Stop();

        // Assert — should return -1 within ~timeout + small margin
        Assert.Equal(-1, exitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Timeout kill should return within ~1s, took {stopwatch.Elapsed.TotalSeconds:F1}s");
    }
}
