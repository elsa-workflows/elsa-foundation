using Elsa.Cli;
using System.Diagnostics;
using System.Reflection;

namespace Elsa.Cli.Tests;

/// <summary>What one run of the tool produced.</summary>
public sealed record CliRun(int ExitCode, string Output, string Error)
{
    /// <summary>Both streams, for the assertions that care what was said rather than where it was said.</summary>
    public string Text => Output + Error;
}

/// <summary>
/// Runs the real tool the way an operator does: a separate process, against a host's build output, with no
/// source tree involved.
/// </summary>
/// <remarks>
/// In-process invocation would test the command tree and skip the two things this slice is actually about —
/// that the worker starts inside the host's closure at all, and that the exit code the process reports is
/// the one the host's tooling decided.
/// </remarks>
internal static class DotnetElsa
{
    private static readonly string Configuration =
        typeof(DotnetElsa).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>The tool's own build output, which is also where its worker sits — the shipped layout.</summary>
    public static string ToolAssembly { get; } = Path.Join(RepoRoot, "src", "Elsa", "Cli", "bin", Configuration, "net10.0", "Elsa.Cli.dll");

    /// <summary>A fixture host's build output directory.</summary>
    public static string Host(string fixture) =>
        Path.Join(RepoRoot, "tests", "Elsa", "Cli", "Fixtures", fixture, "bin", Configuration, "net10.0");

    public static CliRun Run(params string[] arguments) => Run(environment: null, arguments);

    /// <summary>Runs the tool with extra environment variables, for the inputs an operator supplies that way.</summary>
    public static CliRun Run(IReadOnlyDictionary<string, string>? environment, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(DotnetMuxer.Path())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var variable in environment ?? new Dictionary<string, string>())
            startInfo.Environment[variable.Key] = variable.Value;
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(ToolAssembly);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new(process.ExitCode, output.Result, error.Result);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}

/// <summary>A temporary directory that deletes itself, for artifacts a test writes.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory(string prefix) => Path = Directory.CreateTempSubdirectory(prefix).FullName;

    public string Path { get; }

    public string File(string name) => System.IO.Path.Join(Path, name);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
