using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Exceptions;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Reconciliation.Handlers;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Activities.Design.Reconciliation.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Identity;
using Elsa.Primitives.Versioning;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Reconciliation.Handlers;
using Elsa.Workflows.Design.Reconciliation.Models;
using Elsa.Workflows.Design.Reconciliation.Options;
using Elsa.Workflows.Design.Reconciliation.Services;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using IDesignAtomicWriter = Elsa.Workflows.Design.Persistence.Core.Contracts.IDesignAtomicWriter;
using Options = Microsoft.Extensions.Options.Options;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class ReconcilerColdStartRaceSqliteTests : IAsyncLifetime
{
    private SqliteTestDatabase publishing = null!;
    private SqliteTestDatabase activitiesDesign = null!;
    private SqliteTestDatabase runtime = null!;
    private SqliteTestDatabase workflowsDesign = null!;

    private ReconcilerDatabases Databases => new(
        publishing.ConnectionString,
        activitiesDesign.ConnectionString,
        runtime.ConnectionString,
        workflowsDesign.ConnectionString);

    public async Task InitializeAsync()
    {
        publishing = await SqliteTestDatabase.CreateAsync<PublishingSnapshotReviewSqliteDbContext>(options => new(options));
        activitiesDesign = await SqliteTestDatabase.CreateAsync<ActivitiesDesignSqliteDbContext>(options => new(options));
        runtime = await SqliteTestDatabase.CreateAsync<RuntimeSqliteDbContext>(options => new(options));
        workflowsDesign = await SqliteTestDatabase.CreateAsync<WorkflowsDesignSqliteDbContext>(options => new(options));
    }

    public async Task DisposeAsync()
    {
        await publishing.DisposeAsync();
        await activitiesDesign.DisposeAsync();
        await runtime.DisposeAsync();
        await workflowsDesign.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // The source marks the definition deleted at first import.
    public Task Two_nodes_cold_starting_against_new_content_converge_on_one_definition_version_and_publication(bool deletedInSource) =>
        new ReconcilerColdStartRace(PublishingNativeProvider.Sqlite, Databases, deletedInSource).RunAsync();

    [Fact]
    public async Task Two_scopes_reconciling_the_same_source_keep_their_own_version_publication_and_source_reference()
    {
        // The derived version id leaves the scope out, so both scopes store the version under one id. Every record that
        // carries it is kept under its scope as well, so nothing aliases, even with one publication id and one
        // source-reference id in both scopes.
        string[] scopes = ["tenant-a", "tenant-b"];
        var provider = PublishingNativeProvider.Sqlite;
        var versionId = WorkflowReconciliationPass.VersionId;
        foreach (var scope in scopes)
        {
            await WorkflowReconciliationPass.RunAsync(provider, Databases.WorkflowsDesign, TestAccess.Scoped(scope));
            await using var records = OpenRecords(scope);
            await records.SourceReferences.SaveAsync(new WorkflowExecutableSourceReference(
                "activation-ref:publication-1",
                $"artifact-{scope}",
                "Workflow",
                "source-1",
                WorkflowReconciliationPass.Version,
                WorkflowReconciliationPass.DefinitionId,
                versionId,
                WorkflowReconciliationPass.Version,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                WorkflowExecutableReferenceScope.Published));
            await new EfPublicationRecordStore(records.Publishing, TestAccess.Scoped(scope)).SaveAsync(
                EfPublicationRecordStoreTests.Record("publication-1", "slot-1") with
                {
                    WorkflowDefinitionId = WorkflowReconciliationPass.DefinitionId,
                    WorkflowDefinitionVersionId = versionId,
                    ArtifactId = $"artifact-{scope}",
                    SourceReferenceId = "activation-ref:publication-1"
                });
        }

        foreach (var scope in scopes)
        {
            var version = Assert.Single(await WorkflowReconciliationPass.ListVersionsAsync(provider, Databases.WorkflowsDesign, TestAccess.Scoped(scope)));
            Assert.Equal((versionId, scope), (version.Id, version.TenantId));
            await using var records = OpenRecords(scope);
            var reference = Assert.Single((await records.SourceReferences.ListByDefinitionVersionPageAsync(new(versionId))).Items);
            Assert.Equal($"artifact-{scope}", reference.ArtifactId);
            var publication = await new EfPublicationRecordStore(records.Publishing, TestAccess.Scoped(scope)).FindAsync("publication-1");
            Assert.Equal((versionId, $"artifact-{scope}"), (publication!.WorkflowDefinitionVersionId, publication.ArtifactId));
        }

        await using var context = provider.WorkflowDesign(Databases.WorkflowsDesign, []);
        var tenants = await context.Versions.AsNoTracking().Where(row => row.Id == versionId).Select(row => row.TenantId).ToListAsync();
        Assert.Equal(scopes, tenants.Order(StringComparer.Ordinal));
    }

    private ActivityPublicationScope OpenRecords(string scope) => new(
        PublishingNativeProvider.Sqlite.Publishing(publishing.ConnectionString, []),
        PublishingNativeProvider.Sqlite.Design(activitiesDesign.ConnectionString, []),
        PublishingNativeProvider.Sqlite.Runtime(runtime.ConnectionString, []),
        TestAccess.Scoped(scope));
}

[Collection(PublishingPostgreSqlContainerFixture.CollectionName)]
public sealed class ReconcilerColdStartRacePostgreSqlTests(PublishingPostgreSqlContainerFixture fixture)
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)] // The source marks the definition deleted at first import.
    public async Task Two_nodes_cold_starting_against_new_content_converge_on_one_definition_version_and_publication(bool deletedInSource) =>
        await new ReconcilerColdStartRace(PublishingNativeProvider.PostgreSql, await CreateDatabasesAsync(), deletedInSource).RunAsync();

    /// <summary>Creates this run's database for each module on the server, with its schema.</summary>
    private async Task<ReconcilerDatabases> CreateDatabasesAsync()
    {
        var server = PublishingProviderContainerSupport.Require(fixture.IsAvailable, fixture.SkipReason, "PostgreSQL", () => fixture.ConnectionString);
        var provider = PublishingNativeProvider.PostgreSql;
        var prefix = $"elsa_cold_{Guid.NewGuid().ToString("N")[..12]}";
        await provider.CreateDesignDatabase!(server, $"{prefix}_act");
        return new(
            await CreateAsync(server, $"{prefix}_pub", provider.Publishing),
            await CreateAsync(server, $"{prefix}_act", provider.Design),
            await CreateAsync(server, $"{prefix}_rt", provider.Runtime),
            await CreateAsync(server, $"{prefix}_wf", provider.WorkflowDesign));
    }

    private static async Task<string> CreateAsync<TContext>(string server, string database, Func<string, IInterceptor[], TContext> open)
        where TContext : DbContext
    {
        var connectionString = PublishingLedgerNativeProviderSmoke.WithDatabase(server, database);
        await using var context = open(connectionString, []);
        await context.Database.EnsureCreatedAsync();
        return connectionString;
    }
}

