using System.Diagnostics;
using System.Text;
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
        Assert.Contains("--configuration", script, StringComparison.Ordinal);
        Assert.Contains("--no-build", script, StringComparison.Ordinal);
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

    private static bool? _dotnetEf;

    private static bool HasDotnetEf()
    {
        if (_dotnetEf is { } cached)
            return cached;

        var root = RepoPath();
        if (File.Exists(Path.Join(root, ".tools", "dotnet-ef")))
            return Remember(true);

        TryRestoreDotnetTools(root);
        var start = new ProcessStartInfo("dotnet", ["ef", "--version"])
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        CopyDotnetEnvironment(start.Environment);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return Remember(false);
            if (!process.WaitForExit(60_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException exception)
                {
                    Debug.WriteLine(exception);
                }

                return Remember(false);
            }

            return Remember(process.ExitCode == 0);
        }
        catch (InvalidOperationException)
        {
            return Remember(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Remember(false);
        }
    }

    private static bool Remember(bool value)
    {
        _dotnetEf = value;
        return value;
    }

    private static void TryRestoreDotnetTools(string root)
    {
        var start = new ProcessStartInfo("dotnet", ["tool", "restore"])
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        CopyDotnetEnvironment(start.Environment);
        try
        {
            using var process = Process.Start(start);
            process?.WaitForExit(60_000);
        }
        catch (InvalidOperationException)
        {
            // pending / apply --sqlite then skip.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // pending / apply --sqlite then skip.
        }
    }

    private static ScriptResult RunDualMigrate(IReadOnlyList<string> args, IReadOnlyDictionary<string, string?>? extraEnv = null)
    {
        var root = RepoPath();
        var script = Path.Join(root, "tools", "ef", "dual-migrate.sh");
        var start = new ProcessStartInfo("bash")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(script);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        CopyDotnetEnvironment(start.Environment);
        if (extraEnv is not null)
        {
            foreach (var (key, value) in extraEnv)
            {
                if (value is null)
                    start.Environment.Remove(key);
                else
                    start.Environment[key] = value;
            }
        }

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("bash could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw new TimeoutException($"dual-migrate.sh {string.Join(' ', args)} did not exit within 180s.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        return new ScriptResult(process.ExitCode, output, error);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process may already have exited; cleanup is best-effort.
        }
    }

    private static void CopyDotnetEnvironment(IDictionary<string, string?> environment)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
            environment["DOTNET_ROOT"] = dotnetRoot;
        environment["PATH"] = path;
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

    private sealed record ScriptResult(int ExitCode, string Output, string Error)
    {
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
}
