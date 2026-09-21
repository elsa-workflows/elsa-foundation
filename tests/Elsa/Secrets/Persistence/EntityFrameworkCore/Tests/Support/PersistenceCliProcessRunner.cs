using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>How the connection string reaches the tool. Never as an argument, under either value.</summary>
internal enum ConnectionTransport
{
    /// <summary>The child process's own environment carries it; only the variable's NAME is an argument.</summary>
    Environment,

    /// <summary>The child process's own stdin carries it; nothing about it is an argument.</summary>
    Stdin
}

/// <summary>
/// Runs the real <c>dotnet elsa persistence</c> CLI against the Secrets module the way an operator does:
/// a separate <c>dotnet exec</c> process, pointed at a built host's output directory, with no source tree
/// involved. Replaces the <c>dual-migrate.sh</c> runner this suite used before #1878 retired that script.
/// The PostgreSQL integration project links this file so its Testcontainers proof crosses the exact same
/// process boundary as the SQLite tests.
/// </summary>
/// <remarks>
/// <para>
/// In-process invocation would skip what these tests are for: that the tool starts a worker inside the
/// host's own dependency closure, that the exit code the process reports is the one the host's tooling
/// decided, and — the reason the connection travels the way it does — that neither stream ever carries the
/// connection string back out.
/// </para>
/// <para>
/// No cross-process lock, unlike the runner this replaces: that one existed because every <c>dotnet ef</c>
/// invocation copied its BuildHost into the startup project's output folder and concurrent callers raced on
/// that copy. This tool only reads the host directory, so parallel callers with their own databases do not
/// contend.
/// </para>
/// <para>
/// A third near-identical CLI process runner alongside <c>tests/Elsa/Cli/Tests/DotnetElsa.cs</c> and
/// <c>tests/Elsa/Persistence/EntityFrameworkCore/CliAcceptance/ProviderTests/DotnetElsaTool.cs</c> -- same
/// repo-root walk, same muxer resolution, same <see cref="ProcessStartInfo"/>/timeout/capture shape. Left
/// unshared on purpose: the three live in test projects with disjoint dependency closures, and a shared
/// test-utility project for this would be disproportionate, the same reasoning already applied to the
/// duplicated <c>TableCount</c> helper in #1876.
/// </para>
/// </remarks>
internal static class PersistenceCliProcessRunner
{
    /// <summary>The CLI's own default, named explicitly so the flag the connection travels under is visible here.</summary>
    public const string ConnectionVariable = "ELSA_EF_CONNECTION";

    /// <summary>The canonical module name this suite's host declares, as <c>dotnet elsa persistence list</c> reports it.</summary>
    public const string Module = "Secrets";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(180);

    private static readonly string Configuration =
        typeof(PersistenceCliProcessRunner).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
        ?? throw new InvalidOperationException("The test assembly does not declare its build configuration.");

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>The tool's own build output, which is also where its worker sits — the shipped layout.</summary>
    private static readonly string ToolAssembly =
        Path.Join(RepositoryRoot, "src", "Elsa", "Cli", "bin", Configuration, "net10.0", "Elsa.Cli.dll");

    /// <summary>
    /// The fixture host that ships every first-party EF module — Secrets among them — and all four provider
    /// engines. Reused rather than duplicated, the same way the CLI acceptance leg reuses it: what the tool
    /// inspects is a built host's output directory, which is all a packaged deployment offers it.
    /// </summary>
    private static readonly string Host =
        Path.Join(RepositoryRoot, "tests", "Elsa", "Cli", "Fixtures", "Host", "bin", Configuration, "net10.0");

    /// <summary>
    /// Runs one <c>persistence</c> command against <paramref name="connection"/> for the Secrets module.
    /// Both project references that produce the tool and the host are declared as build dependencies of this
    /// suite, so a missing artifact is a broken build rather than a reason to skip: it says so instead.
    /// </summary>
    public static CliResult Run(
        string command,
        string provider,
        string connection,
        ConnectionTransport transport = ConnectionTransport.Environment)
    {
        Require(File.Exists(ToolAssembly), $"The dotnet-elsa tool was not built at '{ToolAssembly}'.");
        Require(Directory.Exists(Host), $"The CLI fixture host was not built at '{Host}'.");

        var startInfo = new ProcessStartInfo(DotnetMuxer())
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardInput = transport == ConnectionTransport.Stdin,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        // A schema is an input to the command, read from this tool's own environment. An ambient one would
        // silently send every statement somewhere other than where this test reads them back.
        startInfo.Environment.Remove("ELSA_EF_SCHEMA");

        foreach (var argument in new[]
                 {
                     "exec", ToolAssembly, "persistence", command,
                     "--host", Host,
                     "--provider", provider,
                     "--modules", Module
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (transport == ConnectionTransport.Environment)
        {
            startInfo.Environment[ConnectionVariable] = connection;
            startInfo.ArgumentList.Add("--connection-env");
            startInfo.ArgumentList.Add(ConnectionVariable);
        }
        else
        {
            startInfo.Environment.Remove(ConnectionVariable);
            startInfo.ArgumentList.Add("--connection-stdin");
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"'{startInfo.FileName}' could not be started.");
        if (transport == ConnectionTransport.Stdin)
        {
            process.StandardInput.Write(connection);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(Timeout);
        try
        {
            process.WaitForExitAsync(cancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            // Names the command, never the connection: the argument list is the one place it is not.
            throw new TimeoutException($"dotnet elsa persistence {command} did not exit within {Timeout}.");
        }

        Task.WaitAll(output, error);
        return new CliResult(process.ExitCode, output.Result, error.Result);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"{message} Build Elsa.Server.slnx before running this suite.");
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already gone, which is the state this call wanted.
        }
    }

    private static string DotnetMuxer() =>
        System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}

/// <summary>What one run of the tool produced.</summary>
internal sealed record CliResult(int ExitCode, string Output, string Error)
{
    /// <summary>Both streams, for the assertions that care what was said rather than where it was said.</summary>
    public string Text => Output + Error;

    public string Describe()
    {
        var text = new StringBuilder();
        text.Append("exit ").Append(ExitCode);
        if (!string.IsNullOrWhiteSpace(Output))
            text.AppendLine().Append(Output);
        if (!string.IsNullOrWhiteSpace(Error))
            text.AppendLine().Append(Error);
        return text.ToString();
    }
}
