using Elsa.Persistence.Groundwork.Diagnostics;
using Elsa.Persistence.Groundwork.Extensions;
using Elsa.Persistence.Groundwork.Providers;
using Elsa.Persistence.Groundwork.Secrets;
using Elsa.Tasks.Core;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Sqlite.Materialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class MaterializeGroundworkStartupTaskTests
{
    [Fact]
    public async Task StartupTaskMaterializesSecretsManifestWithSqliteProvider()
    {
        await using var harness = GroundworkBridgeHarness.Create(options =>
            options.Manifests.Add(SecretsGroundworkManifestFactory.Create()));

        await harness.ExecuteStartupTask();

        var snapshot = harness.GetDiagnosticsSnapshot();
        Assert.Contains(snapshot.Materializations, record =>
            record.ManifestIdentity == "elsa.secrets" &&
            record.ProviderName == "groundwork-sqlite" &&
            record.Status == GroundworkMaterializationStatus.Succeeded);
        Assert.True(await harness.SchemaHistoryContains("elsa.secrets", "groundwork-sqlite"));
    }

    [Fact]
    public async Task StartupTaskRecordsSkippedMaterializationWhenDisabled()
    {
        var provider = new CountingGroundworkProvider();
        await using var harness = GroundworkBridgeHarness.Create(options =>
        {
            options.Manifests.Add(SecretsGroundworkManifestFactory.Create());
            options.MaterializeOnStartup = false;
        }, provider);

        await harness.ExecuteStartupTask();

        var snapshot = harness.GetDiagnosticsSnapshot();
        Assert.Equal(0, provider.MaterializeCount);
        Assert.Contains(snapshot.Materializations, record =>
            record.ManifestIdentity == "elsa.secrets" &&
            record.Status == GroundworkMaterializationStatus.Skipped);
    }

    [Fact]
    public async Task StartupTaskRecordsSkippedMaterializationWhenDisabledAndNoProvidersAreRegistered()
    {
        await using var harness = GroundworkBridgeHarness.Create(options =>
        {
            options.Manifests.Add(SecretsGroundworkManifestFactory.Create());
            options.MaterializeOnStartup = false;
        }, registerSqliteProvider: false);

        await harness.ExecuteStartupTask();

        var snapshot = harness.GetDiagnosticsSnapshot();
        Assert.Contains(snapshot.Materializations, record =>
            record.ManifestIdentity == "elsa.secrets" &&
            record.ProviderName is null &&
            record.Status == GroundworkMaterializationStatus.Skipped);
        Assert.DoesNotContain(snapshot.Materializations, record =>
            record.ManifestIdentity == "elsa.secrets" &&
            record.Status == GroundworkMaterializationStatus.ProviderUnavailable);
    }

    [Fact]
    public async Task StartupTaskRecordsUnavailableProviderWhenNoProvidersAreRegistered()
    {
        await using var harness = GroundworkBridgeHarness.Create(
            options => options.Manifests.Add(SecretsGroundworkManifestFactory.Create()),
            registerSqliteProvider: false);

        await harness.ExecuteStartupTask();

        var snapshot = harness.GetDiagnosticsSnapshot();
        Assert.Contains(snapshot.Materializations, record =>
            record.ManifestIdentity == "elsa.secrets" &&
            record.Status == GroundworkMaterializationStatus.ProviderUnavailable);
    }

    [Fact]
    public async Task StartupTaskRecordsRemainingManifestsBeforeRethrowingMaterializationFailures()
    {
        var provider = new FailingGroundworkProvider("elsa.secrets");
        await using var harness = GroundworkBridgeHarness.Create(options =>
        {
            options.Manifests.Add(SecretsGroundworkManifestFactory.Create());
            options.Manifests.Add(SecretsGroundworkManifestFactory.Create() with
            {
                Identity = new StorageManifestIdentity("elsa.secrets.after-failure")
            });
        }, provider);

        var exception = await Assert.ThrowsAsync<AggregateException>(harness.ExecuteStartupTask);
        var snapshot = harness.GetDiagnosticsSnapshot();

        Assert.Single(exception.InnerExceptions);
        Assert.Contains(snapshot.Materializations, record =>
            record.ManifestIdentity == "elsa.secrets" &&
            record.ProviderName == "failing-provider" &&
            record.Status == GroundworkMaterializationStatus.Failed);
        Assert.Contains(snapshot.Materializations, record =>
            record.ManifestIdentity == "elsa.secrets.after-failure" &&
            record.ProviderName == "failing-provider" &&
            record.Status == GroundworkMaterializationStatus.Succeeded);
    }

    private sealed class GroundworkBridgeHarness : IAsyncDisposable
    {
        private readonly ServiceProvider serviceProvider;

        private GroundworkBridgeHarness(ServiceProvider serviceProvider, SqliteConnection connection)
        {
            this.serviceProvider = serviceProvider;
            Connection = connection;
        }

        private SqliteConnection Connection { get; }

        public static GroundworkBridgeHarness Create(
            Action<Elsa.Persistence.Groundwork.Options.GroundworkPersistenceOptions> configure,
            IGroundworkPersistenceProvider? provider = null,
            bool registerSqliteProvider = true)
        {
            var services = new ServiceCollection();
            var connection = new SqliteConnection("Data Source=:memory:");
            services.AddLogging();
            services.AddSingleton(connection);
            services.AddElsaGroundworkPersistence(configure);

            if (provider is not null)
                services.AddSingleton(provider);
            else if (registerSqliteProvider)
                services.AddSingleton<IGroundworkPersistenceProvider, SqliteGroundworkProvider>();

            return new GroundworkBridgeHarness(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), connection);
        }

        public GroundworkPersistenceDiagnosticSnapshot GetDiagnosticsSnapshot()
        {
            using var scope = serviceProvider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<GroundworkPersistenceDiagnostics>().GetSnapshot();
        }

        public async Task ExecuteStartupTask()
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var startupTask = scope.ServiceProvider
                .GetServices<IStartupTask>()
                .OfType<Elsa.Persistence.Groundwork.Tasks.MaterializeGroundworkStartupTask>()
                .Single();
            await startupTask.ExecuteAsync(CancellationToken.None);
        }

        public async Task<bool> SchemaHistoryContains(string manifestId, string providerName)
        {
            await using var command = Connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM groundwork_schema_history
                WHERE manifest_id = $manifestId AND provider_name = $providerName;
                """;
            command.Parameters.AddWithValue("$manifestId", manifestId);
            command.Parameters.AddWithValue("$providerName", providerName);
            return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
        }

        public async ValueTask DisposeAsync()
        {
            await serviceProvider.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class SqliteGroundworkProvider(SqliteConnection connection) : IGroundworkPersistenceProvider
    {
        public ProviderIdentity Identity { get; } = new("groundwork-sqlite", "1.0.0");

        public Task MaterializeAsync(StorageManifest manifest, CancellationToken cancellationToken = default) =>
            new SqliteGroundworkMaterializer(connection).MaterializeAsync(manifest, Identity, cancellationToken);
    }

    private sealed class CountingGroundworkProvider : IGroundworkPersistenceProvider
    {
        public int MaterializeCount { get; private set; }
        public ProviderIdentity Identity { get; } = new("counting-provider", "1.0.0");

        public Task MaterializeAsync(StorageManifest manifest, CancellationToken cancellationToken = default)
        {
            MaterializeCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingGroundworkProvider(string failingManifestIdentity) : IGroundworkPersistenceProvider
    {
        public ProviderIdentity Identity { get; } = new("failing-provider", "1.0.0");

        public Task MaterializeAsync(StorageManifest manifest, CancellationToken cancellationToken = default)
        {
            if (manifest.Identity.Value == failingManifestIdentity)
                throw new InvalidOperationException("Synthetic materialization failure.");

            return Task.CompletedTask;
        }
    }
}
