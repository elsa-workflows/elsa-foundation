using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Testing;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>Runs <see cref="PublicationJournalConvergence"/> on SQLite, each module in its own database file.</summary>
public sealed class PublicationJournalConvergenceSqliteTests : IAsyncLifetime
{
    private SqliteTestDatabase publishing = null!;
    private SqliteTestDatabase runtime = null!;

    public static TheoryData<string> Scenarios => PublicationJournalConvergence.Scenarios;

    public async Task InitializeAsync()
    {
        publishing = await SqliteTestDatabase.CreateAsync<PublishingSnapshotReviewSqliteDbContext>(options => new(options));
        runtime = await SqliteTestDatabase.CreateAsync<RuntimeSqliteDbContext>(options => new(options));
    }

    public async Task DisposeAsync()
    {
        await publishing.DisposeAsync();
        await runtime.DisposeAsync();
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public Task Sqlite(string scenario) =>
        PublicationJournalConvergence.RunAsync(scenario, new(PublishingNativeProvider.Sqlite, publishing.ConnectionString, runtime.ConnectionString));
}

/// <summary>Runs <see cref="PublicationJournalConvergence"/> on PostgreSQL, in a database per module for each scenario.</summary>
[Collection(PublishingPostgreSqlContainerFixture.CollectionName)]
public sealed class PublicationJournalConvergencePostgreSqlTests(PublishingPostgreSqlContainerFixture fixture)
{
    public static TheoryData<string> Scenarios => PublicationJournalConvergence.Scenarios;

    [SkippableTheory]
    [MemberData(nameof(Scenarios))]
    public async Task PostgreSql(string scenario)
    {
        var server = PublishingProviderContainerSupport.Require(fixture.IsAvailable, fixture.SkipReason, "PostgreSQL", () => fixture.ConnectionString);
        var provider = PublishingNativeProvider.PostgreSql;
        var prefix = $"elsa_journal_{Guid.NewGuid().ToString("N")[..12]}";
        var databases = new JournalDatabases(
            provider,
            PublishingLedgerNativeProviderSmoke.WithDatabase(server, $"{prefix}_pub"),
            PublishingLedgerNativeProviderSmoke.WithDatabase(server, $"{prefix}_rt"));
        await using (var context = provider.Publishing(databases.Publishing, []))
            await context.Database.EnsureCreatedAsync();
        await using (var context = provider.Runtime(databases.Runtime, []))
            await context.Database.EnsureCreatedAsync();

        await PublicationJournalConvergence.RunAsync(scenario, databases);
    }
}

/// <summary>The publishing and runtime databases every process of one scenario shares.</summary>
internal sealed record JournalDatabases(PublishingNativeProvider Provider, string Publishing, string Runtime);

/// <summary>
/// The publication journal across the activation crash window of #2193 (#2223), written once and run on EF Core over
/// SQLite and PostgreSQL.
/// </summary>
/// <remarks>
/// A crash is a publish whose process stops for good once its slot transition commits
/// (<see cref="PauseAfterSlotTransition"/> with nothing to resume it), so neither the projection switch nor any journal
/// write runs. Each scenario then works through another process over the same databases, publishing through the real
/// <see cref="PublishWorkflowRequestHandler"/>, and checks the journal, the slot and what serves.
/// </remarks>
internal static class PublicationJournalConvergence
{
    private const string DefinitionId = "definition-journal";
    private const string SlotName = "default";
    private const string NodeId = "node-start";
    private const string StimulusType = "Event";
    private const string StimulusHash = "journal";
    private static readonly string SlotId = WorkflowActivationSlotIdentity.Create(DefinitionId, SlotName);

    private static readonly Dictionary<string, Func<JournalDatabases, Task>> All = new()
    {
        ["same-version-republish-converges-the-journal-of-an-interrupted-replacement"] = SameVersionRepublishConvergesAnInterruptedReplacementAsync,
        ["same-version-republish-converges-the-journal-of-an-interrupted-first-publication"] = SameVersionRepublishConvergesAnInterruptedFirstPublicationAsync,
        ["shell-start-converges-the-journal-of-an-interrupted-replacement"] = ShellStartConvergesAnInterruptedReplacementAsync,
        ["replacing-an-interrupted-replacement-retires-it-without-a-journal-error"] = ReplacingAnInterruptedReplacementRetiresItAsync,
        ["same-version-republish-converges-a-journal-written-after-the-runtime-finished"] = SameVersionRepublishConvergesAfterTheRuntimeFinishedAsync,
        ["same-version-republish-of-a-publication-that-cannot-serve-is-refused"] = SameVersionRepublishOfAPublicationThatCannotServeIsRefusedAsync,
        ["a-stop-before-the-last-journal-write-leaves-the-candidate-lagging-until-the-next-completion"] = AStopBeforeTheLastJournalWriteLeavesTheCandidateLaggingAsync,
        ["two-nodes-completing-one-slot-converge-to-one-journal-state"] = TwoNodesCompletingOneSlotConvergeAsync,
        ["a-publish-whose-slot-transition-commits-and-then-throws-converges-the-journal"] = APublishWhoseSlotTransitionCommitsAndThenThrowsConvergesAsync
    };

