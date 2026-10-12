using Xunit;

namespace Elsa.Cli.Tests;

public sealed class PseudoTerminalCliTests
{
    [Fact]
    public async Task Pty_runner_waits_for_prompt_then_sends_one_review_line()
    {
        if (OperatingSystem.IsWindows())
        {
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => PseudoTerminalCli.RunProcessAsync(
                "python3", ["-c", "print('unreachable')"], "REVIEW-PROMPT", "accept"));
            return;
        }

        var run = await PseudoTerminalCli.RunProcessAsync(
            "python3",
            ["-c", "import os; print('TTY=' + str(os.isatty(0))); print('REVIEW-PROMPT', flush=True); print('ANSWER=' + input())"],
            "REVIEW-PROMPT",
            "accept");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.ResponseSent);
        Assert.False(run.TimedOut);
        Assert.Contains("TTY=True", run.Output, StringComparison.Ordinal);
        Assert.Contains("ANSWER=accept", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("PTY_", run.Error, StringComparison.Ordinal);
    }
}
