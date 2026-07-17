using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.MongoDb;
using Groundwork.MongoDb.Documents;
using Groundwork.MongoDb.Materialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Elsa.Persistence.Groundwork.MongoDb;

/// <summary>
/// Admits the selected MongoDB target once during host preparation. By default, startup inspects
/// topology and schema only; enable <c>autoApplyOnStartup</c> to apply safe pending operations
/// automatically.
/// </summary>
public sealed class MongoDbGroundworkDocumentStoreInitializer : IHostedService, IShellInitializer
{
    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly bool _autoApplyOnStartup;
    private readonly GroundworkStoreSessionSource _sessionSource;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMongoDbGroundworkRuntimeAdmission _admission;
    private readonly ILogger<MongoDbGroundworkDocumentStoreInitializer> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public MongoDbGroundworkDocumentStoreInitializer(
        string connectionString,
        string databaseName,
        bool autoApplyOnStartup,
        GroundworkStoreSessionSource sessionSource,
        IServiceScopeFactory scopeFactory,
        ILogger<MongoDbGroundworkDocumentStoreInitializer> logger)
        : this(
            connectionString,
            databaseName,
            autoApplyOnStartup,
            sessionSource,
            scopeFactory,
            new MongoDbGroundworkRuntimeAdmission(),
            logger)
    {
    }

    internal MongoDbGroundworkDocumentStoreInitializer(
        string connectionString,
        string databaseName,
        bool autoApplyOnStartup,
        GroundworkStoreSessionSource sessionSource,
        IServiceScopeFactory scopeFactory,
        IMongoDbGroundworkRuntimeAdmission admission,
        ILogger<MongoDbGroundworkDocumentStoreInitializer> logger)
    {
        _connectionString = connectionString;
        _databaseName = databaseName;
        _autoApplyOnStartup = autoApplyOnStartup;
        _sessionSource = sessionSource;
        _scopeFactory = scopeFactory;
        _admission = admission;
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) =>
        EnsureInitializedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            var topology = await _admission.InspectReplicaSetAsync(
                _connectionString,
                _databaseName,
                cancellationToken);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var providerCapabilities = await GroundworkProviderCapabilitySnapshotBuilder.ForSelectedSourcesAsync(
                MongoDbGroundworkCapabilities.RuntimeForTransactionCapableDeployment(),
                topology,
                scope.ServiceProvider.GetServices<IGroundworkStorageManifestSource>(),
                cancellationToken);
            var source = await scope.ServiceProvider
                .GetRequiredService<GroundworkStorageCompositionFactory>()
                .CreateSourceAsync(
                    providerCapabilities,
                    MongoDbPhysicalNameNormalizer.Instance,
                    cancellationToken,
                    MongoDbGroundworkPhysicalSchemaTargetCompiler.Instance);

            if (!_sessionSource.IsInitialized)
            {
                await _admission.OpenAndPublishAsync(
                    _connectionString,
                    _databaseName,
                    source,
                    _sessionSource,
                    _autoApplyOnStartup,
                    _logger,
                    cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }
}

/// <summary>Inspects MongoDB topology and publishes a store only after exact runtime admission.</summary>
public interface IMongoDbGroundworkRuntimeAdmission
{
    ValueTask<GroundworkProviderTopologySnapshot> InspectReplicaSetAsync(
        string connectionString,
        string databaseName,
        CancellationToken cancellationToken);

    ValueTask OpenAndPublishAsync(
        string connectionString,
        string databaseName,
        GroundworkPhysicalSchemaManifestSource source,
        GroundworkStoreSessionSource sessionSource,
        bool autoApplyOnStartup,
        ILogger logger,
        CancellationToken cancellationToken);
}

/// <summary>The production MongoDB topology, transaction-probe and physical-target admission path.</summary>
public sealed class MongoDbGroundworkRuntimeAdmission : IMongoDbGroundworkRuntimeAdmission
{
    private const string ProviderIdentity = "groundwork-mongodb";
    private const string TopologyIdentity = "transaction-capable-replica-set";
    private const string ProbeCollection = "groundwork_replica_set_admission_probe";

    public async ValueTask<GroundworkProviderTopologySnapshot> InspectReplicaSetAsync(
        string connectionString,
        string databaseName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var configuredReplicaSet = MongoClientSettings
                .FromConnectionString(connectionString)
                .ReplicaSetName;
            if (string.IsNullOrWhiteSpace(configuredReplicaSet))
                throw UnsupportedTopology();

            using var client = new MongoClient(connectionString);
            var hello = await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
                new BsonDocument("hello", 1),
                cancellationToken: cancellationToken);
            var observedReplicaSet = hello.GetValue("setName", string.Empty).AsString;
            var isWritablePrimary = hello.GetValue("isWritablePrimary", false).ToBoolean();
            if (!isWritablePrimary ||
                string.IsNullOrWhiteSpace(observedReplicaSet) ||
                !string.Equals(configuredReplicaSet, observedReplicaSet, StringComparison.Ordinal))
            {
                throw UnsupportedTopology();
            }