/// <summary>The databases both nodes share, one per module, as a host that splits the lanes uses them.</summary>
internal sealed record ReconcilerDatabases(string Publishing, string ActivitiesDesign, string Runtime, string WorkflowsDesign);

/// <summary>One node's workflow reconciliation pass of one definition version: its own context, the real aggregating handler and EF commands.</summary>
internal static class WorkflowReconciliationPass
{
    public const string DefinitionId = "wf-cold-start";
    public const string Version = "1.0.0";
    public static readonly string VersionId = WorkflowReconciliationVersionIds.For(DefinitionId, SemVer.ToSortKey(Version));
    public static readonly IPayloadSerializer Serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

    public static async Task RunAsync(
        PublishingNativeProvider provider,
        string database,
        IPersistenceAccessContextAccessor access,
        bool deletedInSource = false,
        ConcurrentQueue<(string OperationKind, DesignAtomicWriteStatus Status)>? writes = null,
        Pause? definitionRead = null,
        Pause? versionCheck = null)
    {
        await using var context = provider.WorkflowDesign(database, []);
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(context, access);
        if (writes is not null)
            writer = new RecordingAtomicWriter(writer, writes);
        var definitions = new EfWorkflowDefinitionStore(context, access);
        var versions = new EfWorkflowDefinitionVersionStore(context, Serializer, definitions, access);
        var identities = new GuidIdentityGenerator();
        var source = new StaticWorkflowSource(new WorkflowVersionReconciliationModel(
            DefinitionId, "Cold start", null, Version, new([], null, [], [], null), Deleted: deletedInSource));
        await new WorkflowsVersionReconciler(
            NullLogger<WorkflowsVersionReconciler>.Instance,
            new HandlingPublisher<WorkflowVersionsReconciling>(new WorkflowVersionsReconcilingHandler(
                new WorkflowDefinitionFactory(identities),
                new WorkflowDefinitionVersionFactory(identities),
                [source])),
            Options.Create(new WorkflowVersionReconcilerOptions()),
            definitionRead is null ? definitions : new PausingDefinitionStore(definitions, definitionRead),
            versionCheck is null ? versions : new PausingVersionStore(versions, versionCheck),
            new EfMaterializeWorkflowDefinitionCommand(context, access, writer),
            new EfMaterializeWorkflowDefinitionVersionCommand(context, access, writer, Serializer),
            new EfSaveWorkflowDefinitionCommand(context, access, writer),
            Serializer).Reconcile(CancellationToken.None);
    }

