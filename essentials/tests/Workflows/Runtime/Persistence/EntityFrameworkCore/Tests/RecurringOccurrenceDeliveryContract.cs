using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Recurring-trigger occurrences are delivered at least once and start once (#2198), on a real database, written once so
/// SQLite and each native provider are held to the same outcome. Each pump has a schedule store on a context of its own,
/// and starts through a whole runtime node, so a second start would show as a second execution. A node "dies" by stopping
/// mid-sweep: its pump runs nothing after the point it stopped at, neither a settlement nor a release.
/// </summary>
internal static class RecurringOccurrenceDeliveryContract
{
    private const string TriggerNodeId = "recurring-start-trigger";
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = Now.AddSeconds(-30);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);
    private static readonly DateTimeOffset AfterLease = Now + Lease + TimeSpan.FromSeconds(1);

    // The started workflow waits on an event, so it stays running: a start that ran twice would collide with it rather
    // than meet a terminal execution the drainer refuses to touch.
    private static readonly WorkflowExecutable Executable = RuntimeEventExecutableTestFixture.Create("recurring-start");

    // A republish of the same workflow: another artifact, the same trigger node.
    private static readonly WorkflowExecutable Republished = RuntimeEventExecutableTestFixture.Create("recurring-start", version: 2);

    // The slot both publications serve, named as activation names it: by the workflow definition and the slot name.
    private static readonly string Slot = WorkflowActivationSlotIdentity.Create(Executable.Identity.DefinitionId, "default");

    // A Cron that never fires again: February 30 does not exist.
    private const string ExhaustedCron = "0 0 30 2 *";

    /// <summary>
    /// The occurrence is claimed and the node dies before routing it. The dead node's lease keeps a peer off it until it
    /// lapses; the peer then starts the workflow, once.
    /// </summary>
    public static async Task AClaimantThatDiesBeforeRoutingLeavesTheOccurrenceToAPeerAsync(
        string provider,
        string connectionString,
        Func<IInterceptor[], RuntimeDbContext> createContext)
    {
        await using var deliveries = await Deliveries.StartAsync(provider, connectionString, createContext);
        var schedule = await deliveries.PublishRecurringStartWorkflowAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            deliveries.Pump(new DyingRouter(deliveries.Router, afterRouting: false), Now).ExecuteAsync(CancellationToken.None));

        var peerRouter = new RecordingRouter(deliveries.Router);
        await deliveries.Pump(peerRouter, Now).ExecuteAsync(CancellationToken.None);
        Assert.Empty(peerRouter.Results);

        await deliveries.Pump(peerRouter, AfterLease).ExecuteAsync(CancellationToken.None);

        Assert.Equal(StimulusStartStatus.Started, Assert.Single(Assert.Single(peerRouter.Results).Starts).Status);
        await KeyedStartNodes.AssertStartedOnceAsync(deliveries.Node, KeyedExecutionId(schedule));
        Assert.Equal(AfterLease.AddMinutes(1), (await deliveries.Store().FindAsync(schedule.ScheduleId))!.NextOccurrence);
    }

    /// <summary>
    /// The node routes the occurrence, which starts the workflow, and dies before settling it. The peer fires the occurrence
    /// again once the lease lapses, with the same key, and converges on the execution the first fire started.
    /// </summary>
    public static async Task AClaimantThatDiesAfterRoutingIsRepeatedByAPeerWithoutASecondStartAsync(
        string provider,
        string connectionString,
        Func<IInterceptor[], RuntimeDbContext> createContext)
    {
        await using var deliveries = await Deliveries.StartAsync(provider, connectionString, createContext);
        var schedule = await deliveries.PublishRecurringStartWorkflowAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            deliveries.Pump(new DyingRouter(deliveries.Router, afterRouting: true), Now).ExecuteAsync(CancellationToken.None));
        await KeyedStartNodes.AssertStartedOnceAsync(deliveries.Node, KeyedExecutionId(schedule));

        var peerRouter = new RecordingRouter(deliveries.Router);
        await deliveries.Pump(peerRouter, AfterLease).ExecuteAsync(CancellationToken.None);

        Assert.Equal(StimulusStartStatus.SkippedDuplicate, Assert.Single(Assert.Single(peerRouter.Results).Starts).Status);
        await KeyedStartNodes.AssertStartedOnceAsync(deliveries.Node, KeyedExecutionId(schedule));
        Assert.Equal(AfterLease.AddMinutes(1), (await deliveries.Store().FindAsync(schedule.ScheduleId))!.NextOccurrence);
    }

    /// <summary>
    /// Two pumps both read the due occurrence before either claims it: each is held at its claim's write until the other
    /// reaches its own. Exactly one claim holds, so the occurrence is routed once and starts once.
    /// </summary>
    public static async Task TwoPumpsRacingOnOneOccurrenceRouteAndStartItOnceAsync(
        string provider,
        string connectionString,
        Func<IInterceptor[], RuntimeDbContext> createContext)
    {
        await using var deliveries = await Deliveries.StartAsync(provider, connectionString, createContext);
        var schedule = await deliveries.PublishRecurringStartWorkflowAsync();
        var rendezvous = new ClaimWriteRendezvous(participants: 2);
        var router = new RecordingRouter(deliveries.Router);

        await Task.WhenAll(
            deliveries.Pump(router, Now, rendezvous.Participant()).ExecuteAsync(CancellationToken.None),
            deliveries.Pump(router, Now, rendezvous.Participant()).ExecuteAsync(CancellationToken.None));

        Assert.Equal(StimulusStartStatus.Started, Assert.Single(Assert.Single(router.Results).Starts).Status);
        await KeyedStartNodes.AssertStartedOnceAsync(deliveries.Node, KeyedExecutionId(schedule));
    }

    /// <summary>
    /// A pump of the replaced publication has claimed the due occurrence and not routed it yet when the replacement is
    /// activated, through the store's activation as publication activation drives it. The activation hands the occurrence to
    /// the replacement and fences the claim out, so the replaced pump can neither renew nor settle it, and the replacement
    /// fires the occurrence, starting the workflow once.
    /// </summary>
    public static async Task AReplacementActivatedWhileTheReplacedPublicationHoldsTheOccurrenceFiresItOnceAsync(
        string provider,
        string connectionString,
        Func<IInterceptor[], RuntimeDbContext> createContext)
    {
        await using var deliveries = await Deliveries.StartAsync(provider, connectionString, createContext);
        await deliveries.ActivatePublicationAsync(Executable, "publication-a", replacedActivationId: null, next: Due, createdAt: Due.AddHours(-1));
        var replacedClaim = Assert.Single(await deliveries.Store().ClaimDueAsync(new("replaced-pump", Now, Lease, 10)));

        var replacement = await deliveries.ActivatePublicationAsync(Republished, "publication-b", "publication-a", next: Now.AddSeconds(50), createdAt: Now.AddSeconds(-10));

        Assert.Equal(Due, replacement.NextOccurrence);
        Assert.Null(await deliveries.Store().RenewClaimAsync(replacedClaim, Now, Lease));
        Assert.False(await deliveries.Store().SettleClaimAsync(replacedClaim, Now.AddMinutes(1)));

        var router = new RecordingRouter(deliveries.Router);
        await deliveries.Pump(router, Now).ExecuteAsync(CancellationToken.None);
        await deliveries.Pump(router, Now).ExecuteAsync(CancellationToken.None);

        Assert.Equal(StimulusStartStatus.Started, Assert.Single(Assert.Single(router.Results).Starts).Status);
        await KeyedStartNodes.AssertStartedOnceAsync(deliveries.Node, KeyedExecutionId(replacement));
        Assert.Equal(Now.AddMinutes(1), (await deliveries.Store().FindAsync(replacement.ScheduleId))!.NextOccurrence);
    }

    /// <summary>
    /// The replaced publication already routed the due occurrence, which started the workflow, and its pump died before
    /// settling. The replacement activated after it fires the same occurrence, under the same key, and converges on that
    /// start although it serves another artifact: no second start.
    /// </summary>
    public static async Task AReplacementActivatedAfterTheReplacedPublicationRoutedTheOccurrenceDoesNotStartItAgainAsync(
        string provider,
        string connectionString,
        Func<IInterceptor[], RuntimeDbContext> createContext)
    {
        await using var deliveries = await Deliveries.StartAsync(provider, connectionString, createContext);
        var replaced = await deliveries.ActivatePublicationAsync(Executable, "publication-a", replacedActivationId: null, next: Due, createdAt: Due.AddHours(-1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            deliveries.Pump(new DyingRouter(deliveries.Router, afterRouting: true), Now).ExecuteAsync(CancellationToken.None));
        await KeyedStartNodes.AssertStartedOnceAsync(deliveries.Node, KeyedExecutionId(replaced));

        var replacement = await deliveries.ActivatePublicationAsync(Republished, "publication-b", "publication-a", next: Now.AddSeconds(50), createdAt: Now.AddSeconds(-10));
        var router = new RecordingRouter(deliveries.Router);
        await deliveries.Pump(router, Now).ExecuteAsync(CancellationToken.None);

        Assert.Equal(Due, replacement.NextOccurrence);
        Assert.Equal(replaced.BuildOccurrenceKey(), Assert.Single(router.Requests).IdempotencyKey);
        Assert.Equal(StimulusStartStatus.SkippedDuplicate, Assert.Single(Assert.Single(router.Results).Starts).Status);
        await KeyedStartNodes.AssertStartedOnceAsync(deliveries.Node, KeyedExecutionId(replaced));
    }

    /// <summary>
    /// A Cron with no occurrence after the one in its cursor fires that last occurrence, starting the workflow once, and
    /// only then is the schedule deleted.
    /// </summary>
    public static async Task AnExhaustedCronFiresItsLastOccurrenceOnceAndIsThenDeletedAsync(
        string provider,
        string connectionString,
        Func<IInterceptor[], RuntimeDbContext> createContext)
    {
        await using var deliveries = await Deliveries.StartAsync(provider, connectionString, createContext);
        var schedule = await deliveries.PublishRecurringStartWorkflowAsync(RecurringScheduleKind.Cron, ExhaustedCron);
        var router = new RecordingRouter(deliveries.Router);

        await deliveries.Pump(router, Now).ExecuteAsync(CancellationToken.None);

        Assert.Equal(StimulusStartStatus.Started, Assert.Single(Assert.Single(router.Results).Starts).Status);
        await KeyedStartNodes.AssertStartedOnceAsync(deliveries.Node, KeyedExecutionId(schedule));
        Assert.Null(await deliveries.Store().FindAsync(schedule.ScheduleId));
    }

    /// <summary>
    /// A republish lands while an occurrence is due and not yet fired. The re-index keeps that occurrence in the cursor
    /// instead of recomputing it from now, so the pump fires it under its own key rather than skipping to the next one.
    /// </summary>
    public static async Task ARepublishWhileAnOccurrenceIsDueKeepsItForThePumpAsync(Func<IInterceptor[], RuntimeDbContext> createContext)
    {
        await using var stores = await new EfRecurringScheduleStores(createContext).EnsureCreatedAsync();
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        var executable = TriggerExecutable("artifact-republished");
        var scheduleId = RecurringTriggerSchedule.BuildId(executable.Identity.ArtifactId, TriggerNodeId);

        // Published a minute before the occurrence, which falls due 30 seconds before the republish.
        await Indexer(stores.Create(), bindingStore, Due.AddMinutes(-1)).IndexAsync(executable);
        Assert.Equal(Due, (await stores.Create().FindAsync(scheduleId))!.NextOccurrence);
        await Indexer(stores.Create(), bindingStore, Now).IndexAsync(executable);

        var router = new RecordingRouter(new NoStartRouter());
        await Pump(stores.Create(), bindingStore, router, Now).ExecuteAsync(CancellationToken.None);

        Assert.Equal($"recurring:{scheduleId}:{Due.UtcTicks}", Assert.Single(router.Requests).IdempotencyKey);
        Assert.Equal(Now.AddMinutes(1), (await stores.Create().FindAsync(scheduleId))!.NextOccurrence);
    }

    // Every schedule here serves a slot, so its occurrence starts under the artifact-free occurrence identity.
    private static string KeyedExecutionId(RecurringTriggerSchedule schedule) =>
        KeyedWorkflowStartIdentity.ForOccurrence(schedule.BuildOccurrenceKey()).WorkflowExecutionId;

    private static RecurringTriggerPumpTask Pump(
        IRecurringTriggerScheduleStore store,
        IWorkflowTriggerBindingStore bindingStore,
        IStimulusRouter router,
        DateTimeOffset now) =>
        new(
            store,
            bindingStore,
            router,
            new RecurringScheduleCalculator(),
            Options.Create(new RecurringTriggerPumpOptions { ClaimVisibilityTimeout = Lease }),
            new FakeTimeProvider(now),
            NullLogger<RecurringTriggerPumpTask>.Instance);

    private static RecurringTriggerScheduleIndexer Indexer(
        IRecurringTriggerScheduleStore store,
        IWorkflowTriggerBindingStore bindingStore,
        DateTimeOffset now) =>
        new(
            new BindingWritingIndexer(bindingStore),
            [new EveryMinuteProvider()],
            store,
            new RecurringScheduleCalculator(),
            new FakeTimeProvider(now),
            NullLogger<RecurringTriggerScheduleIndexer>.Instance);

    // An executable whose root is a recurring start trigger, for the schedule indexer.
    private static WorkflowExecutable TriggerExecutable(string artifactId)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var root = new ExecutableNode(
            executableNodeId: TriggerNodeId,
            authoredActivityId: $"authored-{TriggerNodeId}",
            activityType: EveryMinuteProvider.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType });
        return new WorkflowExecutable(
            new WorkflowExecutableIdentity(artifactId, "republished-definition", "republished-version", "1.0.0", "republished-hash"),
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowTriggerBinding Binding(string artifactId, string? activationId = null, string? slotId = null) =>
        new(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(artifactId, TriggerNodeId, EveryMinuteProvider.StimulusHash),
            ArtifactId: artifactId,
            DefinitionId: "recurring-definition",
            ArtifactVersion: "1.0.0",
            ArtifactHash: "recurring-hash",
            ExecutableNodeId: TriggerNodeId,
            StimulusType: EveryMinuteProvider.StimulusType,
            StimulusHash: EveryMinuteProvider.StimulusHash,
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: WorkflowExecutionHarness.Timestamp,
            ActivationId: activationId,
            SlotId: slotId);

    /// <summary>One runtime node, and every pump's store and binding-store scope, disposed together.</summary>
    private sealed class Deliveries : IAsyncDisposable
    {
        private readonly EfRecurringScheduleStores _stores;
        private readonly List<AsyncServiceScope> _scopes = [];

        private Deliveries(WorkflowExecutionHarness node, EfRecurringScheduleStores stores)
        {
            Node = node;
            _stores = stores;
            Router = new KeyedStartNodes.ScopedStimulusRouter(node);
        }

        public WorkflowExecutionHarness Node { get; }
        public IStimulusRouter Router { get; }

        // The node's module migrator installs the schema, so the stores need not create it.
        public static async Task<Deliveries> StartAsync(string provider, string connectionString, Func<IInterceptor[], RuntimeDbContext> createContext) =>
            new(await KeyedStartNodes.StartAsync(provider, connectionString), new EfRecurringScheduleStores(createContext));

        public IRecurringTriggerScheduleStore Store(params IInterceptor[] interceptors) => _stores.Create(interceptors);

        public RecurringTriggerPumpTask Pump(IStimulusRouter router, DateTimeOffset now, params IInterceptor[] interceptors)
        {
            var scope = Node.Services.CreateAsyncScope();
            _scopes.Add(scope);
            return RecurringOccurrenceDeliveryContract.Pump(
                Store(interceptors), scope.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>(), router, now);
        }

        /// <summary>Publishes the workflow with its recurring trigger binding, and saves the schedule with a due occurrence.</summary>
        public async Task<RecurringTriggerSchedule> PublishRecurringStartWorkflowAsync(
            RecurringScheduleKind kind = RecurringScheduleKind.Interval,
            string expression = EveryMinuteProvider.Expression)
        {
            var reference = await Node.PublishAsync(Executable, "ref-recurring-start");
            var artifactId = Executable.Identity.ArtifactId;
            await using (var scope = Node.Services.CreateAsyncScope())
            {
                // The binding targets a node other than the root, so the root waits instead of completing as the trigger target.
                await scope.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>().SaveAsync(
                    Binding(artifactId, reference.ActivationId, reference.SlotId));
            }

            var schedule = new RecurringTriggerSchedule(
                reference.ActivationId is null
                    ? RecurringTriggerSchedule.BuildId(artifactId, TriggerNodeId)
                    : RecurringTriggerSchedule.BuildId(reference.ActivationId, artifactId, TriggerNodeId),
                artifactId,
                TriggerNodeId,
                EveryMinuteProvider.StimulusType,
                EveryMinuteProvider.StimulusHash,
                kind,
                expression,
                Due,
                Due.AddHours(-1),
                reference.ActivationId,
                reference.SlotId);
            await Store().SaveAsync(schedule);
            return schedule;
        }

        /// <summary>
        /// Publishes <paramref name="executable"/> as activation <paramref name="activationId"/> of the shared slot, with its
        /// recurring trigger binding, and prepares and activates its schedule through the store's activation, replacing
        /// <paramref name="replacedActivationId"/>. Returns the schedule as activation left it.
        /// </summary>
        public async Task<RecurringTriggerSchedule> ActivatePublicationAsync(
            WorkflowExecutable executable,
            string activationId,
            string? replacedActivationId,
            DateTimeOffset next,
            DateTimeOffset createdAt)
        {
            var artifactId = executable.Identity.ArtifactId;
            var published = await Node.PublishAsync(executable, $"ref-{activationId}");
            await using (var scope = Node.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(
                    published with { SourceReferenceId = $"ref-{activationId}-slot", ActivationId = activationId, SlotId = Slot });
                await scope.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>().SaveAsync(Binding(artifactId, activationId, Slot));
            }

            var schedule = new RecurringTriggerSchedule(
                RecurringTriggerSchedule.BuildId(activationId, artifactId, TriggerNodeId),
                artifactId,
                TriggerNodeId,
                EveryMinuteProvider.StimulusType,
                EveryMinuteProvider.StimulusHash,
                RecurringScheduleKind.Interval,
                EveryMinuteProvider.Expression,
                next,
                createdAt,
                activationId,
                Slot);
            var store = Store();
            await store.PrepareActivationAsync(activationId, [schedule]);
            await store.ActivateAsync(activationId, replacedActivationId);
            return (await store.FindAsync(schedule.ScheduleId))!;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var scope in _scopes)
                await scope.DisposeAsync();
            await _stores.DisposeAsync();
            await Node.DisposeAsync();
        }
    }

    /// <summary>Stops the node before or after its route: the pump runs nothing after it, as if its process had died there.</summary>
    private sealed class DyingRouter(IStimulusRouter inner, bool afterRouting) : IStimulusRouter
    {
        public async ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
        {
            if (afterRouting)
                await inner.RouteAsync(request, cancellationToken);
            throw new OperationCanceledException("The node stopped.");
        }
    }

    private sealed class RecordingRouter(IStimulusRouter inner) : IStimulusRouter
    {
        private readonly ConcurrentQueue<(StimulusDispatchRequest Request, StimulusRoutingResult Result)> _routes = new();

        public IReadOnlyList<StimulusDispatchRequest> Requests => _routes.Select(route => route.Request).ToArray();
        public IReadOnlyList<StimulusRoutingResult> Results => _routes.Select(route => route.Result).ToArray();

        public async ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
        {
            var result = await inner.RouteAsync(request, cancellationToken);
            _routes.Enqueue((request, result));
            return result;
        }
    }

    private sealed class NoStartRouter : IStimulusRouter
    {
        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default) =>
            new(new StimulusRoutingResult([], []));
    }

    /// <summary>The index writer under the schedule indexer: it replaces the artifact's binding, as the trigger indexer does.</summary>
    private sealed class BindingWritingIndexer(IWorkflowTriggerBindingStore bindingStore) : IWorkflowTriggerIndexer
    {
        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
        {
            var binding = Binding(executable.Identity.ArtifactId);
            await bindingStore.DeleteByArtifactAsync(executable.Identity.ArtifactId, cancellationToken);
            await bindingStore.SaveAsync(binding, cancellationToken);
            return [binding];
        }
    }

    private sealed class EveryMinuteProvider : IRecurringTriggerScheduleProvider
    {
        public const string ActivityType = "Test.EveryMinute";
        public const string StimulusType = "Test.Recurring";
        public const string StimulusHash = "every-minute";
        public const string Expression = "PT1M";

        public string ProviderId => "test.every-minute";

        public IReadOnlyCollection<RecurringScheduleDescriptor> Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, ActivityType)
                ? [new RecurringScheduleDescriptor(StimulusType, StimulusHash, RecurringScheduleKind.Interval, Expression)]
                : [];
    }
}
