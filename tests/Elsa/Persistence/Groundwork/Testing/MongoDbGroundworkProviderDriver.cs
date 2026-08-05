using System.Diagnostics;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.MongoDb;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Groundwork.MongoDb;
using Groundwork.MongoDb.Documents;
using Groundwork.MongoDb.Materialization;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using ElsaRuntimeSchemaAdmissionResult = Elsa.Persistence.Groundwork.Unified.Composition.GroundworkRuntimeSchemaAdmissionResult;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Transaction-capable MongoDB replica-set mechanics for the shared Groundwork fixture.</summary>
public sealed class MongoDbGroundworkProviderDriver : GroundworkProviderDriver, IGroundworkTopologyRejectionProbe
{
    private const string ProviderKey = "mongodb";
    private const string Image = "mongo:7.0.24";
    private const string ReplicaSetName = "rs0";
    private const string ProtocolVersion = "1.0.0";
    private readonly MongoDbContainer _container = new MongoDbBuilder(Image)
        .WithReplicaSet(ReplicaSetName)
        .WithCommand("--setParameter", "enableTestCommands=1")
        .WithStartupCallback(EnsureReplicaSetInitializedAsync)
        .Build();
    private readonly string _databaseName = $"elsa_groundwork_driver_{Guid.NewGuid():N}";
    private readonly GroundworkProcessProbeRunner _processProbeRunner = new();
    private readonly GroundworkProcessLaunchDescriptor _processLaunchDescriptor;
    private string? _connectionString;
    private GroundworkPhysicalSchemaManifestSource? _physicalSource;

    public MongoDbGroundworkProviderDriver() =>
        _processLaunchDescriptor = _processProbeRunner.CreateLaunchDescriptor(ProtocolVersion);

    private static readonly string PackageVersion =
        GroundworkProviderDriverSupport.PackageVersion(typeof(MongoDbDocumentStoreFactory).Assembly);

    public override GroundworkProviderDescriptor Descriptor { get; } = new(
        "mongodb",
        "groundwork-mongodb",
        PackageVersion,
        new GroundworkProviderTopology(
            "mongodb",
            "transaction-capable-replica-set",
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions |
            GroundworkTopologyCapabilities.TransactionCapableMongoTopology |
            GroundworkTopologyCapabilities.ExternalProcessRestart));

    public override GroundworkTopologyCapabilities RequiredTopology =>
        GroundworkTopologyCapabilities.PersistentStorage |
        GroundworkTopologyCapabilities.IndependentClients |
        GroundworkTopologyCapabilities.MultiDocumentTransactions |
        GroundworkTopologyCapabilities.TransactionCapableMongoTopology |
        GroundworkTopologyCapabilities.ExternalProcessRestart;

    public override GroundworkCompositionFingerprint CompositionFingerprint { get; } =
        GroundworkCompositionFingerprint.Create("elsa-runtime-provider-fixture:v1");
    public override string? PhysicalTargetFingerprint => _physicalSource?.PhysicalTarget.Fingerprint;

    public override GroundworkProcessLaunchDescriptor ProcessLaunchDescriptor => _processLaunchDescriptor;

    public override string ProbeDocumentKind => ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind;

    public async ValueTask<GroundworkSanitizedEvidence> CaptureTopologyRejectionAsync(
        CancellationToken cancellationToken = default)
    {
        await using var standalone = new MongoDbBuilder(Image).Build();
        try
        {
            await standalone.StartAsync(cancellationToken);
        }
        catch (DotNet.Testcontainers.Builders.DockerUnavailableException)
        {
            // Rethrown unchanged so a host without Docker is skippable rather than a hard failure. The
            // generic catch below deliberately suppresses connection details; this exception reports that
            // the Docker daemon is unreachable and carries none, and the container has not started yet so
            // there is no connection string to leak. Fixtures' catch (DockerUnavailableException) skip
            // paths were unreachable while this was swallowed.
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"MongoDB standalone topology probe failed to start ({exception.GetType().Name}); provider output was suppressed.");
        }