    public static async Task<WorkflowDefinition?> FindDefinitionAsync(PublishingNativeProvider provider, string database, IPersistenceAccessContextAccessor access)
    {
        await using var context = provider.WorkflowDesign(database, []);
        return await new EfWorkflowDefinitionStore(context, access).FindByIdAsync(DefinitionId);
    }

    public static async Task<IReadOnlyList<WorkflowDefinitionVersion>> ListVersionsAsync(PublishingNativeProvider provider, string database, IPersistenceAccessContextAccessor access)
    {
        await using var context = provider.WorkflowDesign(database, []);
        return await new EfWorkflowDefinitionVersionStore(context, Serializer, new EfWorkflowDefinitionStore(context, access), access).ListByDefinitionAsync(DefinitionId);
    }
}

/// <summary>
/// Two nodes cold-start against databases that hold none of their sources' content (#2189). Each node runs the activity
/// and the workflow version reconciler, as its shell start does, so each reconciler runs on both nodes at once. Every
/// pass is held where it has found its content absent until the other node's pass has found the same, so both write
/// and the provider decides between them. Then both nodes start a second time, against what the first start left.
/// </summary>
/// <remarks>
/// Each pass gets its own contexts and connections, as each node's startup scope does. The activity reconciler runs
/// with the Publishing bridge composed, the configuration in which its losing node used to fail. When the source marks
/// the definition deleted, each node stamps its own deletion time, which the definition's materialization request
/// used to carry.
/// </remarks>
internal sealed class ReconcilerColdStartRace(PublishingNativeProvider provider, ReconcilerDatabases databases, bool deletedInSource)
{
    private static readonly TestAccess Access = TestAccess.Scoped("default");
    private readonly ConcurrentQueue<(string OperationKind, DesignAtomicWriteStatus Status)> workflowWrites = new();
    private readonly ConcurrentQueue<Exception?> publicationCommits = new();

    public async Task RunAsync()
    {
        var first = new Holds();
        var second = new Holds();
        Task[] workflowPasses = [ReconcileWorkflowsAsync(first), ReconcileWorkflowsAsync(second)];
        Task[] activityPasses = [ReconcileActivitiesAsync(first.PublicationRead), ReconcileActivitiesAsync(second.PublicationRead)];
        await Pause.ReleaseTogether((workflowPasses[0], first.DefinitionRead), (workflowPasses[1], second.DefinitionRead));
        await Pause.ReleaseTogether((workflowPasses[0], first.VersionCheck), (workflowPasses[1], second.VersionCheck));
        await Pause.ReleaseTogether((activityPasses[0], first.PublicationRead), (activityPasses[1], second.PublicationRead));
        await Task.WhenAll(workflowPasses.Concat(activityPasses));

        // Both nodes wrote the definition, the version and the publication. Each time, the node that wrote second
        // converged on the first one's write instead of failing its start.
        Assert.Equal(
            new (string, DesignAtomicWriteStatus)[]
            {
                (EfMaterializeWorkflowDefinitionCommand.OperationKind, DesignAtomicWriteStatus.Committed),
                (EfMaterializeWorkflowDefinitionCommand.OperationKind, DesignAtomicWriteStatus.Replayed),
                (EfMaterializeWorkflowDefinitionVersionCommand.OperationKind, DesignAtomicWriteStatus.Committed),
                (EfMaterializeWorkflowDefinitionVersionCommand.OperationKind, DesignAtomicWriteStatus.Replayed)
            },
            workflowWrites.Order());
        Assert.Equal(2, publicationCommits.Count);
        Assert.IsType<ActivityVersionAlreadyPublishedException>(Assert.Single(publicationCommits, failure => failure is not null));
        await AssertReconciledOnceAsync();

        // The second start finds everything in place and writes nothing.
        var writes = workflowWrites.Count;
        var commits = publicationCommits.Count;
        await Task.WhenAll(ReconcileWorkflowsAsync(), ReconcileWorkflowsAsync(), ReconcileActivitiesAsync(), ReconcileActivitiesAsync());
        Assert.Equal(writes, workflowWrites.Count);
        Assert.Equal(commits, publicationCommits.Count);
        await AssertReconciledOnceAsync();
    }

    private Task ReconcileWorkflowsAsync(Holds? holds = null) =>
        WorkflowReconciliationPass.RunAsync(provider, databases.WorkflowsDesign, Access, deletedInSource, workflowWrites, holds?.DefinitionRead, holds?.VersionCheck);

