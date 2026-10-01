using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
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
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Options = Microsoft.Extensions.Options.Options;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class ReconcilerColdStartRaceSqliteTests : IAsyncLifetime
{
    private SqliteTestDatabase publishing = null!;
    private SqliteTestDatabase activitiesDesign = null!;
    private SqliteTestDatabase runtime = null!;
    private SqliteTestDatabase workflowsDesign = null!;

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

    [Fact]
    public Task Two_nodes_cold_starting_against_new_content_both_reconcile_twice() =>
        new ReconcilerColdStartRace(PublishingNativeProvider.Sqlite, new(
            publishing.ConnectionString,
            activitiesDesign.ConnectionString,
            runtime.ConnectionString,
            workflowsDesign.ConnectionString)).RunAsync();
}

[Collection(PublishingPostgreSqlContainerFixture.CollectionName)]
public sealed class ReconcilerColdStartRacePostgreSqlTests(PublishingPostgreSqlContainerFixture fixture)
{
    [SkippableFact]
    public async Task Two_nodes_cold_starting_against_new_content_both_reconcile_twice()
    {
        var server = PublishingProviderContainerSupport.Require(fixture.IsAvailable, fixture.SkipReason, "PostgreSQL", () => fixture.ConnectionString);
        var provider = PublishingNativeProvider.PostgreSql;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var databases = new ReconcilerDatabases(
            PublishingLedgerNativeProviderSmoke.WithDatabase(server, $"elsa_cold_{suffix}_pub"),
            PublishingLedgerNativeProviderSmoke.WithDatabase(server, $"elsa_cold_{suffix}_act"),
            PublishingLedgerNativeProviderSmoke.WithDatabase(server, $"elsa_cold_{suffix}_rt"),
            PublishingLedgerNativeProviderSmoke.WithDatabase(server, $"elsa_cold_{suffix}_wf"));
        await provider.CreateDesignDatabase!(server, $"elsa_cold_{suffix}_act");
        await using (var context = provider.Publishing(databases.Publishing, []))
            await context.Database.EnsureCreatedAsync();
        await using (var context = provider.Design(databases.ActivitiesDesign, []))
            await context.Database.EnsureCreatedAsync();
        await using (var context = provider.Runtime(databases.Runtime, []))
            await context.Database.EnsureCreatedAsync();
        await using (var context = provider.WorkflowDesign(databases.WorkflowsDesign, []))
            await context.Database.EnsureCreatedAsync();

        await new ReconcilerColdStartRace(provider, databases).RunAsync();
    }
}

/// <summary>The databases both nodes share, one per module, as a host that splits the lanes uses them.</summary>
internal sealed record ReconcilerDatabases(string Publishing, string ActivitiesDesign, string Runtime, string WorkflowsDesign);

/// <summary>
/// Two nodes cold-start against databases that hold none of their sources' content (#2189). Each node runs the activity
/// and the workflow version reconciler, as its shell start does, so each reconciler runs on both nodes at once. Every
/// pass is held where it has found its content absent until the other node's pass has found the same, so both write
/// and the provider decides between them. Then both nodes start a second time, against what the first start left.
/// </summary>
/// <remarks>
/// Each pass gets its own contexts and connections, as each node's startup scope does. The activity reconciler runs
/// with the Publishing bridge composed, the configuration in which its losing node used to fail.
/// </remarks>
internal sealed class ReconcilerColdStartRace(PublishingNativeProvider provider, ReconcilerDatabases databases)
{
    private const string DefinitionId = "wf-cold-start";
    private const string Version = "1.0.0";
    private static readonly IPayloadSerializer Serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
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
        Assert.Single(publicationCommits, failure => failure is not null);
        await AssertReconciledOnceAsync();

        // The second start finds everything in place and writes nothing.
        var writes = workflowWrites.Count;
        var commits = publicationCommits.Count;
        await Task.WhenAll(ReconcileWorkflowsAsync(), ReconcileWorkflowsAsync(), ReconcileActivitiesAsync(), ReconcileActivitiesAsync());
        Assert.Equal(writes, workflowWrites.Count);
        Assert.Equal(commits, publicationCommits.Count);
        await AssertReconciledOnceAsync();
    }

    private async Task ReconcileWorkflowsAsync(Holds? holds = null)
    {
        await using var context = provider.WorkflowDesign(databases.WorkflowsDesign, []);
        var writer = new RecordingAtomicWriter(new EfDesignAtomicWriter(context, Access), workflowWrites);
        var definitions = new EfWorkflowDefinitionStore(context, Access);
        var versions = new EfWorkflowDefinitionVersionStore(context, Serializer, definitions, Access);
        var identities = new GuidIdentityGenerator();
        var source = new StaticWorkflowSource(new WorkflowVersionReconciliationModel(DefinitionId, "Cold start", null, Version, new([], null, [], [], null)));
        await new WorkflowsVersionReconciler(
            NullLogger<WorkflowsVersionReconciler>.Instance,
            new HandlingPublisher<WorkflowVersionsReconciling>(new WorkflowVersionsReconcilingHandler(
                new WorkflowDefinitionFactory(identities),
                new WorkflowDefinitionVersionFactory(identities),
                [source])),
            Options.Create(new WorkflowVersionReconcilerOptions()),
            holds is null ? definitions : new PausingDefinitionStore(definitions, holds.DefinitionRead),
            holds is null ? versions : new PausingVersionStore(versions, holds.VersionCheck),
            new EfMaterializeWorkflowDefinitionCommand(context, Access, writer),
            new EfMaterializeWorkflowDefinitionVersionCommand(context, Access, writer, Serializer),
            new EfSaveWorkflowDefinitionCommand(context, Access, writer),
            Serializer).Reconcile(CancellationToken.None);
    }

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
            TimeProvider.System);
        var identities = new GuidIdentityGenerator();
        var hasher = new DefaultActivityDefinitionHasher();
        await new ActivityVersionReconciler(
            NullLogger<ActivityVersionReconciler>.Instance,
            hasher,
            new HandlingPublisher<ActivityVersionsReconciling>(new CollectActivityVersions(
                stores,
                new ActivityDefinitionFactory(identities),
                new ActivityDefinitionVersionFactory(identities, hasher),
                Serializer,
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
        await using (var context = provider.WorkflowDesign(databases.WorkflowsDesign, []))
        {
            var definitions = new EfWorkflowDefinitionStore(context, Access);
            var version = Assert.Single(await new EfWorkflowDefinitionVersionStore(context, Serializer, definitions, Access).ListByDefinitionAsync(DefinitionId));
            Assert.Equal(WorkflowReconciliationVersionIds.For(DefinitionId, SemVer.ToSortKey(Version)), version.Id);
        }

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
                    Version: Version,
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
