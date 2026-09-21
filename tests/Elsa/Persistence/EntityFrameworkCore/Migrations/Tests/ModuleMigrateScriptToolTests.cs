using System.Diagnostics;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Exercises the real <c>tools/ef/module-migrate.sh</c> against a recording <c>dotnet</c> shim (#1878).
/// <c>apply</c>, <c>validate</c>, <c>script</c> and <c>script-check</c> are thin shims over <c>dotnet elsa
/// persistence</c> now, so what this pins is the shim's own contract — that it builds the CLI and the
/// tooling project, then calls the CLI with the right <c>--host</c>/<c>--provider</c>/module-selection/
/// connection flags, and passes its exit code straight through. The SQL-layout and provider-agreement
/// assertions that used to live here belong to the CLI's own tests now (<c>tests/Elsa/Cli/Tests</c>), since
/// the CLI is what actually generates and checks the artifact.
/// </summary>
public sealed class ModuleMigrateScriptToolTests : IDisposable
{
    private readonly string sandbox = Path.Join(Path.GetTempPath(), $"elsa-module-migrate-{Guid.NewGuid():N}");
    private readonly string repository;
    private readonly string output;
    private readonly string calls;

    public ModuleMigrateScriptToolTests()
    {
        repository = Path.Join(sandbox, "repo");
        output = Path.Join(sandbox, "db", "migrations");
        calls = Path.Join(sandbox, "calls.log");
        Directory.CreateDirectory(Path.Join(repository, "tools", "ef"));
        File.Copy(RealScript, Path.Join(repository, "tools", "ef", "module-migrate.sh"));
        // Fixed relative paths the shim builds and points the CLI at (--host); the fake `dotnet build`
        // below only needs a project file to exist at each one to compute an output directory from.
        Directory.CreateDirectory(Path.Join(repository, "tools", "ef", "Elsa.EntityFrameworkCore.Tooling"));
        File.WriteAllText(
            Path.Join(repository, "tools", "ef", "Elsa.EntityFrameworkCore.Tooling", "Elsa.EntityFrameworkCore.Tooling.csproj"),
            "<Project />");
        Directory.CreateDirectory(Path.Join(repository, "src", "Elsa", "Cli"));
        File.WriteAllText(Path.Join(repository, "src", "Elsa", "Cli", "Elsa.Cli.csproj"), "<Project />");

        WriteShim("dotnet", """
            #!/usr/bin/env bash
            set -euo pipefail
            case "${1:-}" in
              build)
                shift
                project="$1"
                configuration="Release"
                shift || true
                while [[ $# -gt 0 ]]; do
                  if [[ "$1" == "-c" ]]; then
                    configuration="${2:?}"
                    shift 2
                  else
                    shift
                  fi
                done
                project_dir="$(dirname "$project")"
                name="$(basename "$project" .csproj)"
                # Real `dotnet build` writes one <application>.deps.json under a target-framework folder
                # this script's own build_output_dir has to find without knowing its name in advance.
                target="$project_dir/bin/$configuration/net10.0"
                mkdir -p "$target"
                : > "$target/$name.deps.json"
                exit 0
                ;;
              exec)
                dll="${2:?}"
                shift 2
                # The connection never travels as an argument (D7): only what actually reached this
                # recording shim's argv is written here, never this process's environment or stdin.
                printf '%s\n' "$*" >> "$ELSA_EF_TEST_CALLS"
                echo "dotnet exec $(basename "$dll") $*"
                exit "${ELSA_CLI_TEST_EXIT_CODE:-0}"
                ;;
              *)
                exit 95
                ;;
            esac
            """);
    }

    [SkippableFact]
    public void Apply_builds_the_cli_and_the_tooling_project_then_calls_persistence_apply_with_every_module_selected()
    {
        var result = RunWithEnvironment(
            new Dictionary<string, string> { ["ELSA_EF_CONNECTION"] = "Data Source=sentinel.db" },
            "apply", "PostgreSql");

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(File.ReadAllLines(calls));
        Assert.StartsWith("persistence apply --host ", call, StringComparison.Ordinal);
        Assert.Contains("Elsa.EntityFrameworkCore.Tooling/bin/Release/net10.0", call, StringComparison.Ordinal);
        Assert.Contains("--provider PostgreSql", call, StringComparison.Ordinal);
        Assert.Contains("--all", call, StringComparison.Ordinal);
        Assert.Contains("--connection-env ELSA_EF_CONNECTION", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--modules", call, StringComparison.Ordinal);
        Assert.DoesNotContain("Data Source=sentinel.db", call, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Validate_calls_persistence_validate_the_same_way_apply_calls_persistence_apply()
    {
        var result = RunWithEnvironment(
            new Dictionary<string, string> { ["ELSA_EF_CONNECTION"] = "Data Source=sentinel.db" },
            "validate", "SqlServer");

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(File.ReadAllLines(calls));
        Assert.StartsWith("persistence validate --host ", call, StringComparison.Ordinal);
        Assert.Contains("--provider SqlServer", call, StringComparison.Ordinal);
        Assert.Contains("--all", call, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void A_module_filter_is_forwarded_as_dash_dash_modules_instead_of_dash_dash_all()
    {
        var result = RunWithEnvironment(
            new Dictionary<string, string> { ["ELSA_EF_CONNECTION"] = "Data Source=sentinel.db" },
            "apply", "PostgreSql", "Secrets,Workflows.Runtime");

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(File.ReadAllLines(calls));
        Assert.Contains("--modules Secrets,Workflows.Runtime", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--all", call, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Script_calls_persistence_script_with_the_resolved_output_directory()
    {
        var result = Run("script", "PostgreSql", output);

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(File.ReadAllLines(calls));
        Assert.StartsWith("persistence script --host ", call, StringComparison.Ordinal);
        Assert.Contains("--provider PostgreSql", call, StringComparison.Ordinal);
        Assert.Contains("--all", call, StringComparison.Ordinal);
        Assert.Contains($"--output {output}", call, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Script_check_calls_persistence_script_check_with_only_the_directory_and_the_host_never_a_provider()
    {
        Directory.CreateDirectory(output);

        var result = Run("script-check", output);

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(File.ReadAllLines(calls));
        Assert.StartsWith($"persistence script-check {output} --host ", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--provider", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--modules", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--all", call, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shim reports exactly what the CLI decided, not a code of its own devising — a negative result
    /// (1), a usage refusal (2), a resolution failure (3) and a database failure (4) all have to reach the
    /// operator unchanged.
    /// </summary>
    [SkippableFact]
    public void The_shims_exit_code_is_whatever_the_cli_exited_with()
    {
        var result = RunWithEnvironment(
            new Dictionary<string, string> { ["ELSA_EF_CONNECTION"] = "Data Source=sentinel.db", ["ELSA_CLI_TEST_EXIT_CODE"] = "3" },
            "validate", "MySql");

        Assert.Equal(3, result.ExitCode);
    }

    [SkippableFact]
    public void An_unknown_command_prints_the_usage_of_every_command()
    {
        var result = Run("not-a-command");

        Assert.Equal(2, result.ExitCode);
        foreach (var command in new[] { "pending", "apply", "validate", "script", "script-check" })
            Assert.Contains($"module-migrate.sh {command} ", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The interim fix an earlier slice made (#1876, FR-051) and this one keeps: <c>apply</c>/<c>validate</c>
    /// take no positional connection argument, converging on <c>--connection-env</c>/<c>--connection-stdin</c>
    /// the same way <c>dotnet elsa</c> itself does — because that is now literally what runs underneath.
    /// </summary>
    [SkippableFact]
    public void Apply_reads_the_connection_from_a_named_environment_variable_never_argv()
    {
        const string sentinel = "Data Source=sentinel-4f2b91.db";
        var result = RunWithEnvironment(
            new Dictionary<string, string> { ["CONTOSO_CONNECTION"] = sentinel },
            "apply", "PostgreSql", "--connection-env", "CONTOSO_CONNECTION");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(sentinel, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, result.Error, StringComparison.Ordinal);
        var call = Assert.Single(File.ReadAllLines(calls));
        Assert.Contains("--connection-env CONTOSO_CONNECTION", call, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, call, StringComparison.Ordinal);
    }

    /// <summary>Defaults to <c>ELSA_EF_CONNECTION</c> (D7) when <c>--connection-env</c> is not given.</summary>
    [SkippableFact]
    public void Apply_defaults_the_connection_environment_variable_name()
    {
        var result = RunWithEnvironment(
            new Dictionary<string, string> { ["ELSA_EF_CONNECTION"] = "Data Source=sentinel-default.db" },
            "apply", "PostgreSql");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--connection-env ELSA_EF_CONNECTION", Assert.Single(File.ReadAllLines(calls)), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--connection-stdin</c> travels to the CLI unexamined; this process's own stdin is never read or
    /// consumed along the way, so the child inherits the exact stream the operator piped in.
    /// </summary>
    [SkippableFact]
    public void Validate_forwards_dash_dash_connection_stdin_without_reading_this_scripts_own_stdin()
    {
        var result = RunWithStdin("Data Source=sentinel-stdin.db", "validate", "PostgreSql", "--connection-stdin");

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(File.ReadAllLines(calls));
        Assert.Contains("--connection-stdin", call, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-stdin", call, StringComparison.Ordinal);
    }

    /// <summary>There is no <c>--connection</c> flag (D7); a caller that passes one is told, and nothing runs.</summary>
    [SkippableFact]
    public void A_connection_flag_is_rejected_and_nothing_runs()
    {
        var result = Run("apply", "PostgreSql", "--connection", "Data Source=whatever.db");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--connection is not accepted", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(calls));
    }

    public void Dispose() => Directory.Delete(sandbox, recursive: true);

    private static string RealScript
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Join(directory.FullName, "tools", "ef", "module-migrate.sh");
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new InvalidOperationException("tools/ef/module-migrate.sh was not found above the test assembly.");
        }
    }

    private void WriteShim(string name, string body)
    {
        var path = Path.Join(sandbox, name);
        File.WriteAllText(path, body);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private Result Run(params string[] arguments) => Run(environment: null, stdin: null, arguments);

    /// <summary>Runs with extra environment variables, for <c>--connection-env</c> and the exit-code sentinel.</summary>
    private Result RunWithEnvironment(IReadOnlyDictionary<string, string> environment, params string[] arguments) =>
        Run(environment, stdin: null, arguments);

    /// <summary>Runs with <paramref name="stdin"/> written to and closed on this process's own stdin, for <c>--connection-stdin</c>.</summary>
    private Result RunWithStdin(string stdin, params string[] arguments) => Run(environment: null, stdin, arguments);

    private Result Run(IReadOnlyDictionary<string, string>? environment, string? stdin, params string[] arguments)
    {
        Skip.If(OperatingSystem.IsWindows(), "The recording shim requires bash and Unix executable permissions.");
        var start = new ProcessStartInfo("bash", [Path.Join(repository, "tools", "ef", "module-migrate.sh"), .. arguments])
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null
        };
        start.Environment["PATH"] = $"{sandbox}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}";
        start.Environment["ELSA_EF_TEST_CALLS"] = calls;
        foreach (var variable in environment ?? new Dictionary<string, string>())
            start.Environment[variable.Key] = variable.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("bash did not start.");
        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(120_000), "module-migrate.sh did not exit.");
        Task.WaitAll(stdout, stderr);
        return new(process.ExitCode, stdout.Result, stderr.Result);
    }

    private sealed record Result(int ExitCode, string Output, string Error);
}
