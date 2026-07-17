using System.Diagnostics;
using Elsa.Persistence.Groundwork;
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
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Transaction-capable MongoDB replica-set mechanics for the shared Groundwork fixture.</summary>
public sealed class MongoDbGroundworkProviderDriver : GroundworkProviderDriver, IGroundworkTopologyRejectionProbe
{
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

    protected override async ValueTask ResetPhysicalCoreAsync(CancellationToken cancellationToken)
    {
        using var client = new MongoClient(RequiredConnectionString());
        await client.DropDatabaseAsync(_databaseName, cancellationToken);
        var topology = await new MongoDbGroundworkRuntimeAdmission().InspectReplicaSetAsync(
            RequiredConnectionString(),
            _databaseName,
            cancellationToken);
        var source = await GroundworkStoreInitialization.CreateRuntimePhysicalSchemaSourceAsync(
            MongoDbGroundworkCapabilities.RuntimeForTransactionCapableDeployment(),
            topology,
            MongoDbPhysicalNameNormalizer.Instance,
            MongoDbGroundworkPhysicalSchemaTargetCompiler.Instance,
            cancellationToken);
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
            cancellationToken);

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
        CancellationToken cancellationToken)
    {
        var source = _physicalSource ?? throw new InvalidOperationException("The MongoDB physical target has not been applied.");
        var handle = await MongoDbDocumentStoreFactory.OpenPhysicalAsync(
            RequiredConnectionString(),
            _databaseName,
            source.CreateManifest(),
            source.PhysicalTarget.Provider,
            GroundworkTestAccess.DefaultScoped,
            source.CreateNamePolicy(),
            cancellationToken: cancellationToken);
        if (!IsExactTarget(source, handle))
        {
            await handle.DisposeAsync();
            throw new InvalidOperationException("The MongoDB physical provider driver opened a target different from the applied runtime target.");
        }

        var services = new ServiceCollection()
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
        if (result.Outcome is PhysicalSchemaApplicationOutcome.Rejected or PhysicalSchemaApplicationOutcome.AuthorizationRequired)
            throw new InvalidOperationException($"MongoDB physical schema application was not accepted: {result.Outcome}.");
    }

}