            await VerifyTransactionRoundTripAsync(
                client,
                client.GetDatabase(databaseName),
                cancellationToken);
            return new GroundworkProviderTopologySnapshot(
                ProviderIdentity,
                TopologyIdentity,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "persistent-storage",
                    "independent-clients",
                    "multi-document-transactions",
                    "transaction-capable-mongodb",
                    TopologyIdentity
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnsupportedReplicaSetTopologyException exception)
        {
            throw new InvalidOperationException(exception.Message);
        }
        catch (Exception exception)
        {
            throw SanitizedFailure("replica-set topology inspection", exception);
        }
    }

    public async ValueTask OpenAndPublishAsync(
        string connectionString,
        string databaseName,
        GroundworkPhysicalSchemaManifestSource source,
        GroundworkStoreSessionSource sessionSource,
        bool autoApplyOnStartup,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sessionSource);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var handle = await MongoDbDocumentStoreFactory.OpenPhysicalAsync(
                connectionString,
                databaseName,
                source.CreateManifest(),
                source.PhysicalTarget.Provider,
                DocumentStoreAccess.Global,
                source.CreateNamePolicy(),
                options: new MongoDbPhysicalDocumentStoreOptions
                {
                    AutoApplyOnStartup = autoApplyOnStartup,
                    SchemaAdmissionLogger = logger
                },
                cancellationToken: cancellationToken);
            if (!IsExactTarget(source, handle))
            {
                await handle.DisposeAsync();
                throw new InvalidOperationException(
                    $"MongoDB Groundwork runtime admission opened a target that differs from the selected target '{source.TargetFingerprint}'.");
            }

            if (!sessionSource.TrySet((access, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var store = handle.CreateStore(access);
                return ValueTask.FromResult(new GroundworkStoreSessionResources(store, store));
            }, handle))
            {
                await handle.DisposeAsync();
                throw new InvalidOperationException(
                    "MongoDB Groundwork runtime admission could not publish the selected provider handle because another provider already initialized the session source.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception) when (
            exception.Message.StartsWith("MongoDB Groundwork runtime admission", StringComparison.Ordinal))
        {
            throw;
        }
        catch (Exception exception)
        {
            throw SanitizedFailure("runtime admission", exception);
        }
    }

    private static async Task VerifyTransactionRoundTripAsync(
        MongoClient client,
        IMongoDatabase database,
        CancellationToken cancellationToken)
    {
        using var session = await client.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction(new TransactionOptions(
            ReadConcern.Snapshot,
            ReadPreference.Primary,
            WriteConcern.WMajority));
        try
        {
            await database.GetCollection<BsonDocument>(ProbeCollection)
                .Find(session, Builders<BsonDocument>.Filter.Empty)
                .Limit(1)
                .AnyAsync(cancellationToken);
            await session.AbortTransactionAsync(cancellationToken);
        }
        catch
        {
            if (session.IsInTransaction)
            {
                try
                {
                    await session.AbortTransactionAsync(CancellationToken.None);
                }
                catch
                {
                    // Preserve the sanitized topology failure from the transaction probe.
                }
            }

            throw;
        }
    }

    private static bool IsExactTarget(
        GroundworkPhysicalSchemaManifestSource source,
        MongoDbPhysicalDocumentStoreOpenHandle handle) =>
        handle.Model.Target.ManifestIdentity == source.PhysicalTarget.ManifestIdentity &&
        handle.Model.Target.ManifestVersion == source.PhysicalTarget.ManifestVersion &&
        handle.Model.Target.Provider == source.PhysicalTarget.Provider &&
        string.Equals(handle.Model.Target.Fingerprint, source.TargetFingerprint, StringComparison.Ordinal);

    private const string UnsupportedTopologyMessage =
        "MongoDB Groundwork startup requires the configured and observed deployment to be the same writable replica set with working transactions; no store was opened.";

    private static UnsupportedReplicaSetTopologyException UnsupportedTopology() => new();

    private static InvalidOperationException SanitizedFailure(string operation, Exception exception) => new(
        $"MongoDB Groundwork {operation} failed ({exception.GetType().Name}); provider and connection details were suppressed.");

    private sealed class UnsupportedReplicaSetTopologyException()
        : InvalidOperationException(UnsupportedTopologyMessage);
}
