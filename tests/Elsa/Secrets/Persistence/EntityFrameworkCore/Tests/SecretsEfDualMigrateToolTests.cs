using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Keeps the out-of-process dual-migrate script pointed at the derived contexts in this module
/// (the assembly Nuplane loads at apply time).
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
