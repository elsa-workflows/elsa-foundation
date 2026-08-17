using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Providers;
using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Providers.Tests;

public sealed class GroundworkProviderRegistrationTests
{
    [Fact]
    public void Composition_assembly_references_only_the_public_v2_groundwork_surface()
    {
        var references = typeof(GroundworkProviderRegistration).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name is "Groundwork.Core" or "Groundwork.Documents");
        Assert.Contains(references, reference => reference.Name == "Groundwork.Store");
        Assert.Contains(references, reference => reference.Name == "Groundwork.MongoDb");
        Assert.Contains(references, reference => reference.Name == "Groundwork.PostgreSql");
        Assert.Contains(references, reference => reference.Name == "Groundwork.Sqlite");
        Assert.Contains(references, reference => reference.Name == "Groundwork.SqlServer");
    }

    [Fact]
    public void Default_target_resolves_the_same_connection_from_ordinary_and_keyed_DI()
    {
        var path = TemporaryDatabasePath();
        try
        {
            using var provider = new ServiceCollection()
                .AddGroundworkSqliteProvider($"Data Source={path}")
                .BuildServiceProvider();

            var ordinary = provider.GetRequiredService<IStorageProviderConnection>();
            var keyed = provider.GetRequiredKeyedService<IStorageProviderConnection>("default");

            Assert.Same(ordinary, keyed);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void Named_target_is_keyed_only_and_does_not_fall_back_to_default()
    {
        var path = TemporaryDatabasePath();
        try
        {
            using var provider = new ServiceCollection()
                .AddGroundworkSqliteProvider($"Data Source={path}", "authoring")
                .BuildServiceProvider();

            Assert.Null(provider.GetService<IStorageProviderConnection>());
            Assert.NotNull(provider.GetRequiredKeyedService<IStorageProviderConnection>("authoring"));
            Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredKeyedService<IStorageProviderConnection>("default"));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void Duplicate_target_registration_is_refused_during_composition()
    {
        var services = new ServiceCollection();

        services.AddGroundworkSqliteProvider("Data Source=:memory:", "runtime");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddGroundworkSqliteProvider("Data Source=:memory:", "runtime"));

        Assert.Contains("runtime", exception.Message, StringComparison.Ordinal);
        Assert.Contains("v2 provider connection", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_provider_owns_and_disposes_the_provider_connection()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var provider = new ServiceCollection()
                .AddGroundworkSqliteProvider($"Data Source={path}")
                .BuildServiceProvider();

            var connection = provider.GetRequiredService<IStorageProviderConnection>();
            provider.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                connection.OpenSession(ProviderCompositionUnit(), StorageAccess.Scoped(new StorageScope("tenant-a"))));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void SQLite_provider_admits_a_schema_and_scoped_session_through_the_public_connection()
    {
        var path = TemporaryDatabasePath();
        try
        {
            using var provider = new ServiceCollection()
                .AddGroundworkSqliteProvider($"Data Source={path}")
                .BuildServiceProvider();
            var connection = provider.GetRequiredService<IStorageProviderConnection>();
            var unit = ProviderCompositionUnit();

            connection.Schema.Apply(unit);
            var session = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("tenant-a")));
            var values = new StorageValues(new Dictionary<string, object?>
            {
                ["id"] = "row-1",
                ["value"] = "admitted"
            });

            var outcome = session.Insert(values, WriteOptions.Unconditional);

            Assert.True(outcome.Succeeded);
            Assert.NotNull(session.Read(new StorageKey(new Dictionary<string, object?>
            {
                ["id"] = "row-1"
            })));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task SQLite_host_startup_admits_units_and_persists_across_fresh_provider_composition()
    {
        var path = TemporaryDatabasePath();
        var unit = ProviderCompositionUnit();
        var access = StorageAccess.Scoped(new StorageScope("tenant-a"));
        var key = new StorageKey(new Dictionary<string, object?>
        {
            ["id"] = "row-1"
        });
        try
        {
            await using (var first = BuildSqliteHost(path, unit))
            {
                await first.GetRequiredService<IHostedService>().StartAsync(CancellationToken.None);
                var sessions = first.GetRequiredService<IGroundworkStorageSessionSource>();
                var outcome = sessions.Open(unit.Id.Value, access).Insert(
                    new StorageValues(new Dictionary<string, object?>
                    {
                        ["id"] = "row-1",
                        ["value"] = "survives-recomposition"
                    }),
                    WriteOptions.Unconditional);

                Assert.Equal(WriteOutcomeStatus.Inserted, outcome.Status);
            }

            await using (var second = BuildSqliteHost(path, unit))
            {
                await second.GetRequiredService<IHostedService>().StartAsync(CancellationToken.None);
                var sessions = second.GetRequiredService<IGroundworkStorageSessionSource>();
                var entry = sessions.Open(unit.Id.Value, access).Read(key);

                Assert.NotNull(entry);
                Assert.Equal("survives-recomposition", entry!.Values.Values["value"]);
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void Storage_session_source_reuses_sessions_per_unit_target_and_access_context()
    {
        var path = TemporaryDatabasePath();
        var unit = ProviderCompositionUnit();
        try
        {
            using var provider = BuildSqliteHost(path, unit);
            var sessions = provider.GetRequiredService<IGroundworkStorageSessionSource>();
            var first = sessions.Open(
                unit.Id.Value,
                StorageAccess.Scoped(new StorageScope("tenant-a")));

            for (var attempt = 0; attempt < 1_000; attempt++)
            {
                Assert.Same(
                    first,
                    sessions.Open(
                        unit.Id.Value,
                        StorageAccess.Scoped(new StorageScope("tenant-a"))));
            }

            Assert.NotSame(
                first,
                sessions.Open(
                    unit.Id.Value,
                    StorageAccess.Scoped(new StorageScope("tenant-b"))));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task Host_startup_refuses_a_declared_target_without_a_provider_connection()
    {
        await using var provider = new ServiceCollection()
            .AddGroundworkStorageUnit(ProviderCompositionUnit(), "missing-target")
            .BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<IHostedService>().StartAsync(CancellationToken.None));

        Assert.Contains("missing-target", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no v2 provider connection", exception.Message, StringComparison.Ordinal);
    }

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public void Configured_external_provider_resolves_through_its_public_registration(string providerName)
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            $"Set {EnvironmentVariable(providerName)} to run the {providerName} provider proof.");

        using var provider = AddProvider(new ServiceCollection(), providerName, connectionString!).BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredKeyedService<IStorageProviderConnection>("default"));
    }

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"groundwork-provider-composition-{Guid.NewGuid():N}.db");

    private static StorageUnit ProviderCompositionUnit() =>
        StorageUnit.Declare("provider_composition", "provider_composition")
            .String("id", 64, column => column.Required())
            .String("value", 128)
            .Key("id")
            .Scoped()
            .Build();

    private static ServiceProvider BuildSqliteHost(string path, StorageUnit unit) =>
        new ServiceCollection()
            .AddGroundworkSqliteProvider($"Data Source={path}")
            .AddGroundworkStorageUnit(unit)
            .BuildServiceProvider();

    private static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal", $"{path}.schema.lock" })
            if (File.Exists(candidate))
                File.Delete(candidate);
    }

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private static IServiceCollection AddProvider(
        IServiceCollection services,
        string providerName,
        string connectionString) =>
        providerName switch
        {
            "postgresql" => services.AddGroundworkPostgreSqlProvider(connectionString),
            "sqlserver" => services.AddGroundworkSqlServerProvider(connectionString),
            "mongodb" => services.AddGroundworkMongoDbProvider(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };
}
