using Npgsql;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests;

public sealed class PostgreSqlDesignProviderFixtureTests
{
    [Fact]
    public void Per_design_connection_strings_disable_pooling()
    {
        var connectionString = PostgreSqlDesignProviderFixture.CreateDesignConnectionString(
            "Host=localhost;Database=postgres;Username=postgres;Password=postgres",
            "elsa_design_test");
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal("elsa_design_test", parsed.Database);
        Assert.False(parsed.Pooling);
    }
}
