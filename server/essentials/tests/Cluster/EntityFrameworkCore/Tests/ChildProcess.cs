using System.Diagnostics;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>A child process run to completion with its output captured, for the test that runs <c>dotnet</c> or the persistence tool.</summary>
internal static class ChildProcess
{
    /// <summary>What the output events still owe once the process has exited, and how long they are given to arrive.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs <paramref name="startInfo"/> to completion, redirecting both output streams, and returns its exit code and
    /// everything it wrote. Kills its whole process tree and throws when it outlasts <paramref name="timeout"/>;
    /// <paramref name="description"/> names the command in that message, so it must not carry a secret.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout, string description)
    {
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;

        var output = new CapturedOutput();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, line) => output.Append(line.Data);
        process.ErrorDataReceived += (_, line) => output.Append(line.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{description} did not finish within {timeout} and was killed. Its output:{Environment.NewLine}{output}");
        }

        // The output events are drained once the process has exited and its streams have closed; bounded, as a grandchild that
        // outlives it can keep them open.
        process.WaitForExit(DrainTimeout);
        return (process.ExitCode, output.ToString());
    }
}