    public static TheoryData<string> Scenarios
    {
        get
        {
            var scenarios = new TheoryData<string>();
            foreach (var scenario in All.Keys)
                scenarios.Add(scenario);
            return scenarios;
        }
    }

    public static Task RunAsync(string scenario, JournalDatabases databases) => All[scenario](databases);

    /// <summary>
    /// The direction that looks like success: the slot names the new publication and nothing failed, yet its record is a
    /// candidate and the one it replaced is still active. Publishing the same version again used to find it already
    /// published and fail to describe it; it now completes the publication and answers with it.
    /// </summary>
    private static async Task SameVersionRepublishConvergesAnInterruptedReplacementAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        var interrupted = await StopAfterSlotTransitionAsync(databases, "version-2");
        await using var node = new PublishingNode(databases);
        await node.AssertLaggingAsync(interrupted, first);
        await node.AssertServingAsync(first);

        var republished = await node.PublishAsync("version-2");

        Assert.False(republished.WasCreated);
        Assert.Equal(interrupted, republished.PublicationId);
        Assert.Equal(PublicationStatusView.Active, republished.Status);
        await node.AssertConvergedAsync(interrupted, first);
        await node.AssertServingAsync(interrupted);
        Assert.Equal(interrupted, (await node.PublishAsync("version-2")).PublicationId);
        await node.AssertConvergedAsync(interrupted, first);
    }

    /// <summary>A first publication has nothing to replace; its record still lags the slot until it is completed.</summary>
    private static async Task SameVersionRepublishConvergesAnInterruptedFirstPublicationAsync(JournalDatabases databases)
    {
        var interrupted = await StopAfterSlotTransitionAsync(databases, "version-1");
        await using var node = new PublishingNode(databases);
        await node.AssertLaggingAsync(interrupted);
        await node.AssertServingAsync();

        var republished = await node.PublishAsync("version-1");

        Assert.Equal(interrupted, republished.PublicationId);
        await node.AssertConvergedAsync(interrupted);
        await node.AssertServingAsync(interrupted);
    }

    /// <summary>
    /// Nothing publishes a designer-published workflow again on its own, so shell start converges its journal. A second
    /// start changes nothing.
    /// </summary>
    private static async Task ShellStartConvergesAnInterruptedReplacementAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        var interrupted = await StopAfterSlotTransitionAsync(databases, "version-2");
        await using var node = new PublishingNode(databases);

        await node.StartShellAsync();

