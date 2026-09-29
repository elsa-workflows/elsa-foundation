using System.Diagnostics;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// The real <c>dotnet elsa</c> tool as an operator runs it: <c>Elsa.Cli.dll</c> from its own build output, in a process of its
/// own, which starts its worker inside the closure of the host directory it is pointed at. Nothing of the tool or the host is
/// loaded into the test process.
/// </summary>
internal static class ElsaCliProcess
{
    /// <summary>The tool loads the host's package set and runs the migrations: seconds, and this is the ceiling for a hang.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private static string ToolAssembly
    {
        get
        {
            var framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            var path = Path.Join(FoundationHostProcess.RepoRoot, "src", "essentials", "Cli", "bin", FoundationHostProcess.Configuration, framework, "Elsa.Cli.dll");
            return File.Exists(path) ? path : throw new FileNotFoundException($"Build src/essentials/Cli ({FoundationHostProcess.Configuration}) before running these tests.", path);
        }
    }

    /// <summary>
    /// Runs the tool with <paramref name="arguments"/> and <paramref name="environment"/> added to its own, and returns its
    /// exit code and everything it wrote. Kills its process tree if it outlasts <see cref="Timeout"/>.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(IEnumerable<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(FoundationHostProcess.DotnetPath, ["exec", ToolAssembly, .. arguments])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var (name, value) in environment)
            startInfo.Environment[name] = value;

        var output = new CapturedOutput();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, line) => output.Append(line.Data);
        process.ErrorDataReceived += (_, line) => output.Append(line.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            // The arguments and their environment name the connection by variable, never by value, so this is safe to say.
            throw new TimeoutException($"dotnet elsa {string.Join(' ', arguments)} did not finish within {Timeout} and was killed. Its output:{Environment.NewLine}{output}");
        }

        // The output events are drained once the process has exited and its streams have closed.
        process.WaitForExit();
        return (process.ExitCode, output.ToString());
    }
}
