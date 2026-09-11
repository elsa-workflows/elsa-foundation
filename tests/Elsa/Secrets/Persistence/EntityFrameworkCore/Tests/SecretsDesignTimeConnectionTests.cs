using Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling;
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

    private static string UniqueVariable() =>
        $"ELSA_SECRETS_EF_DESIGN_TIME_TEST_{Guid.NewGuid():N}";
}
