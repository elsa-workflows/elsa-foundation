using Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

public sealed class SecretsDesignTimeConnectionTests
{
    [Fact]
    public void Resolve_rejects_invalid_arguments()
    {
        Assert.Throws<ArgumentException>(() => SecretsDesignTimeConnection.Resolve("", "Data Source=x"));
        Assert.Throws<ArgumentException>(() => SecretsDesignTimeConnection.Resolve("   ", "Data Source=x"));
        Assert.Throws<ArgumentException>(() =>
            SecretsDesignTimeConnection.Resolve(SecretsDesignTimeConnection.SqliteVariable, ""));
        Assert.Throws<ArgumentException>(() =>
            SecretsDesignTimeConnection.Resolve(SecretsDesignTimeConnection.SqliteVariable, "   "));
        Assert.ThrowsAny<ArgumentException>(() =>
            SecretsDesignTimeConnection.Resolve(null!, "Data Source=x"));
        Assert.ThrowsAny<ArgumentException>(() =>
            SecretsDesignTimeConnection.Resolve(SecretsDesignTimeConnection.SqliteVariable, null!));
    }

    [Fact]
    public void Resolve_returns_fallback_when_the_environment_value_is_unset_or_whitespace()
    {
        var name = UniqueVariable();
        try
        {
            Environment.SetEnvironmentVariable(name, null);
            Assert.Equal("Data Source=fallback.db", SecretsDesignTimeConnection.Resolve(name, "Data Source=fallback.db"));

            Environment.SetEnvironmentVariable(name, "   ");
            Assert.Equal("Data Source=fallback.db", SecretsDesignTimeConnection.Resolve(name, "Data Source=fallback.db"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Resolve_returns_the_configured_environment_value()
    {
        var name = UniqueVariable();
        try
        {
            Environment.SetEnvironmentVariable(name, "Server=localhost;Database=elsa-secrets;");
            Assert.Equal(
                "Server=localhost;Database=elsa-secrets;",
                SecretsDesignTimeConnection.Resolve(name, "Data Source=fallback.db"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Variable_names_match_the_dual_migrate_hooks()
    {
        Assert.Equal("ELSA_SECRETS_EF_SQLITE", SecretsDesignTimeConnection.SqliteVariable);
        Assert.Equal("ELSA_SECRETS_EF_SQLSERVER", SecretsDesignTimeConnection.SqlServerVariable);
        Assert.Equal("ELSA_SECRETS_EF_POSTGRESQL", SecretsDesignTimeConnection.PostgreSqlVariable);
    }

    [Fact]
    public void Sqlite_factory_uses_the_environment_connection()
    {
        using var _ = Override(SecretsDesignTimeConnection.SqliteVariable, "Data Source=factory-env.db");
        using var context = new SecretsSqliteDesignTimeFactory().CreateDbContext([]);
        Assert.Equal("Data Source=factory-env.db", context.Database.GetConnectionString());
    }

    [Fact]
    public void SqlServer_factory_uses_the_environment_connection()
    {
        using var _ = Override(SecretsDesignTimeConnection.SqlServerVariable, "Server=ci;Database=elsa-secrets;");
        using var context = new SecretsSqlServerDesignTimeFactory().CreateDbContext([]);
        Assert.Equal("Server=ci;Database=elsa-secrets;", context.Database.GetConnectionString());
    }

    [Fact]
    public void PostgreSql_factory_uses_the_environment_connection()
    {
        using var _ = Override(SecretsDesignTimeConnection.PostgreSqlVariable, "Host=ci;Database=elsa-secrets;");
        using var context = new SecretsPostgreSqlDesignTimeFactory().CreateDbContext([]);
        Assert.Equal("Host=ci;Database=elsa-secrets;", context.Database.GetConnectionString());
    }

    private static string UniqueVariable() =>
        $"ELSA_SECRETS_EF_DESIGN_TIME_TEST_{Guid.NewGuid():N}";

    private static EnvironmentOverride Override(string name, string value) => new(name, value);

    private sealed class EnvironmentOverride : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentOverride(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
