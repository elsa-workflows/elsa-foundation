using Elsa.Cli;
using System.Diagnostics;
using System.Reflection;
using Xunit;

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
    public static string ToolAssembly { get; } = Path.Join(RepoRoot, "src", "essentials", "Cli", "bin", Configuration, "net10.0", "Elsa.Cli.dll");

    /// <summary>A fixture host's build output directory.</summary>
    public static string Host(string fixture) =>
        Path.Join(RepoRoot, "tests", "essentials", "Cli", "Fixtures", fixture, "bin", Configuration, "net10.0");

    public static CliRun Run(params string[] arguments) => Run(environment: null, arguments);

    /// <summary>Runs the tool with extra environment variables, for the inputs an operator supplies that way.</summary>
    public static CliRun Run(IReadOnlyDictionary<string, string>? environment, params string[] arguments) =>
        RunAsync(environment, stdin: null, workingDirectory: null, arguments).GetAwaiter().GetResult();

    /// <summary>
    /// Runs the tool from <paramref name="workingDirectory"/>, for the assertions about what the tool
    /// resolves against its own current directory rather than against the host it was given.
    /// </summary>
    /// <remarks>
    /// Every other overload leaves the current directory inherited from the test host, which is already some
    /// bin directory rather than any host. Naming it explicitly is what lets a test put a decoy where a
    /// relative path would land if it were resolved against the process.
    /// </remarks>
    public static CliRun RunIn(string workingDirectory, params string[] arguments) =>
        RunAsync(environment: null, stdin: null, workingDirectory, arguments).GetAwaiter().GetResult();

    /// <summary>Runs the tool with <paramref name="stdin"/> written to and closed on its own stdin, for <c>--connection-stdin</c>.</summary>
    public static Task<CliRun> RunWithStdinAsync(string stdin, params string[] arguments) =>
        RunAsync(environment: null, stdin, workingDirectory: null, arguments);

    private static async Task<CliRun> RunAsync(
        IReadOnlyDictionary<string, string>? environment,
        string? stdin,
        string? workingDirectory,
        string[] arguments)
    {
        var startInfo = new ProcessStartInfo(DotnetMuxer.Path())
        {
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };
        foreach (var variable in environment ?? new Dictionary<string, string>())
            startInfo.Environment[variable.Key] = variable.Value;
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(ToolAssembly);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new(process.ExitCode, await output, await error);
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

/// <summary>
/// Byte-for-byte comparison of two artifact directories, for the tests that assert one run produced exactly
/// what another already produced.
/// </summary>
internal static class ArtifactAssert
{
    public static void SameBytes(string expected, string actual)
    {
        var expectedFiles = Files(expected);
        var actualFiles = Files(actual);

        Assert.Equal(expectedFiles.Keys, actualFiles.Keys);
        foreach (var (name, bytes) in expectedFiles)
            Assert.True(bytes.AsSpan().SequenceEqual(actualFiles[name]), $"{name} differs between the two runs.");
    }

    private static SortedDictionary<string, byte[]> Files(string directory) =>
        new(Directory.EnumerateFiles(directory).ToDictionary(file => Path.GetFileName(file), File.ReadAllBytes), StringComparer.Ordinal);
}
