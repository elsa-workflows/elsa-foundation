using Elsa.Locking.Core;
using Elsa.Locking.Database;
using Medallion.Threading.MySql;
using Medallion.Threading.Postgres;
using Medallion.Threading.SqlServer;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Locking.Tests;

/// <summary>
/// The database lock feature's settings, without a database (#2192): which engines it accepts, that SQLite is refused with a
/// pointer back to the file-system lock, and where its connection comes from. Taking locks is in Database/ProviderTests.
/// </summary>
public sealed class DatabaseLockingFeatureTests
{
    private readonly Dictionary<string, string?> _configuration = new();

    [Theory]
    [InlineData("PostgreSql", "Host=localhost;Database=elsa")]
    [InlineData("postgres", "Host=localhost;Database=elsa")]
    [InlineData("SqlServer", "Server=localhost;Database=elsa")]
    [InlineData("mssql", "Server=localhost;Database=elsa")]
    [InlineData("MySql", "Server=localhost;Database=elsa")]
    public void Composes_a_lock_provider_for_each_supported_engine(string provider, string connectionString)
    {
        var locks = Resolve(new DatabaseLockingFeature { Provider = provider, ConnectionString = connectionString });

        Assert.NotNull(locks);
    }

    [Theory]
    [InlineData("PostgreSql", typeof(PostgresDistributedSynchronizationProvider), "Host=localhost;Database=elsa")]
    [InlineData("postgres", typeof(PostgresDistributedSynchronizationProvider), "Host=localhost;Database=elsa")]
    [InlineData(" Npgsql ", typeof(PostgresDistributedSynchronizationProvider), "Host=localhost;Database=elsa")]
    [InlineData("SqlServer", typeof(SqlDistributedSynchronizationProvider), "Server=localhost;Database=elsa")]
    [InlineData("sql server", typeof(SqlDistributedSynchronizationProvider), "Server=localhost;Database=elsa")]
    [InlineData("mssql", typeof(SqlDistributedSynchronizationProvider), "Server=localhost;Database=elsa")]
    [InlineData("MySql", typeof(MySqlDistributedSynchronizationProvider), "Server=localhost;Database=elsa")]
    [InlineData("my sql", typeof(MySqlDistributedSynchronizationProvider), "Server=localhost;Database=elsa")]
    public void Maps_each_provider_name_and_alias_to_its_engine(string provider, Type engine, string connectionString)
    {
        var locks = Resolve(new DatabaseLockingFeature { Provider = provider, ConnectionString = connectionString });

        // The adapter keeps the engine's provider in its primary-constructor field (the compiler names it <inner>P); nothing else exposes which engine was chosen.
        var inner = typeof(MedallionDistributedLockProvider)
            .GetField("<inner>P", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(locks);
        Assert.IsType(engine, inner);
    }

    [Fact]
    public void Refuses_sqlite_as_single_node_and_points_at_the_file_system_lock()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Configure(new DatabaseLockingFeature { Provider = "Sqlite" }));

        Assert.Contains("single-node by definition", failure.Message);
        Assert.Contains("FileSystemDistributedLocking", failure.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Oracle")]
    public void Refuses_a_missing_or_unknown_engine_when_the_shell_is_built(string? provider)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Configure(new DatabaseLockingFeature { Provider = provider }));

        Assert.Contains("PostgreSql, SqlServer or MySql", failure.Message);
    }

    [Fact]
    public void Falls_back_to_the_shared_elsa_connection()
    {
        _configuration["ConnectionStrings:Elsa"] = "Host=localhost;Database=elsa";

        Assert.NotNull(Resolve(new DatabaseLockingFeature { Provider = "PostgreSql" }));
    }

    [Fact]
    public void Refuses_a_named_connection_that_is_missing_rather_than_falling_back()
    {
        _configuration["ConnectionStrings:Elsa"] = "Host=localhost;Database=elsa";

        var failure = Assert.Throws<InvalidOperationException>(() =>
            Resolve(new DatabaseLockingFeature { Provider = "PostgreSql", ConnectionName = "Locks" }));

        Assert.Contains("'Locks'", failure.Message);
    }

    [Fact]
    public void Refuses_to_start_without_any_connection()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Resolve(new DatabaseLockingFeature { Provider = "PostgreSql" }));

        Assert.Contains("ConnectionStrings:Elsa", failure.Message);
    }

    private IServiceCollection Configure(DatabaseLockingFeature feature)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build());
        feature.ConfigureServices(services);
        return services;
    }

    private IDistributedLockProvider Resolve(DatabaseLockingFeature feature) =>
        Configure(feature).BuildServiceProvider().GetRequiredService<IDistributedLockProvider>();
}