        MongoDbDocumentStoreHandle? handle = null;
        try
        {
            handle = await MongoDbDocumentStoreFactory.CreateAsync(
                standalone.GetConnectionString(),
                $"elsa_groundwork_standalone_{Guid.NewGuid():N}",
                ElsaRuntimeStorageManifest.Create(),
                new ProviderIdentity("groundwork-mongodb", PackageVersion),
                GroundworkTestAccess.DefaultScoped,
                cancellationToken: cancellationToken);
            try
            {
                await ProbeTransactionAsync(handle.Store, cancellationToken);
            }
            catch (GroundworkProviderTopologyException)
            {
                return StandaloneRejectedEvidence();
            }
            catch (UnsupportedAtomicCommitException)
            {
                return StandaloneRejectedEvidence();
            }

            throw new InvalidOperationException(
                "MongoDB standalone topology unexpectedly admitted a multi-document transaction.");
        }
        catch (UnsupportedAtomicCommitException)
        {
            return StandaloneRejectedEvidence();
        }
        catch (GroundworkProviderTopologyException)
        {
            return StandaloneRejectedEvidence();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MongoException exception)
        {
            throw SanitizedProviderFailure("standalone-topology-probe", exception);
        }
        finally
        {
            if (handle is not null)
                await handle.DisposeAsync();
        }
    }

    protected override async ValueTask InitializeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _container.StartAsync(cancellationToken);
            _connectionString = new MongoUrlBuilder(_container.GetConnectionString())
            {
                ReplicaSetName = ReplicaSetName
            }.ToString();
            await ValidateTransactionTopologyAsync(cancellationToken);
        }
        catch (DotNet.Testcontainers.Builders.DockerUnavailableException)
        {
            // Rethrown unchanged so a host without Docker is skippable rather than a hard failure, matching
            // SqlServerGroundworkProviderDriver. The generic catch below suppresses connection details; this
            // exception reports an unreachable Docker daemon and carries none. While it was swallowed, every
            // fixture's catch (DockerUnavailableException) skip path was unreachable.
            await CleanupFailedInitializationAsync();
            throw;
        }
        catch (OperationCanceledException)
        {
            await CleanupFailedInitializationAsync();
            throw;
        }
        catch (GroundworkProviderTopologyException)
        {
            await CleanupFailedInitializationAsync();
            throw;
        }
        catch (Exception exception)
        {
            await CleanupFailedInitializationAsync();
            throw new InvalidOperationException(
                $"MongoDB provider startup failed ({exception.GetType().Name}); connection details were suppressed.");
        }
    }

    protected override async ValueTask ResetCoreAsync(CancellationToken cancellationToken)
    {
        using var client = new MongoClient(RequiredConnectionString());
        await client.DropDatabaseAsync(_databaseName, cancellationToken);
        _physicalSource = null;
    }

    protected override async ValueTask ResetPhysicalCoreAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource>? manifestSources,
        CancellationToken cancellationToken)
    {
        await ResetPhysicalStorageCoreAsync(cancellationToken);
        var topology = await new MongoDbGroundworkRuntimeAdmission().InspectReplicaSetAsync(
            RequiredConnectionString(),
            _databaseName,
            cancellationToken);
        var source = await CreatePhysicalSchemaSourceAsync(manifestSources, topology, cancellationToken);
        await ApplyPhysicalSchemaCoreAsync(source, cancellationToken);
    }

    protected override async ValueTask ResetPhysicalStorageCoreAsync(CancellationToken cancellationToken)
    {
        using var client = new MongoClient(RequiredConnectionString());
        await client.DropDatabaseAsync(_databaseName, cancellationToken);
    }

    protected override async ValueTask ApplyPhysicalSchemaCoreAsync(
        GroundworkPhysicalSchemaManifestSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var client = new MongoClient(RequiredConnectionString());
        var executor = new MongoDbPhysicalSchemaExecutor(client.GetDatabase(_databaseName));
        var applied = await PhysicalSchemaApplication.ApplyAsync(
            source.PhysicalTarget,
            executor,
            cancellationToken: cancellationToken);
        EnsureSchemaApplied(applied);
        var admission = await source.InspectRuntimeAdmissionAsync(executor, cancellationToken: cancellationToken);
        if (!admission.IsReady)
            throw new InvalidOperationException("MongoDB physical provider driver did not admit its applied runtime target.");
        _physicalSource = source;
    }

    protected override ValueTask<GroundworkProviderClient> OpenClientCoreAsync(
        Guid clientId,
        CancellationToken cancellationToken) =>
        OpenClientCoreAsync(
            clientId,
            ElsaRuntimeStorageManifest.Create(),
            GroundworkTestAccess.DefaultScoped,
            cancellationToken: cancellationToken);

    protected override async ValueTask<GroundworkProviderClient> OpenClientCoreAsync(
        Guid clientId,
        StorageManifest manifest,
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        // The string factory constructs a new MongoClient for every handle. Two open driver clients are
        // therefore backed by separate driver clusters as well as separate Groundwork store adapters.
        var handle = await MongoDbDocumentStoreFactory.CreateAsync(
            RequiredConnectionString(),
            _databaseName,
            manifest,
            new ProviderIdentity("groundwork-mongodb", PackageVersion),
            access,
            cancellationToken: cancellationToken);
        var services = new ServiceCollection()
            .AddSingleton(handle.Store)
            .AddSingleton<IDocumentStore>(handle.Store)
            .BuildServiceProvider();

        return new GroundworkProviderClient(
            clientId,
            services,
            handle.Store,
            async () =>
            {
                try
                {
                    await services.DisposeAsync();
                }
                finally
                {
                    await handle.DisposeAsync();
                }
            });
    }

    protected override async ValueTask<GroundworkProviderClient> OpenPhysicalClientCoreAsync(
        Guid clientId,
        DocumentStoreAccess access,
        CancellationToken cancellationToken)
    {
        var source = _physicalSource ?? throw new InvalidOperationException("The MongoDB physical target has not been applied.");
        var handle = await MongoDbDocumentStoreFactory.OpenPhysicalAsync(
            RequiredConnectionString(),
            _databaseName,
            source.CreateManifest(),
            source.PhysicalTarget.Provider,
            access,
            source.CreateNamePolicy(),
            cancellationToken: cancellationToken);
        if (!IsExactTarget(source, handle))
        {
            await handle.DisposeAsync();
            throw new InvalidOperationException("The MongoDB physical provider driver opened a target different from the applied runtime target.");
        }

        var store = handle.CreateStore(access);
        var services = new ServiceCollection()
            .AddSingleton<IDocumentStore>(store)
            .AddSingleton<IBoundedDocumentStore>(store)
            .BuildServiceProvider();
        return new GroundworkProviderClient(
            clientId,
            services,
            store,
            async () =>
            {
                try
                {
                    await services.DisposeAsync();
                }
                finally
                {
                    await handle.DisposeAsync();
                }
            },
            store);
    }

    private static ValueTask<GroundworkPhysicalSchemaManifestSource> CreatePhysicalSchemaSourceAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource>? manifestSources,
        GroundworkProviderTopologySnapshot topology,
        CancellationToken cancellationToken)
    {
        var capabilityReport = MongoDbGroundworkCapabilities.RuntimeForTransactionCapableDeployment();
        return manifestSources is null
            ? GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
                capabilityReport,
                topology,
                MongoDbPhysicalNameNormalizer.Instance,
                MongoDbGroundworkPhysicalSchemaTargetCompiler.Instance,
                cancellationToken)
            : GroundworkStoreInitialization.CreatePhysicalSchemaSourceAsync(
                capabilityReport,
                topology,
                MongoDbPhysicalNameNormalizer.Instance,
                manifestSources,
                MongoDbGroundworkPhysicalSchemaTargetCompiler.Instance,
                cancellationToken);
    }

    protected override ValueTask<GroundworkProcessProbeResult> RunInNewProcessCoreAsync(
        GroundworkProcessProbeRequest request,
        CancellationToken cancellationToken) =>
        _processProbeRunner.RunAsync(
            ProcessLaunchDescriptor,
            Descriptor,
            ProbeDocumentKind,
            new GroundworkProcessProbeState(RequiredConnectionString(), _databaseName),
            request,
            cancellationToken: cancellationToken);

    protected override async ValueTask<GroundworkSanitizedEvidence> CaptureDiagnosticsCoreAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new MongoClient(RequiredConnectionString());
            var admin = client.GetDatabase("admin");
            var hello = await admin.RunCommandAsync<BsonDocument>(
                new BsonDocument("hello", 1),
                cancellationToken: cancellationToken);
            var buildInfo = await admin.RunCommandAsync<BsonDocument>(
                new BsonDocument("buildInfo", 1),
                cancellationToken: cancellationToken);
            var setName = hello.GetValue("setName", "missing").AsString;
            var writable = hello.GetValue("isWritablePrimary", false).ToBoolean();
            var engineVersion = buildInfo.GetValue("version", "unknown").AsString;
            return GroundworkSanitizedEvidence.Create(
                "diagnostics",
                $"provider:mongodb\ntopology:transaction-capable-replica-set\nreplica-set:{setName}\nwritable-primary:{writable.ToString().ToLowerInvariant()}\nengine-version:{engineVersion}\nstate:ready");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MongoException exception)
        {
            throw SanitizedProviderFailure("diagnostics", exception);
        }
    }

    protected override async ValueTask<GroundworkNativePlanEvidence> CaptureNativePlanCoreAsync(
        GroundworkExecutionPath executionPath,
        string scenarioId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new MongoClient(RequiredConnectionString());
            var database = client.GetDatabase(_databaseName);
            var collectionName = MongoDbGroundworkNames.CollectionName(ProbeDocumentKind);
            var plan = await database.RunCommandAsync<BsonDocument>(
                new BsonDocument
                {
                    ["explain"] = new BsonDocument
                    {
                        ["find"] = collectionName,
                        ["filter"] = new BsonDocument(),
                        ["limit"] = 1
                    },
                    ["verbosity"] = "queryPlanner"
                },
                cancellationToken: cancellationToken);
            var stages = new SortedSet<string>(StringComparer.Ordinal);
            var indexes = new SortedSet<string>(StringComparer.Ordinal);
            CollectPlanSummary(plan, stages, indexes);
            if (stages.Count == 0)
                throw new InvalidOperationException("MongoDB explain evidence contained no query-planner stages.");

            var evidence = GroundworkSanitizedEvidence.Create(
                "native-plan",
                $"evidence-class:substrate-only-plan-smoke\nadmitted-route-proof:false\nprovider:mongodb\nformat:query-planner\nstages:{string.Join(',', stages)}\nindexes:{string.Join(',', indexes)}\nbound:limit-1");
            return GroundworkNativePlanEvidence.Create(executionPath, scenarioId, evidence);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MongoException exception)
        {
            throw SanitizedProviderFailure("native-plan", exception);
        }
    }

    protected override async ValueTask PrepareNativeRoutePlanDatasetCoreAsync(
        IReadOnlyCollection<GroundworkNativeRoutePlanRequest> requests,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new MongoClient(RequiredConnectionString());
            var database = client.GetDatabase(_databaseName);
            foreach (var group in requests.GroupBy(request => (request.PhysicalName, request.DocumentKind)))
                await EnsureNativeRouteDatasetAsync(database, group.ToArray(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MongoException exception)
        {
            throw SanitizedProviderFailure("identity-native-route-dataset", exception);
        }
    }

    protected override async ValueTask<GroundworkNativeRoutePlanResult> CaptureNativeRoutePlanCoreAsync(
        GroundworkNativeRoutePlanRequest request,
        PhysicalDocumentQueryExplanation explanation,
        int materializedCandidateCount,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new MongoClient(RequiredConnectionString());
            var database = client.GetDatabase(_databaseName);
            var collection = database.GetCollection<BsonDocument>(request.PhysicalName);
            var expectedIndex = await ResolveNativeRouteIndexAsync(collection, request, cancellationToken);
            var routeIndexes = (await ListNativeRouteIndexesAsync(collection, cancellationToken))
                .Where(index => !string.Equals(index, "_id_", StringComparison.Ordinal))
                .ToArray();
            var commands = explanation.Commands.Select((command, ordinal) =>
            {
                if (!string.Equals(command.NativePlanFormat, "mongodb-json", StringComparison.Ordinal))
                    throw new InvalidOperationException($"MongoDB route '{request.QueryIdentity}' returned an unexpected native-plan format.");
                var plan = BsonDocument.Parse(command.NativePlan);
                var stages = new SortedSet<string>(StringComparer.Ordinal);
                var indexes = new SortedSet<string>(StringComparer.Ordinal);
                CollectPlanSummary(plan, stages, indexes);
                var expectedIndexes = command.Kind == PhysicalDocumentQueryCommandKind.PrimaryHydration
                    ? ["_id_"]
                    : routeIndexes;
                if (!indexes.Intersect(expectedIndexes, StringComparer.Ordinal).Any() ||
                    !stages.Contains("IXSCAN") ||
                    stages.Contains("COLLSCAN"))
                {
                    throw new InvalidOperationException(
                        $"MongoDB route '{request.QueryIdentity}' command '{command.Identity}' was not scan-free through an admitted route index.");
                }
                return GroundworkNativeRouteCommandEvidence.Create(
                    ordinal,
                    command,
                    "index-scan",
                    indexes);
            }).ToArray();
            var selectedIndex = GroundworkNativeRoutePlanResult.SelectCompiledIndex(
                request,
                routeIndexes,
                commands);
            var cardinality = await collection.CountDocumentsAsync(
                FilterDefinition<BsonDocument>.Empty,
                cancellationToken: cancellationToken);
            return GroundworkNativeRoutePlanResult.Create(
                request,
                ProviderKey,
                cardinality,
                "index-scan",
                selectedIndex,
                request.Limit,
                materializedCandidateCount,
                commands);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MongoException exception)
        {
            throw SanitizedProviderFailure("identity-native-route-plan", exception);
        }
    }

    protected override async ValueTask<GroundworkPhysicalSchemaManifestSource> PrepareSchemaParityCoreAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource> manifestSources,
        CancellationToken cancellationToken)
    {
        using var client = new MongoClient(RequiredConnectionString());
        await client.DropDatabaseAsync(_databaseName, cancellationToken);
        _physicalSource = null;
        var topology = await new MongoDbGroundworkRuntimeAdmission().InspectReplicaSetAsync(
            RequiredConnectionString(),
            _databaseName,
            cancellationToken);
        return await CreatePhysicalSchemaSourceAsync(manifestSources, topology, cancellationToken);
    }

    protected override async ValueTask<GroundworkProviderSchemaStateSnapshot> CaptureSchemaStateCoreAsync(
        CancellationToken cancellationToken)
    {
        using var client = new MongoClient(RequiredConnectionString());
        var database = client.GetDatabase(_databaseName);
        var items = new List<string>();
        using var collections = await database.ListCollectionsAsync(cancellationToken: cancellationToken);
        var collectionDocuments = await collections.ToListAsync(cancellationToken);
        foreach (var collectionDocument in collectionDocuments.OrderBy(
                     document => document.GetValue("name", string.Empty).AsString,
                     StringComparer.Ordinal))
        {
            var collectionName = collectionDocument.GetValue("name", string.Empty).AsString;
            if (string.IsNullOrWhiteSpace(collectionName) || collectionName.StartsWith("system.", StringComparison.Ordinal))
                continue;
            items.Add($"collection\u001f{collectionName}\u001f{CanonicalBson(collectionDocument)}");
            var collection = database.GetCollection<BsonDocument>(collectionName);
            using (var indexes = await collection.Indexes.ListAsync(cancellationToken))
            {
                var indexDocuments = await indexes.ToListAsync(cancellationToken);
                items.AddRange(indexDocuments.Select(index =>
                    $"index\u001f{collectionName}\u001f{CanonicalBson(index)}"));
            }
            var documents = await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(cancellationToken);
            items.AddRange(documents.Select(document =>
                $"document\u001f{collectionName}\u001f{CanonicalBson(document)}"));
        }
        return GroundworkProviderSchemaStateSnapshot.Create(ProviderKey, items);
    }

    protected override async ValueTask<ElsaRuntimeSchemaAdmissionResult> InspectSchemaParityAdmissionCoreAsync(
        GroundworkPhysicalSchemaManifestSource source,
        CancellationToken cancellationToken)
    {
        using var client = new MongoClient(RequiredConnectionString());
        return await source.InspectRuntimeAdmissionAsync(
            new MongoDbPhysicalSchemaExecutor(client.GetDatabase(_databaseName)),
            cancellationToken: cancellationToken);
    }

    protected override SchemaToolConnection SchemaToolConnectionCore() =>
        new(RequiredConnectionString(), _databaseName);

    protected override async ValueTask DisposeCoreAsync()
    {
        try
        {
            await _container.DisposeAsync();
        }
        finally
        {
            _connectionString = null;
        }
    }

    private async Task ValidateTransactionTopologyAsync(CancellationToken cancellationToken)
    {
        var connectionString = RequiredConnectionString();
        using var firstClient = new MongoClient(connectionString);
        using var secondClient = new MongoClient(connectionString);
        var firstHello = await HelloAsync(firstClient, cancellationToken);
        var secondHello = await HelloAsync(secondClient, cancellationToken);
        if (!IsExpectedPrimary(firstHello) || !IsExpectedPrimary(secondHello))
            throw UnsupportedTopology();

        MongoDbDocumentStoreHandle? firstHandle = null;
        MongoDbDocumentStoreHandle? secondHandle = null;
        try
        {
            firstHandle = await MongoDbDocumentStoreFactory.CreateAsync(
                connectionString,
                _databaseName,
                ElsaRuntimeStorageManifest.Create(),
                new ProviderIdentity("groundwork-mongodb", PackageVersion),
                GroundworkTestAccess.DefaultScoped,
                cancellationToken: cancellationToken);
            secondHandle = await MongoDbDocumentStoreFactory.CreateAsync(
                connectionString,
                _databaseName,
                ElsaRuntimeStorageManifest.Create(),
                new ProviderIdentity("groundwork-mongodb", PackageVersion),
                GroundworkTestAccess.DefaultScoped,
                cancellationToken: cancellationToken);
            if (ReferenceEquals(firstHandle.Store, secondHandle.Store))
                throw UnsupportedTopology();

            await ProbeTransactionAsync(firstHandle.Store, cancellationToken);
            await ProbeTransactionAsync(secondHandle.Store, cancellationToken);
        }
        catch (UnsupportedAtomicCommitException)
        {
            throw UnsupportedTopology();
        }
        finally
        {
            try
            {
                if (secondHandle is not null)
                    await secondHandle.DisposeAsync();
            }
            finally
            {
                if (firstHandle is not null)
                    await firstHandle.DisposeAsync();
            }
        }
    }

    private async ValueTask CleanupFailedInitializationAsync()
    {
        _connectionString = null;
        try
        {
            await _container.DisposeAsync();
        }
        catch
        {
            // Preserve the sanitized startup failure or caller cancellation.
        }
    }

    private static InvalidOperationException SanitizedProviderFailure(string operation, MongoException exception) =>
        new($"MongoDB {operation} failed ({exception.GetType().Name}); provider output was suppressed.");

    private async Task ProbeTransactionAsync(
        IDocumentStore store,
        CancellationToken cancellationToken)
    {
        if (store.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
            throw UnsupportedTopology();
        var sentinelId = $"transaction-rollback-{Guid.NewGuid():N}";
        await using var unitOfWork = await store.BeginAsync(
            DocumentCommitScope.Of(ProbeDocumentKind),
            cancellationToken);
        var saved = await unitOfWork.SaveAsync(
            new SaveDocumentRequest(
                ProbeDocumentKind,
                sentinelId,
                ProtocolVersion,
                "{\"value\":\"transaction-rollback-probe\"}",
                ExpectedVersion: 0),
            cancellationToken);
        if (saved.Status != DocumentStoreWriteStatus.Saved)
            throw UnsupportedTopology();
        await unitOfWork.RollbackAsync(cancellationToken);
        if (await store.LoadAsync(ProbeDocumentKind, sentinelId, cancellationToken) is not null)
            throw UnsupportedTopology();
    }

    private static async Task<BsonDocument> HelloAsync(
        MongoClient client,
        CancellationToken cancellationToken) =>
        await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
            new BsonDocument("hello", 1),
            cancellationToken: cancellationToken);

    private static bool IsExpectedPrimary(BsonDocument hello) =>
        string.Equals(hello.GetValue("setName", string.Empty).AsString, ReplicaSetName, StringComparison.Ordinal) &&
        hello.GetValue("isWritablePrimary", false).ToBoolean();

    private static GroundworkProviderTopologyException UnsupportedTopology() => new(
        "mongodb",
        "transaction-capable-replica-set",
        GroundworkTopologyCapabilities.MultiDocumentTransactions |
        GroundworkTopologyCapabilities.TransactionCapableMongoTopology);

    private static GroundworkSanitizedEvidence StandaloneRejectedEvidence() =>
        GroundworkSanitizedEvidence.Create(
            "topology-rejection",
            "provider=mongodb\nobserved-topology=standalone\nrequired-topology=replica-set-or-sharded\noutcome=rejected");

    private static async Task EnsureReplicaSetInitializedAsync(
        MongoDbContainer container,
        CancellationToken cancellationToken)
    {
        const string script = """
            const hello = db.adminCommand({hello: 1});
            if (hello.ok === 1 && hello.setName === "rs0") quit(0);
            const result = rs.initiate({_id: "rs0", members: [{_id: 0, host: "127.0.0.1:27017"}]});
            quit(result.ok === 1 || result.code === 23 ? 0 : 1);
            """;
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromMinutes(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await container.ExecScriptAsync(script, cancellationToken);
                if (result.ExitCode == 0)
                    return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // MongoDB has not accepted authenticated commands yet; retry without retaining raw output.
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
        throw new InvalidOperationException("MongoDB replica-set admission timed out; provider output was suppressed.");
    }

    private static void CollectPlanSummary(
        BsonValue value,
        ISet<string> stages,
        ISet<string> indexes)
    {
        if (value is BsonDocument document)
        {
            if (document.TryGetValue("stage", out var stage) && stage.IsString)
                stages.Add(stage.AsString);
            if (document.TryGetValue("indexName", out var index) && index.IsString)
                indexes.Add(index.AsString);
            foreach (var element in document)
                CollectPlanSummary(element.Value, stages, indexes);
        }
        else if (value is BsonArray array)
        {
            foreach (var item in array)
                CollectPlanSummary(item, stages, indexes);
        }
    }

    private static string CanonicalBson(BsonValue value) => value.ToJson(new JsonWriterSettings
    {
        OutputMode = JsonOutputMode.CanonicalExtendedJson,
        Indent = false
    });

    private static async Task EnsureNativeRouteDatasetAsync(
        IMongoDatabase database,
        IReadOnlyCollection<GroundworkNativeRoutePlanRequest> requests,
        CancellationToken cancellationToken)
    {
        var dataset = GroundworkNativeRouteDataset.Create(requests);
        var lookup = database.GetCollection<BsonDocument>(dataset.PhysicalName);
        var primary = dataset.PrimaryPhysicalName is null
            ? lookup
            : database.GetCollection<BsonDocument>(dataset.PrimaryPhysicalName);
        await lookup.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken);
        if (dataset.PrimaryPhysicalName is not null)
        {
            await primary.DeleteManyAsync(
                Builders<BsonDocument>.Filter.Eq("document_kind", dataset.DocumentKind),
                cancellationToken);
        }
        const int batchSize = 5_000;
        for (var offset = 0; offset < dataset.AcceptanceCardinality; offset += batchSize)
        {
            var count = Math.Min(batchSize, dataset.AcceptanceCardinality - offset);
            var primaryDocuments = new List<BsonDocument>(count);
            var lookupDocuments = dataset.PrimaryPhysicalName is null
                ? primaryDocuments
                : new List<BsonDocument>(count);
            for (var index = 0; index < count; index++)
            {
                var ordinal = offset + index;
                var id = ordinal == 0 ? dataset.CandidateDocumentId : $"native-{ordinal:D6}";
                var storageScope = ordinal == 1 ? dataset.CrossScope : dataset.StorageScope;
                var comparisonKey = ordinal == 0 ? dataset.CandidateComparisonKey : $"noise-comparison-{ordinal:D6}";
                var lookupKey = ordinal == 0 ? dataset.CandidateLookupKey : $"noise-lookup-{ordinal:D6}";
                var content = ordinal == 0
                    ? BsonDocument.Parse(dataset.CandidateContentJson)
                    : new BsonDocument();
                var incarnation = $"native-{ordinal:D32}";
                var primaryId = dataset.PrimaryPhysicalName is null
                    ? new BsonDocument
                    {
                        ["storage_scope"] = storageScope,
                        ["id_lookup_key"] = lookupKey
                    }
                    : new BsonDocument
                    {
                        ["document_kind"] = dataset.DocumentKind,
                        ["storage_scope"] = storageScope,
                        ["id_lookup_key"] = lookupKey
                    };
                var document = new BsonDocument
                {
                    ["_id"] = primaryId,
                    ["document_kind"] = dataset.DocumentKind,
                    ["storage_scope"] = storageScope,
                    ["id"] = id,
                    ["id_comparison_key"] = comparisonKey,
                    ["id_lookup_key"] = lookupKey,
                    ["schema_version"] = dataset.CandidateSchemaVersion,
                    ["version"] = 1L,
                    [dataset.ContentColumn] = content.DeepClone(),
                    ["_groundwork_content"] = content,
                    ["_groundwork_incarnation"] = incarnation,
                    ["_groundwork_created_at"] = DateTime.UnixEpoch,
                    ["_groundwork_updated_at"] = DateTime.UnixEpoch
                };
                primaryDocuments.Add(document);

                var projectedDocument = dataset.PrimaryPhysicalName is null
                    ? document
                    : new BsonDocument
                    {
                        ["_id"] = new BsonDocument
                        {
                            ["document_kind"] = dataset.DocumentKind,
                            ["storage_scope"] = storageScope,
                            ["document_id_lookup_key"] = lookupKey
                        },
                        ["document_kind"] = dataset.DocumentKind,
                        ["storage_scope"] = storageScope,
                        ["document_id"] = id,
                        ["document_id_comparison_key"] = comparisonKey,
                        ["document_id_lookup_key"] = lookupKey,
                        ["_groundwork_primary_version"] = 1L,
                        ["_groundwork_incarnation"] = incarnation
                    };
                foreach (var projectedField in dataset.ProjectedValues.Values)
                {
                    var varies = dataset.VaryingProjectedFields.Contains(projectedField.Field, StringComparer.Ordinal);
                    var isPredicate = dataset.PredicateFields.Contains(projectedField.Field);
                    projectedDocument[projectedField.Field] = ordinal <= dataset.MatchingCardinality
                        ? NativeMatchingBsonValue(
                            projectedField,
                            ordinal,
                            varies && dataset.MatchingCardinality > 1)
                        : varies || !isPredicate
                            ? NativeNoiseBsonValue(projectedField, ordinal)
                            : NativeNoiseBsonValue(projectedField);
                }
                if (dataset.PrimaryPhysicalName is not null)
                    lookupDocuments.Add(projectedDocument);
            }

            await primary.InsertManyAsync(
                primaryDocuments,
                new InsertManyOptions { IsOrdered = false },
                cancellationToken);
            if (dataset.PrimaryPhysicalName is not null)
            {
                await lookup.InsertManyAsync(
                    lookupDocuments,
                    new InsertManyOptions { IsOrdered = false },
                    cancellationToken);
            }
        }

        var cardinality = await lookup.CountDocumentsAsync(
            FilterDefinition<BsonDocument>.Empty,
            cancellationToken: cancellationToken);
        if (cardinality != dataset.AcceptanceCardinality)
            throw new InvalidOperationException(
                $"MongoDB seeded {cardinality} documents in '{dataset.PhysicalName}', expected {dataset.AcceptanceCardinality}.");
    }

    private static async Task<string> ResolveNativeRouteIndexAsync(
        IMongoCollection<BsonDocument> collection,
        GroundworkNativeRoutePlanRequest request,
        CancellationToken cancellationToken)
    {
        using var cursor = await collection.Indexes.ListAsync(cancellationToken);
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var index in cursor.Current)
            {
                var name = index.GetValue("name", string.Empty).AsString;
                if (string.Equals(name, request.ExpectedIndexName, StringComparison.Ordinal))
                    return name;
                var keys = index.GetValue("key", new BsonDocument()).AsBsonDocument.Elements.ToArray();
                var expected = new[] { "storage_scope" }.Concat(request.IndexFields).ToArray();
                if (keys.Select(key => key.Name).Take(expected.Length).SequenceEqual(expected, StringComparer.Ordinal))
                    return name;
            }
        }

        throw new InvalidOperationException(
            $"MongoDB route '{request.QueryIdentity}' has no physical index prefix ({string.Join(", ", new[] { "storage_scope" }.Concat(request.IndexFields))}).");
    }

    private static async Task<IReadOnlyList<string>> ListNativeRouteIndexesAsync(
        IMongoCollection<BsonDocument> collection,
        CancellationToken cancellationToken)
    {
        var indexes = new List<string>();
        using var cursor = await collection.Indexes.ListAsync(cancellationToken);
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            indexes.AddRange(cursor.Current.Select(index =>
                index.GetValue("name", string.Empty).AsString));
        }
        return indexes;
    }

    private static BsonValue NativeBsonValue(GroundworkNativeRouteProjectedValue value) =>
        value.Kind == GroundworkNativeRouteProjectedValueKind.DateTime
            ? new BsonInt64(((DateTimeOffset)value.ToProviderValue()).UtcTicks)
            : BsonValue.Create(value.ToProviderValue());

    private static BsonValue NativeMatchingBsonValue(
        GroundworkNativeRouteProjectedValue value,
        int ordinal,
        bool varies)
    {
        if (!varies)
            return NativeBsonValue(value);
        return value.Kind switch
        {
            GroundworkNativeRouteProjectedValueKind.String => $"{value.Value}-{ordinal:D6}",
            GroundworkNativeRouteProjectedValueKind.Int64 => (long)value.ToProviderValue() + ordinal,
            GroundworkNativeRouteProjectedValueKind.DateTime =>
                new BsonInt64(((DateTimeOffset)value.ToProviderValue()).UtcTicks + ordinal),
            _ => throw new InvalidOperationException(
                $"Projected field '{value.Field}' cannot vary uniquely at acceptance scale.")
        };
    }

    private static BsonValue NativeNoiseBsonValue(
        GroundworkNativeRouteProjectedValue value) =>
        value.Kind == GroundworkNativeRouteProjectedValueKind.DateTime
            ? new BsonInt64(((DateTimeOffset)value.ToProviderNoiseValue()).UtcTicks)
            : BsonValue.Create(value.ToProviderNoiseValue());

    private static BsonValue NativeNoiseBsonValue(
        GroundworkNativeRouteProjectedValue value,
        int ordinal) =>
        value.Kind switch
        {
            GroundworkNativeRouteProjectedValueKind.String => $"noise-{ordinal:D6}",
            GroundworkNativeRouteProjectedValueKind.Boolean => !((bool)value.ToProviderValue()),
            GroundworkNativeRouteProjectedValueKind.Int64 => (long)value.ToProviderValue() + ordinal + 1L,
            GroundworkNativeRouteProjectedValueKind.DateTime =>
                new BsonInt64(((DateTimeOffset)value.ToProviderValue()).UtcTicks + ordinal + 1L),
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Kind,
                "Unknown native-route projected value kind.")
        };

    /// <summary>
    /// Opens a new MongoDB client/database handle against the driver's current database, for callers that
    /// need to read the physical per-document-kind collections directly rather than through the
    /// <see cref="IDocumentStore"/> abstraction (for example, a read-model data source that runs its own
    /// aggregation pipelines).
    /// </summary>
    public IMongoDatabase CreateRawDatabase() => new MongoClient(RequiredConnectionString()).GetDatabase(_databaseName);

    private string RequiredConnectionString() => _connectionString ??
        throw new InvalidOperationException("The MongoDB provider target has not been initialized.");

    private static bool IsExactTarget(
        GroundworkPhysicalSchemaManifestSource source,
        MongoDbPhysicalDocumentStoreOpenHandle handle) =>
        handle.Model.Target.ManifestIdentity == source.PhysicalTarget.ManifestIdentity &&
        handle.Model.Target.ManifestVersion == source.PhysicalTarget.ManifestVersion &&
        handle.Model.Target.Provider == source.PhysicalTarget.Provider &&
        StringComparer.Ordinal.Equals(handle.Model.Target.Fingerprint, source.TargetFingerprint);

    private static void EnsureSchemaApplied(PhysicalSchemaApplicationResult result)
    {
        if (result.Outcome is not (PhysicalSchemaApplicationOutcome.Applied or PhysicalSchemaApplicationOutcome.NoChanges))
            throw new InvalidOperationException($"MongoDB physical schema application did not complete: {result.Outcome}.");
    }

}
