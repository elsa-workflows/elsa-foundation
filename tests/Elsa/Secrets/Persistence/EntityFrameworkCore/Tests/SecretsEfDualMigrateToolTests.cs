using Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Keeps the out-of-process dual-migrate script pointed at the derived contexts in this module
/// (the assembly Nuplane loads at apply time), and exercises the real script.
/// </summary>
public sealed class SecretsEfDualMigrateToolTests
{
    [Fact]
    public void Dual_migrate_script_covers_each_derived_secrets_context()
    {
        var lib = File.ReadAllText(RepoPath("tools", "ef", "secrets-ef-lib.sh"));
        var script = File.ReadAllText(RepoPath("tools", "ef", "dual-migrate.sh"));

        Assert.Contains("SecretsSqliteDbContext", lib, StringComparison.Ordinal);
        Assert.Contains("SecretsSqlServerDbContext", lib, StringComparison.Ordinal);
        Assert.Contains("SecretsPostgreSqlDbContext", lib, StringComparison.Ordinal);
        Assert.Contains("has-pending-model-changes", script, StringComparison.Ordinal);
        Assert.Contains("database update", script, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretsSqliteDbContext), lib, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretsSqlServerDbContext), lib, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretsPostgreSqlDbContext), lib, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Dual_migrate_builds_once_and_reuses_non_default_configuration_for_each_ef_call()
    {
        Skip.If(OperatingSystem.IsWindows(), "The recording shims require Unix executable permissions.");

        var shimRoot = Path.Join(Path.GetTempPath(), $"elsa-secrets-ef-shims-{Guid.NewGuid():N}");
        Directory.CreateDirectory(shimRoot);
        var recordingLog = Path.Join(shimRoot, "calls.log");

        try
        {
            WriteExecutableShim(shimRoot, "dotnet", """
                #!/usr/bin/env bash
                set -euo pipefail

                [[ "${1:-}" == "build" ]] || exit 97
                shift
                configuration=""
                disable_build_servers=0
                while [[ $# -gt 0 ]]; do
                  case "$1" in
                    --configuration)
                      configuration="${2:?}"
                      shift 2
                      ;;
                    --disable-build-servers)
                      disable_build_servers=1
                      shift
                      ;;
                    *)
                      shift
                      ;;
                  esac
                done
                printf 'build|%s|%s\n' "$configuration" "$disable_build_servers" >> "$ELSA_SECRETS_EF_RECORDING_LOG"
                """);
            WriteExecutableShim(shimRoot, "dotnet-ef", """
                #!/usr/bin/env bash
                set -euo pipefail

                [[ "${1:-}" == "migrations" && "${2:-}" == "has-pending-model-changes" ]] || exit 98
                shift 2
                context=""
                configuration=""
                no_build=0
                while [[ $# -gt 0 ]]; do
                  case "$1" in
                    --context)
                      context="${2:?}"
                      shift 2
                      ;;
                    --configuration)
                      configuration="${2:?}"
                      shift 2
                      ;;
                    --no-build)
                      no_build=1
                      shift
                      ;;
                    *)
                      shift
                      ;;
                  esac
                done
                printf 'ef|%s|%s|%s\n' "$context" "$configuration" "$no_build" >> "$ELSA_SECRETS_EF_RECORDING_LOG"
                printf 'No changes have been made to the model since the last migration.\n'
                """);
            var isolatedRoot = Path.Join(shimRoot, "repo");
            var isolatedScriptDirectory = Path.Join(isolatedRoot, "tools", "ef");
            Directory.CreateDirectory(isolatedScriptDirectory);
            File.Copy(
                RepoPath("tools", "ef", "dual-migrate.sh"),
                Path.Join(isolatedScriptDirectory, "dual-migrate.sh"));
            File.Copy(
                RepoPath("tools", "ef", "secrets-ef-lib.sh"),
                Path.Join(isolatedScriptDirectory, "secrets-ef-lib.sh"));
            var inheritedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var result = RunDualMigrate(
                ["pending"],
                extraEnv: new Dictionary<string, string?>
                {
                    ["ELSA_SECRETS_EF_CONFIGURATION"] = "Canary",
                    ["ELSA_SECRETS_EF_RECORDING_LOG"] = recordingLog,
                    ["PATH"] = $"{shimRoot}{Path.PathSeparator}{inheritedPath}"
                },
                rootOverride: isolatedRoot);

            Assert.True(result.ExitCode == 0, result.Describe());
            Assert.Equal(
                [
                    "build|Canary|1",
                    "ef|SecretsSqliteDbContext|Canary|1",
                    "ef|SecretsSqlServerDbContext|Canary|1",
                    "ef|SecretsPostgreSqlDbContext|Canary|1"
                ],
                File.ReadAllLines(recordingLog));
        }
        finally
        {
            Directory.Delete(shimRoot, recursive: true);
        }
    }

    [Fact]
    public void Dual_migrate_script_uses_the_module_assembly_nuplane_loads()
    {
        var lib = File.ReadAllText(RepoPath("tools", "ef", "secrets-ef-lib.sh"));
        Assert.Contains(
            "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj",
            lib,
            StringComparison.Ordinal);
        Assert.Contains(
            "src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling.csproj",
            lib,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--sqlserver", "ELSA_SECRETS_EF_SQLSERVER", null)]
    [InlineData("--sqlserver", "ELSA_SECRETS_EF_SQLSERVER", "   ")]
    [InlineData("--postgresql", "ELSA_SECRETS_EF_POSTGRESQL", null)]
    [InlineData("--postgresql", "ELSA_SECRETS_EF_POSTGRESQL", "\t")]
    public void Explicit_engine_apply_fails_when_the_connection_env_is_unset_or_whitespace(
        string selector,
        string envName,
        string? envValue)
    {
        var result = RunDualMigrate(["apply", selector], extraEnv: new Dictionary<string, string?>
        {
            [envName] = envValue,
            ["ELSA_SECRETS_EF_REQUIRE_ALL"] = null
        });
        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{envName} is required", result.Error + result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_command_exits_2()
    {
        var result = RunDualMigrate(["not-a-command"]);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("unknown command", result.Error + result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void Pending_is_clean_for_each_derived_context()
    {
        Skip.IfNot(HasDotnetEf(), "dotnet-ef is not available.");
        var result = RunDualMigrate(["pending"]);
        Assert.True(result.ExitCode == 0, result.Describe());
        Assert.Contains("has-pending-model-changes --context SecretsSqliteDbContext", result.Output, StringComparison.Ordinal);
        Assert.Contains("has-pending-model-changes --context SecretsSqlServerDbContext", result.Output, StringComparison.Ordinal);
        Assert.Contains("has-pending-model-changes --context SecretsPostgreSqlDbContext", result.Output, StringComparison.Ordinal);
        // Echo-only pending would still print the banners. The ef tool writes this line once per context.
        Assert.Equal(3, CountOccurrences(result.Output, "No changes have been made to the model since the last migration."));
    }

    [SkippableFact]
    public async Task Default_all_runs_pending_then_applies_sqlite()
    {
        Skip.IfNot(HasDotnetEf(), "dotnet-ef is not available.");
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-ef-dual-default-{Guid.NewGuid():N}.db");
        try
        {
            var result = RunDualMigrate(
                [],
                extraEnv: new Dictionary<string, string?>
                {
                    ["ELSA_SECRETS_EF_SQLITE"] = $"Data Source={path}",
                    ["ELSA_SECRETS_EF_SQLSERVER"] = null,
                    ["ELSA_SECRETS_EF_POSTGRESQL"] = null,
                    ["ELSA_SECRETS_EF_REQUIRE_ALL"] = null
                });
            Assert.True(result.ExitCode == 0, result.Describe());
            Assert.Equal(3, CountOccurrences(result.Output, "No changes have been made to the model since the last migration."));
            Assert.Contains("skip SecretsSqlServerDbContext", result.Output, StringComparison.Ordinal);
            Assert.True(await TableExistsAsync(path, SecretsEfModule.TableName));
        }
        finally
        {
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
        }
    }

    [SkippableFact]
    public async Task Apply_all_skips_missing_non_sqlite_engines_unless_required()
    {
        Skip.IfNot(HasDotnetEf(), "dotnet-ef is not available.");
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-ef-dual-all-{Guid.NewGuid():N}.db");
        try
        {
            var skipped = RunDualMigrate(
                ["apply", "--all"],
                extraEnv: new Dictionary<string, string?>
                {
                    ["ELSA_SECRETS_EF_SQLITE"] = $"Data Source={path}",
                    ["ELSA_SECRETS_EF_SQLSERVER"] = null,
                    ["ELSA_SECRETS_EF_POSTGRESQL"] = null,
                    ["ELSA_SECRETS_EF_REQUIRE_ALL"] = null
                });
            Assert.True(skipped.ExitCode == 0, skipped.Describe());
            Assert.Contains("skip SecretsSqlServerDbContext", skipped.Output, StringComparison.Ordinal);
            Assert.Contains("skip SecretsPostgreSqlDbContext", skipped.Output, StringComparison.Ordinal);
            Assert.True(await TableExistsAsync(path, SecretsEfModule.TableName));

            var required = RunDualMigrate(
                ["apply", "--all"],
                extraEnv: new Dictionary<string, string?>
                {
                    ["ELSA_SECRETS_EF_SQLITE"] = $"Data Source={path}",
                    ["ELSA_SECRETS_EF_SQLSERVER"] = null,
                    ["ELSA_SECRETS_EF_POSTGRESQL"] = null,
                    ["ELSA_SECRETS_EF_REQUIRE_ALL"] = "1"
                });
            Assert.Equal(1, required.ExitCode);
            Assert.Contains("ELSA_SECRETS_EF_SQLSERVER is required", required.Error + required.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
        }
    }

    [SkippableFact]
    public async Task Apply_sqlite_creates_the_secrets_schema()
    {
        Skip.IfNot(HasDotnetEf(), "dotnet-ef is not available.");
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-ef-dual-{Guid.NewGuid():N}.db");
        try
        {
            var result = RunDualMigrate(
                ["apply", "--sqlite"],
                extraEnv: new Dictionary<string, string?>
                {
                    ["ELSA_SECRETS_EF_SQLITE"] = $"Data Source={path}"
                });
            Assert.True(result.ExitCode == 0, result.Describe());
            Assert.Contains("database update --context SecretsSqliteDbContext", result.Output, StringComparison.Ordinal);
            Assert.True(await TableExistsAsync(path, SecretsEfModule.TableName));
            Assert.True(await TableExistsAsync(path, SecretsEfModule.HistoryTableName));
        }
        finally
        {
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
        }
    }

    private static bool HasDotnetEf() => DualMigrateProcessRunner.HasDotnetEf();

    private static DualMigrateProcessRunner.ScriptResult RunDualMigrate(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? extraEnv = null,
        string? rootOverride = null)
        => DualMigrateProcessRunner.Run(args, extraEnv, rootOverride);

    private static string WriteExecutableShim(string directory, string name, string contents)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Executable recording shims require Unix permissions.");

        var path = Path.Join(directory, name);
        File.WriteAllText(path, contents);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static async Task<bool> TableExistsAsync(string path, string table)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        return count == 1;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0)
                return count;
            count++;
            start = index + value.Length;
        }
    }

    private static string RepoPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return Path.Join([directory.FullName, ..segments]);
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

}