        await node.AssertConvergedAsync(interrupted, first);
        await node.AssertServingAsync(interrupted);
        var converged = await node.JournalAsync();
        await node.StartShellAsync();
        Assert.Equal(converged, await node.JournalAsync());
    }

    /// <summary>
    /// Replacing an interrupted replacement completes it, journal included, before replacing it. Retiring it as the
    /// replaced publication then finds it active rather than a candidate, so no journal transition fails.
    /// </summary>
    private static async Task ReplacingAnInterruptedReplacementRetiresItAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        var interrupted = await StopAfterSlotTransitionAsync(databases, "version-2");
        await using var node = new PublishingNode(databases);

        var replacement = await node.PublishAsync("version-3");

        Assert.True(replacement.WasCreated);
        await node.AssertConvergedAsync(replacement.PublicationId, first, interrupted);
        await node.AssertServingAsync(replacement.PublicationId);
        Assert.DoesNotContain(node.Log.Entries, entry => entry.Level >= LogLevel.Error);
    }

    /// <summary>
    /// A process can also stop after the runtime finished, before the journal is written. The runtime then has nothing
    /// left to complete, so the journal has to be compared with the slot, not told by the runtime that it completed
    /// something.
    /// </summary>
    private static async Task SameVersionRepublishConvergesAfterTheRuntimeFinishedAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        PauseOnTransition? stopping = null;
        await using (var stopped = new PublishingNode(databases, wrapRecords: records => stopping = new PauseOnTransition(records, transitionNumber: 1)))
        {
            var publish = stopped.PublishAsync("version-2");
            Assert.Same(stopping!.Paused, await Task.WhenAny(publish, stopping.Paused));
        }

        await using var node = new PublishingNode(databases);
        var interrupted = (await node.SlotPublicationAsync())!;
        await node.AssertLaggingAsync(interrupted, first);
        await node.AssertServingAsync(interrupted);

        var republished = await node.PublishAsync("version-2");

        Assert.Equal(interrupted, republished.PublicationId);
        await node.AssertConvergedAsync(interrupted, first);
    }

    /// <summary>
    /// The direction a convergence must not paper over: when the runtime cannot complete the slot's activation, here
    /// because two other activations still serve it, the publication does not serve. A republish is then refused as a
    /// failed activation, not answered as published and not failed as a server error, and the journal is left as it was.
    /// </summary>
    private static async Task SameVersionRepublishOfAPublicationThatCannotServeIsRefusedAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        await using (var stray = new PublishingNode(databases))
            await stray.ServeStrayAsync("activation-stray", "version-stray");
        var interrupted = await StopAfterSlotTransitionAsync(databases, "version-2");
        await using var node = new PublishingNode(databases);

        var refusal = await Assert.ThrowsAsync<PublicationActivationException>(() => node.PublishAsync("version-2"));

        Assert.Equal(PublicationFailureCodes.ProjectionActivationFailed, refusal.Code);
        await node.AssertLaggingAsync(interrupted, first);
        await node.AssertServingAsync(first, "activation-stray");
    }

    /// <summary>
    /// Marking the slot's publication active is the last journal write, so a process that stops just before it has
    /// retired the publication it replaced and left the slot's own record a candidate. That record lags, and the next
    /// completion finishes the job; had it been written first, nothing would be left to tell the journal it was behind.
    /// </summary>
    private static async Task AStopBeforeTheLastJournalWriteLeavesTheCandidateLaggingAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        PauseOnTransition? stopping = null;
        await using (var stopped = new PublishingNode(databases, wrapRecords: records => stopping = new PauseOnTransition(records, transitionNumber: 2)))
        {
            var publish = stopped.PublishAsync("version-2");
            Assert.Same(stopping!.Paused, await Task.WhenAny(publish, stopping.Paused));
        }

        await using var node = new PublishingNode(databases);
        var interrupted = (await node.SlotPublicationAsync())!;
        await node.AssertLaggingAsync(interrupted);
        await node.AssertStatusAsync(first, PublicationStatus.Retired);
        await node.AssertServingAsync(interrupted);

        var completion = await node.CompleteAsync();

        Assert.True(completion.Succeeded);
        Assert.Equal(interrupted, completion.Publication!.PublicationId);
        await node.AssertConvergedAsync(interrupted, first);
        await node.AssertServingAsync(interrupted);
    }

    /// <summary>
    /// Two nodes complete the same lagging slot at once; every journal transition is a compare-and-swap, so the journal
    /// settles once. The runtime has completed the activation by then, so the race is the journal's own: two runtime
    /// completions racing on one slot are Runtime's concern, and can fail one of them on the projection store.
    /// </summary>
    private static async Task TwoNodesCompletingOneSlotConvergeAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        var interrupted = await StopAfterSlotTransitionAsync(databases, "version-2");
        await using var one = new PublishingNode(databases);
        await using var other = new PublishingNode(databases);
        await one.CompleteRuntimeAsync();

        var completions = await Task.WhenAll(one.CompleteAsync(), other.CompleteAsync());

        Assert.All(completions, completion => Assert.Equal(interrupted, completion.Publication!.PublicationId));
        await one.AssertConvergedAsync(interrupted, first);
        await one.AssertServingAsync(interrupted);
        Assert.DoesNotContain(one.Log.Entries.Concat(other.Log.Entries), entry => entry.Level >= LogLevel.Error);
    }

    /// <summary>
    /// A slot transition that commits and then throws, as one whose connection drops after the commit does. The slot names
    /// the new publication, so the runtime completes its activation rather than compensating it (#2251), and the publish
    /// succeeds. The coordinator reports the publication it replaced, so the activator retires that record. Otherwise both
    /// would stay active, and no completion would correct it, because the record the slot names would not lag.
    /// </summary>
    private static async Task APublishWhoseSlotTransitionCommitsAndThenThrowsConvergesAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        await using var node = new PublishingNode(databases, wrapAuthority: authority => new ThrowAfterSlotTransition(authority));

        var published = (await node.PublishAsync("version-2")).PublicationId;

        await node.AssertConvergedAsync(published, first);
        await node.AssertServingAsync(published);
    }

    private static async Task<string> PublishAsync(JournalDatabases databases, string versionId)
    {
        await using var node = new PublishingNode(databases);
        return (await node.PublishAsync(versionId)).PublicationId;
    }

    /// <summary>Publishes in a process that stops for good once its slot transition commits, and returns the publication the slot then names.</summary>
    private static async Task<string> StopAfterSlotTransitionAsync(JournalDatabases databases, string versionId)
    {
        PauseAfterSlotTransition? stopping = null;
        await using (var stopped = new PublishingNode(databases, wrapAuthority: authority => stopping = new PauseAfterSlotTransition(authority)))
        {
            var publish = stopped.PublishAsync(versionId);
            Assert.Same(stopping!.Paused, await Task.WhenAny(publish, stopping.Paused));
        }

        await using var node = new PublishingNode(databases);
        return (await node.SlotPublicationAsync())!;
    }

    private static string ArtifactId(string versionId) => $"artifact-{versionId}";

    private static WorkflowExecutable Executable(string versionId) => new(
        new WorkflowExecutableIdentity(ArtifactId(versionId), DefinitionId, versionId, "1.0.0", $"sha256:{ArtifactId(versionId)}"),
        new ExecutableNode(
            NodeId,
            NodeId,
            "test/activity",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>()),
        new Dictionary<string, WorkflowExecutableResumeTarget>(),
        DateTimeOffset.UtcNow,
        new Dictionary<string, string>(),
        IncidentStrategyBuiltIns.FaultReference);

    /// <summary>One process over the shared databases: its own contexts, EF stores, coordinator, activator, publish handler and shell-start passes.</summary>
    private sealed class PublishingNode : IAsyncDisposable
    {
        private readonly PublishingSnapshotReviewDbContext _publishing;
        private readonly RuntimeDbContext _runtime;
        private readonly WorkflowTriggerIndexer _indexer;
        private readonly PublishWorkflowRequestHandler _handler;
        private readonly CompleteInterruptedActivationsStartupTask _runtimeShellStart;
        private readonly CompleteInterruptedPublicationsStartupTask _publishingShellStart;

        public PublishingNode(
            JournalDatabases databases,
            Func<IWorkflowActivationAuthority, IWorkflowActivationAuthority>? wrapAuthority = null,
            Func<IPublicationRecordStore, IPublicationRecordStore>? wrapRecords = null)
        {
            _publishing = databases.Provider.Publishing(databases.Publishing, []);
            _runtime = databases.Provider.Runtime(databases.Runtime, []);
            var access = TestAccess.Scoped("tenant-journal");
            var time = TimeProvider.System;
            var executables = new EfWorkflowExecutableStore(_runtime, access);
            var extractor = new OneTriggerExtractor();
            Records = new EfPublicationRecordStore(_publishing, access);
            Authority = new EfWorkflowActivationAuthority(_runtime, access);
            Bindings = new EfWorkflowTriggerBindingStore(_runtime, access);
            References = new EfWorkflowExecutableSourceReferenceStore(_runtime, access, ActivityPublicationScope.RecoveryCodec);
            _indexer = new WorkflowTriggerIndexer(extractor, Bindings);
            var authority = wrapAuthority?.Invoke(Authority) ?? Authority;
            var records = wrapRecords?.Invoke(Records) ?? Records;
            var coordinator = new WorkflowActivationCoordinator(
                authority,
                References,
                new WorkflowExecutableRootWriteLeaseManager(executables, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), time),
                time,
                _indexer,
                Bindings,
                logger: NullLogger<WorkflowActivationCoordinator>.Instance);
            Activator = new PublicationActivator(coordinator, records, authority, References, time, Log);
            _handler = new PublishWorkflowRequestHandler(
                new FixedCompiler(),
                executables,
                References,
                extractor,
                Bindings,
                new EmptyWorkflowDefinitionVersionLayoutStore(),
                Authority,
                new InMemoryPublicationPolicyStore(),
                new PublicationPolicyResolver(),
                records,
                new PublicationPreflightService(),
                Activator,
                time,
                workflowVersionStore: new FixedVersionStore(),
                expressionValidator: new ValidExpressions());
            var slots = new OccupiedActivationSlots(References, Authority, time);
            _runtimeShellStart = new(slots, coordinator, NullLogger<CompleteInterruptedActivationsStartupTask>.Instance);
            _publishingShellStart = new(slots, Activator, NullLogger<CompleteInterruptedPublicationsStartupTask>.Instance);
        }

        public RecordingLogger<PublicationActivator> Log { get; } = new();
        public PublicationActivator Activator { get; }
        public EfPublicationRecordStore Records { get; }
        public EfWorkflowActivationAuthority Authority { get; }
        public EfWorkflowTriggerBindingStore Bindings { get; }
        public EfWorkflowExecutableSourceReferenceStore References { get; }

        public Task<PublishedWorkflowView> PublishAsync(string versionId) =>
            _handler.Handle(new PublishWorkflow(versionId), CancellationToken.None);

        public async Task<PublicationCompletionResult> CompleteAsync() => await Activator.CompleteAsync(DefinitionId, SlotName);

        /// <summary>The runtime's shell-start pass alone, which completes the slot's activation but not the journal.</summary>
        public Task CompleteRuntimeAsync() => _runtimeShellStart.ExecuteAsync(CancellationToken.None);

        /// <summary>Both shell-start passes, in the order the task manager runs them.</summary>
        public async Task StartShellAsync()
        {
            await CompleteRuntimeAsync();
            await _publishingShellStart.ExecuteAsync(CancellationToken.None);
        }

        public async Task<string?> SlotPublicationAsync() =>
            (await Authority.FindAsync(DefinitionId, SlotName))?.ActiveActivationId;

        public async Task<IReadOnlyCollection<PublicationRecord>> JournalAsync() => await Records.ListBySlotAsync(SlotId);

        /// <summary>An activation outside publishing that serves the slot, as a stray left by an earlier fault would.</summary>
        public async Task ServeStrayAsync(string activationId, string versionId)
        {
            await References.SaveAsync(new WorkflowExecutableSourceReference(
                WorkflowActivationReferenceIdentity.Create(activationId),
                ArtifactId(versionId),
                WorkflowExecutableSourceKinds.WorkflowDefinitionVersion,
                versionId,
                "1.0.0",
                DefinitionId,
                versionId,
                "1.0.0",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                WorkflowExecutableReferenceScope.Published,
                ActivationId: activationId,
                SlotId: SlotId));
            await _indexer.PrepareActivationAsync(Executable(versionId), activationId, SlotId);
            await Bindings.ActivateAsync(activationId, null);
        }

        /// <summary>Asserts which activations serve, through the query the stimulus router uses.</summary>
        public async Task AssertServingAsync(params string[] activationIds)
        {
            var bindings = await Bindings.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery(StimulusType, StimulusHash));
            Assert.Equal(activationIds.Order(StringComparer.Ordinal), bindings.Items.Select(binding => binding.ActivationId!).Order(StringComparer.Ordinal));
        }

        /// <summary>
        /// Asserts the journal an interrupted publish leaves behind: the slot names <paramref name="publicationId"/>, still
        /// a candidate with no activation time, and <paramref name="replaced"/> is still active.
        /// </summary>
        public async Task AssertLaggingAsync(string publicationId, params string[] replaced)
        {
            Assert.Equal(publicationId, await SlotPublicationAsync());
            var publication = await FindAsync(publicationId);
            Assert.Equal((PublicationStatus.Candidate, (DateTimeOffset?)null), (publication.Status, publication.ActivatedAt));
            foreach (var other in replaced)
                Assert.Equal(PublicationStatus.Active, (await FindAsync(other)).Status);
        }

        /// <summary>
        /// Asserts that the journal matches the slot: the slot names <paramref name="publicationId"/>, the only active
        /// publication, with an activation time, and each replaced publication is retired with both times.
        /// </summary>
        public async Task AssertConvergedAsync(string publicationId, params string[] replaced)
        {
            Assert.Equal(publicationId, await SlotPublicationAsync());
            var active = Assert.Single(await JournalAsync(), publication => publication.Status == PublicationStatus.Active);
            Assert.Equal(publicationId, active.PublicationId);
            Assert.NotNull(active.ActivatedAt);
            Assert.Null(active.RetiredAt);
            foreach (var other in replaced)
            {
                var retired = await FindAsync(other);
                Assert.Equal(PublicationStatus.Retired, retired.Status);
                Assert.NotNull(retired.ActivatedAt);
                Assert.NotNull(retired.RetiredAt);
            }
        }

        public async Task AssertStatusAsync(string publicationId, PublicationStatus status) =>
            Assert.Equal(status, (await FindAsync(publicationId)).Status);

        public async ValueTask DisposeAsync()
        {
            await _publishing.DisposeAsync();
            await _runtime.DisposeAsync();
        }

        private async Task<PublicationRecord> FindAsync(string publicationId) =>
            await Records.FindAsync(publicationId) ?? throw new InvalidOperationException($"Publication '{publicationId}' has no record.");
    }

    /// <summary>Compiles each version id to its own artifact with one start node.</summary>
    private sealed class FixedCompiler : IWorkflowExecutableCompiler
    {
        public ValueTask<WorkflowExecutable> CompileAsync(WorkflowExecutableCompileRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Executable(request.VersionId));
    }

    /// <summary>One trigger per artifact, all on the same stimulus, so every publication's binding serves the same route.</summary>
    private sealed class OneTriggerExtractor : IWorkflowTriggerBindingExtractor
    {
        public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable)
        {
            var identity = executable.Identity;
            return
            [
                new WorkflowTriggerBinding(
                    WorkflowTriggerBinding.BuildId(identity.ArtifactId, NodeId, StimulusHash),
                    identity.ArtifactId,
                    identity.DefinitionId,
                    identity.ArtifactVersion,
                    identity.ArtifactHash,
                    NodeId,
                    StimulusType,
                    StimulusHash,
                    null,
                    new Dictionary<string, string>(),
                    executable.CreatedAt)
            ];
        }
    }

    /// <summary>Every version exists; publishing reads it only to hand its state to expression validation.</summary>
    private sealed class FixedVersionStore : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkflowDefinitionVersion(DefinitionId, "1.0.0") { Id = versionId });

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => GetWithDefinitionAsync(versionId, cancellationToken);
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ValidExpressions : IExpressionDraftSemanticValidator
    {
        public ValueTask<ExpressionDraftValidationResult> ValidateAsync(WorkflowDefinitionState state, string documentScope, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ExpressionDraftValidationResult(ExpressionDraftValidationState.Valid, []));
    }

    /// <summary>
    /// Holds the <paramref name="transitionNumber"/>th journal transition for good, as a process stopping just before it
    /// would: the slot, the projections and the references are complete, and the journal has been written up to there.
    /// </summary>
    private sealed class PauseOnTransition(IPublicationRecordStore inner, int transitionNumber) : IPublicationRecordStore
    {
        private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _transitions;

        public Task Paused => _paused.Task;

        public async ValueTask<bool> TryTransitionAsync(PublicationRecord publication, PublicationStatus expectedStatus, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _transitions) == transitionNumber)
            {
                _paused.SetResult();
                await new TaskCompletionSource().Task;
            }

            return await inner.TryTransitionAsync(publication, expectedStatus, cancellationToken);
        }

        public ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default) => inner.SaveAsync(publication, cancellationToken);
        public ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default) => inner.FindAsync(publicationId, cancellationToken);
        public ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default) => inner.ListBySlotAsync(slotId, cancellationToken);
    }

    /// <summary>Commits the first slot transition and then throws, as one whose connection drops after the commit does.</summary>
    private sealed class ThrowAfterSlotTransition(IWorkflowActivationAuthority inner) : IWorkflowActivationAuthority
    {
        private int _thrown;

        public async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
        {
            var transition = await inner.TryActivateAsync(request, cancellationToken);
            if (transition.Succeeded && Interlocked.Exchange(ref _thrown, 1) == 0)
                throw new InvalidOperationException("The connection was lost after the slot transition committed.");
            return transition;
        }

        public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) => inner.FindAsync(workflowDefinitionId, slotName, cancellationToken);
        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => inner.ListByDefinitionAsync(workflowDefinitionId, cancellationToken);
        public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(string workflowDefinitionId, string slotName, WorkflowActivationSource source, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            inner.TryDeactivateAsync(workflowDefinitionId, slotName, source, expectedRevision, updatedAt, cancellationToken);
    }
}
