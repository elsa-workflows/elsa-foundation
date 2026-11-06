using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Physical canonical installation/retention controls; not delivery or StateMachine qualification.</summary>
public sealed partial class StimulusIngressSourceStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("tenant");
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1", "hash");
    private static StimulusDeliveryScope Origin => new(Scope, "tenant", new("tenant"), WorkflowRunKind.PublishedRun, null);

    [Fact]
    public async Task First_empty_selection_survives_provider_restart_and_rejects_conflicting_request()
    {
        await using var fixture = await Fixture.Create();
        await using (var context = fixture.Open())
        {
            var store = fixture.SourceStore(context);
            var first = Source("first", "key", false);
            Assert.Equal("first", (await store.InstallAsync(first)).SourceId);
        }
        await using var reopened = fixture.Open();
        var restarted = fixture.SourceStore(reopened);
        var winner = await restarted.InstallAsync(Source("loser", "key", false));
        Assert.Equal("first", winner.SourceId);
        Assert.Empty(winner.Cohort.Starts);
        Assert.Equal("first", (await restarted.FindByKeyAsync(Origin, "key"))!.SourceId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.InstallAsync(Source("conflict", "key", false, "other")).AsTask());
        Assert.Single(await reopened.StimulusIngressSources.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(RuntimeArtifactEfModule.SchemaFamily)]
    [InlineData(RuntimeWorkflowExecutionEfModule.SchemaFamily)]
    public async Task Installation_refuses_before_any_source_or_ownership_write_when_retention_reader_is_not_admitted(string heldFamily)
    {
        await using var fixture = await Fixture.Create(heldFamily);
        await using var context = fixture.Open();
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.SourceStore(context).InstallAsync(Source("source", "key", false)).AsTask());
        Assert.Empty(await context.StimulusIngressSources.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.ExecutionLivenessStates.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canonical_install_retains_an_existing_or_later_started_execution_after_provider_restart(bool existingExecution)
    {
        await using var fixture = await Fixture.Create();
        var source = Source("source", null, true);
        var executionId = Assert.Single(source.GetExecutionDependencies());
        await using (var context = fixture.Open())
        {
            await fixture.Material(context).SaveAsync(Material());
            if (existingExecution) await fixture.Executions(context).SaveAsync(Execution(executionId));
            await fixture.SourceStore(context).InstallAsync(source);
        }
        await using var reopened = fixture.Open();
        if (!existingExecution) await fixture.Executions(reopened).SaveAsync(Execution(executionId));
        var stateBefore = (await reopened.WorkflowExecutionStates.AsNoTracking().SingleAsync()).ContentJson;
        Assert.Equal(RuntimeWorkflowExecutionEfModule.IngressRetentionSchemaVersion,
            (await reopened.WorkflowExecutionStates.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.False(await fixture.Executions(reopened).DeleteAsync(executionId));
        Assert.NotNull(await fixture.Executions(reopened).FindAsync(executionId));
        Assert.Equal(stateBefore, (await reopened.WorkflowExecutionStates.AsNoTracking().SingleAsync()).ContentJson);
        Assert.Single(await reopened.StimulusIngressSources.AsNoTracking().ToArrayAsync());
    }

    private static StimulusIngressSource Source(string id, string? key, bool start, string hash = "hash")
    {
        var binding = new WorkflowTriggerBinding(WorkflowTriggerBinding.BuildId(Pin.ArtifactId, "node", hash),
            Pin.ArtifactId, Pin.DefinitionId, Pin.ArtifactVersion, Pin.ArtifactHash, "node", "event", hash,
            null, new Dictionary<string, string>(), Now);
        return new(1, id, Origin, key, new(1, "event", hash, null, null, StimulusRoutingMode.StartAndResume, "test",
            new Dictionary<string, string>(), null, null, null, StimulusStartKeyScope.Artifact, false, null), Now,
            new(Scope, Now, start ? [new StimulusStartRecipient(binding, Pin, sourceProvenance: null)] : [], [], [], []));
    }

    private static WorkflowExecutionState Execution(string id) => new(id, Pin, WorkflowExecutionStatus.Completed, null,
        Now, Now, Now, Now, null, null, "tenant", new Dictionary<string, string>())
        { RunKind = WorkflowRunKind.PublishedRun, Partition = new("tenant") };
    private static WorkflowExecutable Material(WorkflowExecutableIdentity? identity = null, params WorkflowExecutable[] dependencies) => new(identity ?? Pin,
        new ExecutableNode("node", "authored", "test/activity", "1.0.0",
            new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
        new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), inputContract: null,
        dependencies: dependencies.Select(dependency => new WorkflowExecutableDependency(dependency.Identity.ArtifactId, dependency.Identity.ArtifactHash, ["node"])).ToArray(),
        incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private sealed class Accessor : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
    private sealed class Partitions : IWorkflowExecutionPartitionAccessor
    { public WorkflowExecutionPartition Current => new("tenant"); }
    private sealed class NoTokens : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public byte[] Decode(string purpose, string token) => throw new NotSupportedException();
    }
    private sealed class CurrentFleet : IEfSchemaFleet
    {
        public EfSchemaFleetStanding GetLocalStanding() => new(new("host", "incarnation"), false);
        public ValueTask PublishAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public ValueTask<EfSchemaFleetAnswer> CountObservingAsync(string family, IReadOnlyList<string> versions, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public TimeSpan SettleMargin => TimeSpan.Zero;
        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly EfSchemaFinalizationGates gates = new();
        private readonly ServiceProvider services;
        private readonly FakeTimeProvider clock = new(Now);
        private Fixture() => services = new ServiceCollection().AddSingleton(gates).BuildServiceProvider();
        public static async Task<Fixture> Create(string? heldFamily = null)
        {
            var fixture = new Fixture();
            await fixture.connection.OpenAsync();
            await using var context = fixture.Open();
            await context.Database.EnsureCreatedAsync();
            var families = EfSchemaModuleFamilies.ForContext(typeof(RuntimeDbContext))!;
            var store = new EfSchemaFinalizationStore(context);
            foreach (var family in families.Chains.Where(chain => chain.Family is RuntimeArtifactEfModule.SchemaFamily or
                         RuntimeWorkflowExecutionEfModule.SchemaFamily or RuntimeStimulusIngressEfModule.SchemaFamily))
            {
                var version = family.Family == heldFamily ? "1.0.0" : family.CurrentVersion;
                var record = await store.GetOrCreateAsync(family.Family, version, family.ReadableVersions, SchemaFinalizationActor.OfOperator("test"));
                if (family.Family == heldFamily)
                    await store.PlaceHoldAsync(family.Family, record.Revision, null, "controlled predecessor reader", "test", family.ReadableVersions);
            }
            var gate = new EfSchemaModuleGate(families, new CurrentFleet(), new EfSchemaFinalizationObservations(), new());
            await gate.ActivateAsync(context);
            fixture.gates.Register(typeof(RuntimeDbContext), gate);
            return fixture;
        }
        public RuntimeSqliteDbContext Open(IInterceptor? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).UseApplicationServiceProvider(services);
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            EfSchemaWriteGateInterceptor.EnsureAdded(builder);
            return new(builder.Options);
        }
        public IWorkflowExecutableRootWriteLeaseManager RootManager(RuntimeDbContext context) =>
            new WorkflowExecutableRootWriteLeaseManager(Material(context), Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock);
        public EfWorkflowExecutableStore Material(RuntimeDbContext context) => new(context, new Accessor());
        public EfWorkflowExecutionStateStore Executions(RuntimeDbContext context) => new(context, new Accessor(), new NoTokens(), clock);
        public IStimulusIngressSourceStore SourceStore(RuntimeDbContext context, IWorkflowExecutableRootWriteLeaseManager? rootManager = null)
        {
            var material = Material(context);
            var references = new EfWorkflowExecutableSourceReferenceStore(context, new Accessor(), new NoTokens(), clock);
            return new EfStimulusIngressSourceStore(context, new Accessor(), new Partitions(), material, references, Executions(context),
                rootManager ?? new WorkflowExecutableRootWriteLeaseManager(material, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock), clock);
        }
        public async ValueTask DisposeAsync() { await services.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
