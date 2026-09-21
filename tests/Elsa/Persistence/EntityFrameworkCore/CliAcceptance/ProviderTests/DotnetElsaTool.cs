using System.Diagnostics;
using System.Reflection;

namespace Elsa.Persistence.EntityFrameworkCore.CliAcceptance.ProviderTests;

/// <summary>What one run of the tool produced.</summary>
internal sealed record ToolRun(int ExitCode, string Output, string Error);

/// <summary>
/// Runs the real <c>dotnet-elsa</c> tool the way an operator does: a separate <c>dotnet exec</c> process
/// against a host's build output, never loaded in process. An in-process shortcut would prove the artifact
/// this leg checks matches nothing an operator could actually produce (issue #1875) -- this is the same
/// pattern <c>tests/Elsa/Cli/Tests/DotnetElsa.cs</c> uses, kept local to this project rather than shared
/// across an internal type boundary.
/// </summary>
internal static class DotnetElsaTool
{
    private static readonly string Configuration =
        typeof(DotnetElsaTool).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>The tool's own build output, which is also where its worker sits -- the shipped layout.</summary>
    private static readonly string ToolAssembly = Path.Join(RepoRoot, "src", "Elsa", "Cli", "bin", Configuration, "net10.0", "Elsa.Cli.dll");

    /// <summary>
    /// The fixture host that ships every first-party EF module plus a third-party one and all four provider
    /// engines (<c>tests/Elsa/Cli/Fixtures/Host</c>) -- reused rather than a bespoke host for this leg, since
    /// it already carries exactly the module set this leg scripts.
    /// </summary>
    public static readonly string Host = Path.Join(RepoRoot, "tests", "Elsa", "Cli", "Fixtures", "Host", "bin", Configuration, "net10.0");

    public static ToolRun Script(string provider, IReadOnlyList<string> modules, string output) => Run(
        "persistence", "script",
        "--host", Host,
        "--provider", provider,
        "--modules", string.Join(',', modules),
        "--output", output);

    private static ToolRun Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(DotnetMuxer())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(ToolAssembly);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ToolRun(process.ExitCode, output, error);
    }

    private static string DotnetMuxer() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
