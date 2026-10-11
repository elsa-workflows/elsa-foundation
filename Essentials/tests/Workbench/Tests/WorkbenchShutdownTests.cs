using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// Stopping is a whole-composition property, like activation. The host stops its root hosted services, CShells
/// drains each shell and runs its terminators, and those stop the shell's diagnostics capture drains. Every step
/// must finish within the host's shutdown timeout. A step that ignores its stop token hangs the process. A step
/// that overruns the timeout makes CShells dispose the shell mid-drain, and that sheds captured diagnostics
/// without failing anything.
/// </summary>
public sealed class WorkbenchShutdownTests
{
    /// <summary><c>HostOptions.ShutdownTimeout</c>'s default, which the Workbench does not change.</summary>
    private static readonly TimeSpan HostShutdownTimeout = TimeSpan.FromSeconds(30);

    [SkippableFact]
    public async Task Stock_host_drains_its_shell_and_exits_cleanly_within_the_shutdown_timeout()
    {
        Skip.If(OperatingSystem.IsWindows(), "The host is stopped with a POSIX termination signal.");
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);

        // Twice the timeout separates a hang from an overrun, which the assertions below report on their own.
        var elapsed = await workbench.StopAsync(HostShutdownTimeout * 2);

        Assert.True(elapsed is not null, $"The Workbench was still running {HostShutdownTimeout * 2} after SIGTERM. Host output:{Environment.NewLine}{workbench.Output}");
        var output = workbench.Output;
        var shutdownStart = output.IndexOf("Application is shutting down", StringComparison.Ordinal);
        Assert.True(shutdownStart >= 0, $"The Workbench exited without a graceful shutdown. Host output:{Environment.NewLine}{output}");
        var shutdown = output[shutdownStart..];
        // The shell drain's own log lines also show the level prefixes are plain text, which the warning check relies on.
        Assert.Contains("info: CShells.Hosting.CShellsStartupHostedService", shutdown, StringComparison.Ordinal);
        Assert.Contains("transitioned Draining → Drained", shutdown, StringComparison.Ordinal);
        var problems = shutdown.Split('\n')
            .Where(line => line.StartsWith("warn:", StringComparison.Ordinal) || line.StartsWith("fail:", StringComparison.Ordinal) || line.StartsWith("crit:", StringComparison.Ordinal))
            .ToList();
        Assert.True(problems.Count == 0, $"The Workbench logged problems while stopping. Shutdown output:{Environment.NewLine}{shutdown}");
        Assert.Equal(0, workbench.ExitCode);
        Assert.True(elapsed < HostShutdownTimeout, $"Stopping took {elapsed}, beyond the host's {HostShutdownTimeout} shutdown timeout.");
    }
}