    private async Task ReconcileActivitiesAsync(Pause? publicationRead = null)
    {
        await using var scope = new ActivityPublicationScope(
            provider.Publishing(databases.Publishing, []),
            provider.Design(databases.ActivitiesDesign, []),
            provider.Runtime(databases.Runtime, []),
            Access);
        var stores = scope.DesignStores;
        var publisher = new SourceOwnedActivityVersionPublisher(
            new RecordingSourceCommit(scope.SourceCommand, publicationCommits),
            publicationRead is null ? stores : new PausingPublicationStore(stores, publicationRead),
            CreateNodeCompiler(),
            TimeProvider.System,
            NullLogger<SourceOwnedActivityVersionPublisher>.Instance);
        var identities = new GuidIdentityGenerator();
        var hasher = new DefaultActivityDefinitionHasher();
        await new ActivityVersionReconciler(
            NullLogger<ActivityVersionReconciler>.Instance,
            hasher,
            new HandlingPublisher<ActivityVersionsReconciling>(new CollectActivityVersions(
                stores,
                new ActivityDefinitionFactory(identities),
                new ActivityDefinitionVersionFactory(identities, hasher),
                WorkflowReconciliationPass.Serializer,
                [new StaticActivitySource()])),
            Options.Create(new ActivityVersionReconcilerOptions()),
            stores,
            stores,
            stores,
            stores,
            [publisher]).Reconcile(CancellationToken.None);
    }

    private async Task AssertReconciledOnceAsync()
    {
        var definition = await WorkflowReconciliationPass.FindDefinitionAsync(provider, databases.WorkflowsDesign, Access);
        Assert.Equal(deletedInSource, definition!.DeletedAt is not null);
        var version = Assert.Single(await WorkflowReconciliationPass.ListVersionsAsync(provider, databases.WorkflowsDesign, Access));
        Assert.Equal(WorkflowReconciliationPass.VersionId, version.Id);

        await using var design = provider.Design(databases.ActivitiesDesign, []);
        var catalogVersion = Assert.Single(await design.ActivityDefinitionVersions.AsNoTracking().ToListAsync());
        var publication = Assert.Single(await design.ActivityDefinitionVersionPublications.AsNoTracking().ToListAsync());
        Assert.Equal(catalogVersion.Id, publication.DefinitionVersionId);
    }

    private static ExecutableNodeCompiler CreateNodeCompiler()
    {
        var types = new WellKnownTypeRegistry();
        var outputs = new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
        return new ExecutableNodeCompiler(new DefaultActivityStructureService([]), types, new RuntimeInputBindingCompiler(types), outputs);
    }

    /// <summary>Where one node's passes stop on the first start.</summary>
    private sealed class Holds
    {
        public Pause DefinitionRead { get; } = new();
        public Pause VersionCheck { get; } = new();
        public Pause PublicationRead { get; } = new();
    }

    /// <summary>One CLR-style activity, listed the same way on every node.</summary>
    private sealed class StaticActivitySource : IActivityReconciliationSource
    {
        public string SourceId => "cold-start-assemblies";

        public string SourceKind => "CLR";

        public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>(
            [
                new(
                    Id: null,
                    Version: WorkflowReconciliationPass.Version,
                    ActivityTypeKey: "Acme.ColdStart",
                    DisplayName: "Cold start",
                    Category: "Tests",
                    Description: null,
                    ProviderKey: "elsa.clr-activity",
                    ProviderSchemaVersion: "1",
                    ConsumerKey: WellKnownRuntimeActivityConsumers.ClrActivity,
                    ConsumerSchemaVersion: "1",
                    Descriptor: JsonSerializer.SerializeToElement(new { typeAlias = "Acme.ColdStart" }),
                    Inputs: [],
                    Outputs: [],
                    DesignFacets: [])
            ]);
    }

    /// <summary>Stops the publisher right after it first finds the version unpublished, before it commits.</summary>
    private sealed class PausingPublicationStore(IActivityDefinitionVersionPublicationStore inner, Pause afterFirstRead) : IActivityDefinitionVersionPublicationStore
    {
        private int reads;

        public async Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default)
        {
            var publication = await inner.FindAsync(definitionVersionId, cancellationToken);
            if (Interlocked.Increment(ref reads) == 1)
                await afterFirstRead.HoldAsync();
            return publication;
        }

        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            inner.ListByDefinitionAsync(definitionId, cancellationToken);
    }

    /// <summary>Records each commit attempt: <c>null</c> when it committed, its exception when it failed.</summary>
    private sealed class RecordingSourceCommit(
        ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference> inner,
        ConcurrentQueue<Exception?> attempts) : ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>
    {
        public async Task ExecuteAsync(
            SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await inner.ExecuteAsync(commit, cancellationToken);
                attempts.Enqueue(null);
            }
            catch (Exception failure)
            {
                attempts.Enqueue(failure);
                throw;
            }
        }
    }
}
