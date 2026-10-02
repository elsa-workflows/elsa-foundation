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
/// The publication journal across a publish that stops after its switch (#2223), written once and run on EF Core over
/// SQLite and PostgreSQL.
/// </summary>
/// <remarks>
/// A crash is a publish whose process stops for good once its switch commits (<see cref="PauseAfterSwitch"/> with nothing
/// to resume it). The slot, the projections and the replaced publication's reference switched in that one commit (#2230),
/// so the new publication serves; no journal write ran, so its record is still a candidate and the one it replaced is still
/// active. Each scenario then works through another process over the same databases, publishing through the real
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
        ["same-version-republish-repairs-a-slot-left-half-done-and-converges-the-journal"] = SameVersionRepublishRepairsASlotLeftHalfDoneAsync,
        ["shell-start-repairs-a-slot-left-half-done-and-converges-the-journal"] = ShellStartRepairsASlotLeftHalfDoneAsync,
        ["publishing-a-new-version-to-a-slot-left-half-done-repairs-it-then-replaces-it"] = PublishingANewVersionToASlotLeftHalfDoneRepairsItThenReplacesItAsync,
        ["same-version-republish-of-a-slot-left-half-done-that-cannot-be-repaired-is-refused"] = SameVersionRepublishOfASlotLeftHalfDoneThatCannotBeRepairedIsRefusedAsync,
        ["a-stop-before-the-last-journal-write-leaves-the-candidate-lagging-until-the-next-completion"] = AStopBeforeTheLastJournalWriteLeavesTheCandidateLaggingAsync,
        ["two-nodes-completing-one-slot-converge-to-one-journal-state"] = TwoNodesCompletingOneSlotConvergeAsync,
        ["a-publish-whose-switch-commits-and-then-throws-converges-the-journal"] = APublishWhoseSwitchCommitsAndThenThrowsConvergesAsync,
        ["a-publish-whose-switch-commits-and-then-throws-beside-a-leaked-leftover-retires-the-replaced-record"] = APublishWhoseSwitchCommitsAndThenThrowsBesideALeakedLeftoverAsync,
        ["a-same-version-publish-that-read-the-slot-before-it-moved-is-answered-with-the-slots-publication"] = SameVersionPublishThatReadTheSlotBeforeItMovedAsync,
        ["two-same-version-publishes-racing-leave-one-active-record"] = TwoSameVersionPublishesRacingAsync
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
    /// The direction that looks like success: the new publication serves and nothing failed, yet its record is a candidate
    /// and the one it replaced is still active. Publishing the same version again used to find it already published and
    /// fail to describe it; it now brings the journal into line and answers with it.
    /// </summary>
    private static async Task SameVersionRepublishConvergesAnInterruptedReplacementAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        var interrupted = await StopAfterSwitchAsync(databases, "version-2");
        await using var node = new PublishingNode(databases);
        await node.AssertLaggingAsync(interrupted, first);
        await node.AssertServingAsync(interrupted);

        var republished = await node.PublishAsync("version-2");

        Assert.False(republished.WasCreated);
        Assert.Equal(interrupted, republished.PublicationId);
        Assert.Equal(PublicationStatusView.Active, republished.Status);
        await node.AssertConvergedAsync(interrupted, first);
        await node.AssertServingAsync(interrupted);
        Assert.Equal(interrupted, (await node.PublishAsync("version-2")).PublicationId);
        await node.AssertConvergedAsync(interrupted, first);
    }

    /// <summary>A first publication has nothing to replace; it serves, and its record still lags the slot until it is completed.</summary>
    private static async Task SameVersionRepublishConvergesAnInterruptedFirstPublicationAsync(JournalDatabases databases)
    {
        var interrupted = await StopAfterSwitchAsync(databases, "version-1");
        await using var node = new PublishingNode(databases);
        await node.AssertLaggingAsync(interrupted);
        await node.AssertServingAsync(interrupted);

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
        var interrupted = await StopAfterSwitchAsync(databases, "version-2");
        await using var node = new PublishingNode(databases);

        await node.StartShellAsync();

        await node.AssertConvergedAsync(interrupted, first);
        await node.AssertServingAsync(interrupted);
        var converged = await node.JournalAsync();
        await node.StartShellAsync();
        Assert.Equal(converged, await node.JournalAsync());
    }

    /// <summary>
    /// Replacing an interrupted replacement brings its journal into line before replacing it. Retiring it as the replaced
    /// publication then finds it active rather than a candidate, so no journal transition fails.
    /// </summary>
    private static async Task ReplacingAnInterruptedReplacementRetiresItAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        var interrupted = await StopAfterSwitchAsync(databases, "version-2");
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
    /// A version before #2230 committed the slot transition before the projection switch, and one that stopped between them
    /// left the slot naming a publication that does not serve, beside the one it replaced. A same-version republish
    /// repairs the slot, brings the journal into line, and is answered with the publication.
    /// </summary>
    private static Task SameVersionRepublishRepairsASlotLeftHalfDoneAsync(JournalDatabases databases) =>
        RepairsASlotLeftHalfDoneAsync(databases, async (node, interrupted) =>
        {
            var republished = await node.PublishAsync("version-2");
            Assert.Equal((interrupted, PublicationStatusView.Active), (republished.PublicationId, republished.Status));
        });

    /// <summary>Nothing publishes a designer-published workflow again on its own, so shell start repairs such a slot too.</summary>
    private static Task ShellStartRepairsASlotLeftHalfDoneAsync(JournalDatabases databases) =>
        RepairsASlotLeftHalfDoneAsync(databases, (node, _) => node.StartShellAsync());

    private static async Task RepairsASlotLeftHalfDoneAsync(JournalDatabases databases, Func<PublishingNode, string, Task> meet)
    {
        var first = await PublishAsync(databases, "version-1");
        var interrupted = await StopAfterSwitchAsync(databases, "version-2", slotOnly: true);
        await using var node = new PublishingNode(databases);
        await node.AssertServingAsync(first);

        await meet(node, interrupted);

        await node.AssertConvergedAsync(interrupted, first);
        await node.AssertServingAsync(interrupted);
    }

    /// <summary>Publishing a new version to such a slot repairs it first, then replaces its publication as it would any other.</summary>
    private static async Task PublishingANewVersionToASlotLeftHalfDoneRepairsItThenReplacesItAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        var interrupted = await StopAfterSwitchAsync(databases, "version-2", slotOnly: true);
        await using var node = new PublishingNode(databases);

        var replacement = await node.PublishAsync("version-3");

        Assert.True(replacement.WasCreated);
        await node.AssertConvergedAsync(replacement.PublicationId, first, interrupted);
        await node.AssertServingAsync(replacement.PublicationId);
    }

    /// <summary>
    /// The direction a convergence must not paper over: a slot left half done whose publication no longer has every
    /// projection prepared cannot be repaired in place. A republish is then refused as a failed activation, not answered as
    /// published and not failed as a server error, and the journal is left as it was.
    /// </summary>
    private static async Task SameVersionRepublishOfASlotLeftHalfDoneThatCannotBeRepairedIsRefusedAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        var interrupted = await StopAfterSwitchAsync(databases, "version-2", slotOnly: true);
        await using var node = new PublishingNode(databases);
        await node.Bindings.DeleteByActivationAsync(interrupted);

        var refusal = await Assert.ThrowsAsync<PublicationActivationException>(() => node.PublishAsync("version-2"));

        Assert.Equal(PublicationFailureCodes.ProjectionActivationFailed, refusal.Code);
        await node.AssertLaggingAsync(interrupted, first);
        await node.AssertServingAsync(first);
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
    /// Two nodes complete the same lagging slot at once. The runtime has nothing to complete, and every journal transition
    /// is a compare-and-swap, so the journal settles once.
    /// </summary>
    private static async Task TwoNodesCompletingOneSlotConvergeAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        var interrupted = await StopAfterSwitchAsync(databases, "version-2");
        await using var one = new PublishingNode(databases);
        await using var other = new PublishingNode(databases);

        var completions = await Task.WhenAll(one.CompleteAsync(), other.CompleteAsync());

        Assert.All(completions, completion => Assert.Equal(interrupted, completion.Publication!.PublicationId));
        await one.AssertConvergedAsync(interrupted, first);
        await one.AssertServingAsync(interrupted);
        Assert.DoesNotContain(one.Log.Entries.Concat(other.Log.Entries), entry => entry.Level >= LogLevel.Error);
    }

    /// <summary>
    /// A switch that commits and then throws, as one whose connection drops after the commit does. The slot names the new
    /// publication and it serves, so the runtime reports it activated rather than compensating it (#2251), and the publish
    /// succeeds. The coordinator reports the publication it replaced, so the activator retires that record. Otherwise both
    /// would stay active, and no completion would correct it, because the record the slot names would not lag.
    /// </summary>
    private static async Task APublishWhoseSwitchCommitsAndThenThrowsConvergesAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        await using var node = new PublishingNode(databases, wrapSwitch: (inner, _) => new ThrowAfterSwitch(inner));

        var published = (await node.PublishAsync("version-2")).PublicationId;

        await node.AssertConvergedAsync(published, first);
        await node.AssertServingAsync(published);
    }

    /// <summary>
    /// The same publish beside a leaked leftover: the publication two back, switched off with a reference made live again.
    /// The coordinator reports the publication the new one replaced, read from the slot the switch moved from, not the
    /// leftover, so the activator retires the replaced record and leaves the leftover's as it was. Reporting the leftover
    /// would leave the replaced record active beside the new one for good, because the record the slot names does not lag.
    /// </summary>
    private static async Task APublishWhoseSwitchCommitsAndThenThrowsBesideALeakedLeftoverAsync(JournalDatabases databases)
    {
        var leftover = await PublishAsync(databases, "version-1");
        var replaced = await PublishAsync(databases, "version-2");
        await using var other = new PublishingNode(databases);
        var leftoverRecord = await other.Records.FindAsync(leftover);
        var leaked = false;
        await using var node = new PublishingNode(
            databases,
            wrapSwitch: (inner, _) => new ThrowAfterSwitch(inner, afterCommit: async () =>
            {
                await other.LeakReferenceAsync(leftover);
                leaked = true;
            }));

        var published = (await node.PublishAsync("version-3")).PublicationId;

        Assert.True(leaked);
        await node.AssertConvergedAsync(published, replaced, leftover);
        Assert.Equal(leftoverRecord, await node.Records.FindAsync(leftover));
        await node.AssertServingAsync(published);
    }

    /// <summary>
    /// The loser of a same-version race preflights before the winner's slot transition and so skips the handler's early
    /// return; its candidate then reaches a coordinator that finds the artifact already serving. It is answered with the
    /// winner's publication, and its own record is failed: a second active record would hold a source reference nothing
    /// minted, and nothing would ever retire it.
    /// </summary>
    private static async Task SameVersionPublishThatReadTheSlotBeforeItMovedAsync(JournalDatabases databases)
    {
        var first = await PublishAsync(databases, "version-1");
        await using var loser = new PublishingNode(databases, preflightSlotsBeforeTheyMoved: true);

        var republished = await loser.PublishAsync("version-1");

        Assert.False(republished.WasCreated);
        Assert.Equal(first, republished.PublicationId);
        Assert.Equal(PublicationStatusView.Active, republished.Status);
        await loser.AssertConvergedAsync(first);
        await loser.AssertServingAsync(first);
        var twin = Assert.Single(await loser.JournalAsync(), publication => publication.PublicationId != first);
        Assert.Equal((PublicationStatus.Failed, PublicationFailureCodes.ArtifactAlreadyServing), (twin.Status, twin.Failure?.Code));
    }

    /// <summary>
    /// Two nodes publish one version at once. Whichever interleaving the coordinator sees, exactly one record of the slot
    /// is active, it is the one the slot names, and no caller is told its publication serves when it does not.
    /// </summary>
    private static async Task TwoSameVersionPublishesRacingAsync(JournalDatabases databases)
    {
        await using var one = new PublishingNode(databases);
        await using var other = new PublishingNode(databases);

        var outcomes = await Task.WhenAll(Attempt(one), Attempt(other));

        var published = outcomes.Select(outcome => outcome.View).OfType<PublishedWorkflowView>().ToList();
        Assert.All(outcomes.Select(outcome => outcome.Refusal).OfType<PublicationActivationException>(), refusal =>
            Assert.Contains(refusal.Code, new[] { PublicationFailureCodes.SlotRevisionConflict, PublicationFailureCodes.ProjectionActivationFailed }));
        Assert.NotEmpty(published);
        var slot = (await one.SlotPublicationAsync())!;
        Assert.All(published, view => Assert.Equal(slot, view.PublicationId));
        await one.AssertConvergedAsync(slot);
        await one.AssertServingAsync(slot);

        static async Task<(PublishedWorkflowView? View, Exception? Refusal)> Attempt(PublishingNode node)
        {
            try
            {
                return (await node.PublishAsync("version-1"), null);
            }
            catch (PublicationActivationException refusal)
            {
                return (null, refusal);
            }
        }
    }

    private static async Task<string> PublishAsync(JournalDatabases databases, string versionId)
    {
        await using var node = new PublishingNode(databases);
        return (await node.PublishAsync(versionId)).PublicationId;
    }

    /// <summary>
    /// Publishes in a process that stops for good once its switch commits, and returns the publication the slot then names.
    /// With <paramref name="slotOnly"/> the switch moves the slot alone, as a version before #2230 did before it stopped.
    /// </summary>
    private static async Task<string> StopAfterSwitchAsync(JournalDatabases databases, string versionId, bool slotOnly = false)
    {
        PauseAfterSwitch? stopping = null;
        await using (var stopped = new PublishingNode(databases, wrapSwitch: (inner, authority) => stopping = new PauseAfterSwitch(slotOnly ? new SlotOnlySwitch(inner, authority) : inner)))
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

    /// <summary>One process over the shared databases: its own contexts, EF stores, coordinator, activator, publish handler and shell-start pass.</summary>
    private sealed class PublishingNode : IAsyncDisposable
    {
        private readonly PublishingSnapshotReviewDbContext _publishing;
        private readonly RuntimeDbContext _runtime;
        private readonly PublishWorkflowRequestHandler _handler;
        private readonly CompleteInterruptedPublicationsStartupTask _publishingShellStart;

        public PublishingNode(
            JournalDatabases databases,
            Func<IWorkflowActivationSwitch, IWorkflowActivationAuthority, IWorkflowActivationSwitch>? wrapSwitch = null,
            Func<IPublicationRecordStore, IPublicationRecordStore>? wrapRecords = null,
            bool preflightSlotsBeforeTheyMoved = false)
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
            var indexer = new WorkflowTriggerIndexer(extractor, Bindings);
            var activationSwitch = new EfWorkflowActivationSwitch(Authority, Bindings, References, access, time);
            var records = wrapRecords?.Invoke(Records) ?? Records;
            var coordinator = new WorkflowActivationCoordinator(
                Authority,
                wrapSwitch?.Invoke(activationSwitch, Authority) ?? activationSwitch,
                References,
                new WorkflowExecutableRootWriteLeaseManager(executables, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), time),
                time,
                indexer,
                Bindings,
                logger: NullLogger<WorkflowActivationCoordinator>.Instance);
            Activator = new PublicationActivator(coordinator, records, Authority, References, time, Log);
            _handler = new PublishWorkflowRequestHandler(
                new FixedCompiler(),
                executables,
                References,
                extractor,
                Bindings,
                new EmptyWorkflowDefinitionVersionLayoutStore(),
                preflightSlotsBeforeTheyMoved ? new SlotsBeforeTheyMoved(Authority) : Authority,
                new InMemoryPublicationPolicyStore(),
                new PublicationPolicyResolver(),
                records,
                new PublicationPreflightService(),
                Activator,
                time,
                workflowVersionStore: new FixedVersionStore(),
                expressionValidator: new ValidExpressions());
            _publishingShellStart = new(new OccupiedActivationSlots(References, Authority, time), Activator, NullLogger<CompleteInterruptedPublicationsStartupTask>.Instance);
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

        /// <summary>Publishing's shell-start pass; the runtime has nothing to complete at shell start (#2230).</summary>
        public Task StartShellAsync() => _publishingShellStart.ExecuteAsync(CancellationToken.None);

        public async Task<string?> SlotPublicationAsync() =>
            (await Authority.FindAsync(DefinitionId, SlotName))?.ActiveActivationId;

        public async Task<IReadOnlyCollection<PublicationRecord>> JournalAsync() => await Records.ListBySlotAsync(SlotId);

        /// <summary>
        /// Makes a replaced publication's retired source reference live again: a leftover that an earlier version's
        /// completion could leak when it could not retire one.
        /// </summary>
        public async Task LeakReferenceAsync(string publicationId)
        {
            var retired = await ReferenceAsync(publicationId);
            Assert.True(await References.TryRestoreAsync(retired, retired with { DeletedAt = null, DeletedReason = null }));
        }

        public async Task<WorkflowExecutableSourceReference> ReferenceAsync(string publicationId) =>
            await References.FindAsync(WorkflowActivationReferenceIdentity.Create(publicationId))
            ?? throw new InvalidOperationException($"Publication '{publicationId}' has no source reference.");

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

    /// <summary>Preflight as a publish that read the slots before another node's activation moved them: every slot is empty.</summary>
    private sealed class SlotsBeforeTheyMoved(IWorkflowActivationAuthority inner) : IWorkflowActivationAuthority
    {
        public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowActivationSlot?>(null);

        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowActivationSlot>>([]);

        public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) =>
            inner.TryActivateAsync(request, cancellationToken);

        public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
            string workflowDefinitionId,
            string slotName,
            WorkflowActivationSource source,
            long expectedRevision,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) =>
            inner.TryDeactivateAsync(workflowDefinitionId, slotName, source, expectedRevision, updatedAt, cancellationToken);
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

    /// <summary>
    /// Commits the first switch and then throws, as one whose connection drops after the commit does. Runs
    /// <c>afterCommit</c>, when given, in between.
    /// </summary>
    private sealed class ThrowAfterSwitch(IWorkflowActivationSwitch inner, Func<Task>? afterCommit = null) : ForwardingActivationSwitch(inner)
    {
        private int _thrown;

        public override async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
        {
            var transition = await base.TryActivateAsync(request, cancellationToken);
            if (!transition.Succeeded || Interlocked.Exchange(ref _thrown, 1) != 0)
                return transition;
            if (afterCommit is not null)
                await afterCommit();
            throw new InvalidOperationException("The connection was lost after the switch committed.");
        }
    }

    /// <summary>
    /// Moves the slot alone, as a version before #2230 did: it committed the slot transition before the projection switch,
    /// so a process that stopped between them left the slot naming an activation that serves nothing.
    /// </summary>
    private sealed class SlotOnlySwitch(IWorkflowActivationSwitch inner, IWorkflowActivationAuthority authority) : ForwardingActivationSwitch(inner)
    {
        public override ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) =>
            authority.TryActivateAsync(request, cancellationToken);
    }
}
