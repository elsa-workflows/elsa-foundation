using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Distributed.Services;
using Groundwork.Core.Capabilities;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.PhysicalStorage;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

/// <summary>
/// One placement store + transport pair per provider flavor, so the contract-parity suites run the SAME behavioral
/// assertions against the leaf's in-memory implementations, the Groundwork bridges over the shared in-memory
/// document-store double, and the Groundwork bridges over the real SQLite provider.
/// </summary>
internal sealed class DistributedStoreHarness(
    IExecutionPlacementStore placementStore,
    IExecutionCommandTransport transport,
    IAsyncDisposable? owner = null) : IAsyncDisposable
{
    public const string InMemory = "in-memory";
    public const string GroundworkMemory = "groundwork-memory";
    public const string GroundworkSqlite = "groundwork-sqlite";

    private static readonly ProviderIdentity SqliteProvider = new("groundwork-sqlite", "1.0.0");

    public IExecutionPlacementStore PlacementStore { get; } = placementStore;
    public IExecutionCommandTransport Transport { get; } = transport;

    public static async Task<DistributedStoreHarness> CreateAsync(string provider)
    {
        switch (provider)
        {
            case InMemory:
                return new DistributedStoreHarness(new InMemoryExecutionPlacementStore(), new InMemoryExecutionCommandTransport());
            case GroundworkMemory:
                return FromDocumentStore(new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create()));
            case GroundworkSqlite:
                var database = new TemporarySqliteDatabase();
                var manifest = DistributedGroundworkStorageManifest.Create();
                var target = PhysicalSchemaTargetCompiler.Compile(
                    manifest,
                    SqliteProvider,
                    SqliteGroundworkCapabilities.PhysicalNames);
                var store = await SqliteDocumentStoreFactory.OpenPhysicalAsync(
                    database.ConnectionString,
                    manifest,
                    SqliteProvider,
                    GroundworkTestAccess.DefaultScoped,
                    options: new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });
                var queries = new GroundworkBoundedDocumentStoreRouter(
                    target.Routes.Select(route =>
                        KeyValuePair.Create<string, IBoundedDocumentStore>(
                            route.StorageUnit.Value,
                            SqlitePhysicalQueryRuntime.Create(
                                store,
                                manifest,
                                route,
                                target.Provider))));
                return new DistributedStoreHarness(
                    new GroundworkExecutionPlacementStore(store, queries),
                    new GroundworkExecutionCommandTransport(
                        store,
                        GroundworkDistributedTestAccess.Scoped(),
                        queries),
                    database);
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
    }

    public static DistributedStoreHarness FromDocumentStore(IDocumentStore documentStore, IAsyncDisposable? owner = null)
    {
        var queries = documentStore as IBoundedDocumentStore ?? throw new ArgumentException(
            "The distributed Groundwork harness requires a document store that executes bounded routes.",
            nameof(documentStore));
        return new DistributedStoreHarness(
            new GroundworkExecutionPlacementStore(documentStore, queries),
            new GroundworkExecutionCommandTransport(
                documentStore,
                GroundworkDistributedTestAccess.Scoped(),
                queries),
            owner);
    }

    public static WorkflowExecutionCommandEnvelope Envelope(
        string executionId,
        string envelopeId,
        DateTimeOffset now,
        string partition = PersistenceScope.DefaultValue)
    {
        var command = new WorkflowExecutionCommand(
            CommandId: $"cmd-{envelopeId}",
            WorkflowExecutionId: executionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: now,
            Payload: null,
            Metadata: new Dictionary<string, string>());

        return new WorkflowExecutionCommandEnvelope(
            envelopeId: envelopeId,
            workflowExecutionId: executionId,
            command: command,
            idempotencyKey: $"idem-{envelopeId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now,
            partition: new WorkflowExecutionPartition(partition));
    }

    public async ValueTask DisposeAsync()
    {
        if (owner is not null)
            await owner.DisposeAsync();
    }

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"elsa-distributed-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={_path}";

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }
}
