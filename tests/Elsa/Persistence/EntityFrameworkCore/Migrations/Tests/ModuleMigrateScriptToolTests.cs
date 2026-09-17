using System.Diagnostics;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Exercises the real <c>tools/ef/module-migrate.sh script</c> / <c>script-check</c> against a recording
/// <c>dotnet</c> shim, so the artifact contract a DBA pipeline depends on — one idempotent file per module
/// context at <c>&lt;output-dir&gt;/&lt;Module&gt;/&lt;Provider&gt;.sql</c>, and a check that fails on an
/// edited, missing or stale file — is pinned without needing EF, a database, or four provider engines.
/// </summary>
public sealed class ModuleMigrateScriptToolTests : IDisposable
{
    private const string Catalog = """
        AlphaPostgreSqlDbContext|PostgreSql|Elsa.Alpha.Persistence|Elsa.Alpha.Persistence
        AlphaSqliteDbContext|Sqlite|Elsa.Alpha.Persistence|Elsa.Alpha.Persistence
        BetaPostgreSqlDbContext|PostgreSql|Elsa.Beta.Persistence|Elsa.Beta.Persistence
        """;

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
        Directory.CreateDirectory(Path.Join(repository, "src"));
        File.Copy(RealScript, Path.Join(repository, "tools", "ef", "module-migrate.sh"));
        foreach (var assembly in new[] { "Elsa.Alpha.Persistence", "Elsa.Beta.Persistence" })
            File.WriteAllText(Path.Join(repository, "src", $"{assembly}.csproj"), "<Project />");
        WriteShim("dotnet", $$"""
            #!/usr/bin/env bash
            set -euo pipefail
            case "${1:-}" in
              tool|build) exit 0 ;;
              run) printf '%s\n' '{{Catalog}}' ;;
              ef)
                [[ "${2:-}" == "migrations" && "${3:-}" == "script" ]] || exit 97
                context=""; file=""; idempotent=0
                while [[ $# -gt 0 ]]; do
                  case "$1" in
                    --context) context="${2:?}"; shift 2 ;;
                    --output) file="${2:?}"; shift 2 ;;
                    --idempotent) idempotent=1; shift ;;
                    *) shift ;;
                  esac
                done
                [[ $idempotent -eq 1 ]] || exit 96
                printf 'script|%s|%s\n' "$context" "$file" >> "$ELSA_EF_TEST_CALLS"
                # Deterministic stand-in for what EF writes: the same model must script the same bytes twice.
                printf 'INSERT INTO __EFMigrationsHistory_%s VALUES (1);\n' "$context" > "$file"
                ;;
              *) exit 95 ;;
            esac
            """);
    }

    [SkippableFact]
    public void Script_writes_one_idempotent_file_per_module_context_of_the_named_provider()
    {
        var result = Run("script", "PostgreSql", output);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Alpha/PostgreSql.sql", "Beta/PostgreSql.sql"], WrittenScripts());
        // The same module's Sqlite row is in the catalog; naming PostgreSql must not script it. The shim
        // refuses a call without --idempotent, so a recorded call is also proof the flag was passed.
        Assert.Equal(
            ["AlphaPostgreSqlDbContext|Alpha/PostgreSql.sql", "BetaPostgreSqlDbContext|Beta/PostgreSql.sql"],
            ScriptedContexts());
        Assert.Contains("script AlphaPostgreSqlDbContext -> ", result.Output, StringComparison.Ordinal);
        Assert.Contains("Alpha/PostgreSql.sql", result.Output, StringComparison.Ordinal);
        Assert.Contains("script: 2 module context(s) OK", result.Output, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void A_context_filter_scripts_only_what_it_names()
    {
        var result = Run("script", "PostgreSql", output, "BetaPostgreSqlDbContext");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Beta/PostgreSql.sql"], WrittenScripts());
    }

    /// <summary>
    /// EF throws <c>NotSupportedException</c> for a SQLite idempotent script. Writing a plain one into the
    /// same tree would look identical to a reviewer while being unsafe to re-run, so the command refuses.
    /// </summary>
    [SkippableFact]
    public void Script_refuses_sqlite_rather_than_writing_a_file_that_cannot_be_re_run()
    {
        var result = Run("script", "Sqlite", output);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("SQLite cannot produce an idempotent script", result.Error, StringComparison.Ordinal);
        Assert.Contains("apply Sqlite", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
        Assert.False(File.Exists(calls));
    }

    [SkippableFact]
    public void Script_check_passes_when_the_committed_scripts_are_what_the_model_generates()
    {
        Generated();

        var result = Run("script-check", "PostgreSql", output);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("script-check: 2 module context(s) OK", result.Output, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Script_check_fails_on_a_hand_edited_script_and_prints_the_diff()
    {
        Generated();
        var edited = Path.Join(output, "Alpha", "PostgreSql.sql");
        File.WriteAllText(edited, "DROP TABLE elsa_alpha;\n");

        var result = Run("script-check", "PostgreSql", output);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("-DROP TABLE elsa_alpha;", result.Error, StringComparison.Ordinal);
        Assert.Contains("out of date: ", result.Error, StringComparison.Ordinal);
        Assert.Contains("Alpha/PostgreSql.sql", result.Error, StringComparison.Ordinal);
        // The check regenerates into a temporary directory; the tree under review is never rewritten.
        Assert.Equal("DROP TABLE elsa_alpha;\n", File.ReadAllText(edited).ReplaceLineEndings("\n"));
    }

    [SkippableFact]
    public void Script_check_fails_when_a_module_has_no_committed_script()
    {
        Generated();
        File.Delete(Path.Join(output, "Beta", "PostgreSql.sql"));

        var result = Run("script-check", "PostgreSql", output);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("missing script: ", result.Error, StringComparison.Ordinal);
        Assert.Contains("Beta/PostgreSql.sql", result.Error, StringComparison.Ordinal);
    }

    /// <summary>A module that was removed or renamed leaves SQL nobody generates any more.</summary>
    [SkippableFact]
    public void Script_check_fails_on_a_stale_script_no_module_generates()
    {
        Generated();
        Directory.CreateDirectory(Path.Join(output, "Gamma"));
        File.WriteAllText(Path.Join(output, "Gamma", "PostgreSql.sql"), "-- from a module that is gone\n");

        var result = Run("script-check", "PostgreSql", output);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("stale script: ", result.Error, StringComparison.Ordinal);
        Assert.Contains("Gamma/PostgreSql.sql", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A filtered check cannot tell a stale file from one the caller deliberately left out, so it stays
    /// quiet about it — and must still pass on the contexts it did check.
    /// </summary>
    [SkippableFact]
    public void A_filtered_script_check_does_not_report_the_contexts_it_skipped_as_stale()
    {
        Generated();

        var result = Run("script-check", "PostgreSql", output, "AlphaPostgreSqlDbContext");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("script-check: 1 module context(s) OK", result.Output, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Script_check_refuses_a_directory_that_does_not_exist()
    {
        var result = Run("script-check", "PostgreSql", output);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("No such directory", result.Error, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void An_unknown_command_prints_the_usage_of_every_command()
    {
        var result = Run("not-a-command");

        Assert.Equal(2, result.ExitCode);
        foreach (var command in new[] { "pending", "apply", "validate", "script", "script-check" })
            Assert.Contains($"module-migrate.sh {command} ", result.Error, StringComparison.Ordinal);
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

    private void Generated() => Assert.Equal(0, Run("script", "PostgreSql", output).ExitCode);

    private string[] WrittenScripts() =>
        Directory.Exists(output)
            ? [.. Directory.EnumerateFiles(output, "*.sql", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(output, file))
                .Order(StringComparer.Ordinal)]
            : [];

    /// <summary>The recorded calls, with the absolute output path reduced to its module/provider tail.</summary>
    private string[] ScriptedContexts() =>
        [.. File.ReadAllLines(calls).Select(line =>
        {
            var fields = line.Split('|');
            var segments = fields[2].Split('/');
            return $"{fields[1]}|{segments[^2]}/{segments[^1]}";
        })];

    private void WriteShim(string name, string body)
    {
        var path = Path.Join(sandbox, name);
        File.WriteAllText(path, body);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private Result Run(params string[] arguments)
    {
        Skip.If(OperatingSystem.IsWindows(), "The recording shim requires bash and Unix executable permissions.");
        var start = new ProcessStartInfo("bash", [Path.Join(repository, "tools", "ef", "module-migrate.sh"), .. arguments])
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["PATH"] = $"{sandbox}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}";
        start.Environment["ELSA_EF_TEST_CALLS"] = calls;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("bash did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(120_000), "module-migrate.sh did not exit.");
        Task.WaitAll(stdout, stderr);
        return new(process.ExitCode, stdout.Result, stderr.Result);
    }

    private sealed record Result(int ExitCode, string Output, string Error);
}
