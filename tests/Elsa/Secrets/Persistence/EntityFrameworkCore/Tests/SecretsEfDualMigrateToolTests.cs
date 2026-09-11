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

    [Fact]
    public void Explicit_sqlserver_apply_fails_when_the_connection_env_is_unset()
    {
        var result = RunDualMigrate(["apply", "--sqlserver"], extraEnv: new Dictionary<string, string?>
        {
            ["ELSA_SECRETS_EF_SQLSERVER"] = null,
            ["ELSA_SECRETS_EF_REQUIRE_ALL"] = null
        });
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ELSA_SECRETS_EF_SQLSERVER is required", result.Error + result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_postgresql_apply_fails_when_the_connection_env_is_unset()
    {
        var result = RunDualMigrate(["apply", "--postgresql"], extraEnv: new Dictionary<string, string?>
        {
            ["ELSA_SECRETS_EF_POSTGRESQL"] = null,
            ["ELSA_SECRETS_EF_REQUIRE_ALL"] = null
        });
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ELSA_SECRETS_EF_POSTGRESQL is required", result.Error + result.Output, StringComparison.Ordinal);
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
            return _dotnetEf = true;

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
                return _dotnetEf = false;
            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return _dotnetEf = false;
            }

            return _dotnetEf = process.ExitCode == 0;
        }
        catch (Exception)
        {
            return _dotnetEf = false;
        }
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
        catch (Exception)
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
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(180_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"dual-migrate.sh {string.Join(' ', args)} did not exit within 180s.");
        }

        return new ScriptResult(process.ExitCode, output, error);
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
