using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// Seeded invocation; actual native delivery/claim/callback/history. Not authored start/host/provider/restart proof.
public sealed partial class RuntimeHistoryNativeDeliveryTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1", "hash");
    private const string Run = "run", ActivityId = "activity", Wait = "wait", Node = "node", Resume = "resume";

    [Theory]
    [InlineData("complete")]
    [InlineData("returned-fault")]
    [InlineData("throw")]
    public async Task Receipt_native_duplicate_claim_callback_and_outcome_keep_their_distinct_authority(string outcome)
    {
        using var f = await Fixture.CreateAsync(outcome);
        var request = await f.RequestAsync();
        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, (await f.Dispatcher.DispatchAsync(request)).Status);
        Assert.Equal(0, f.Activator.Calls);
        var before = (await f.History.ReadPageAsync(new(Run))).Items;
        var received = Assert.Single(before, e => e.Name == "DeliveryReceived");
        Assert.Single(before, e => e.Name == "DeliveryDispatched");
        Assert.DoesNotContain(before, e => e.Name is "BookmarkConsumed" or "ActivityContinuationStarted");
        Assert.NotNull(await f.Bookmarks.FindAsync(Run, Wait));
        Assert.Equal(BookmarkResumeDispatchStatus.Duplicate, (await f.Dispatcher.DispatchAsync(request)).Status);
        Assert.Equal(0, f.Activator.Calls);
        var queued = Assert.Single(await f.Queue.ListAllAsync(new(Run)));
        Assert.Equal(received.HistoryEventId, queued.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!.HistoryCausation!.HistoryEventId);
        var drained = await f.DrainAsync();
        Assert.Equal(1, drained.DrainedCount);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(drained.Items).Status);
        Assert.Equal(1, f.Activator.Calls);
        Assert.Null(await f.Bookmarks.FindAsync(Run, Wait));
        var events = (await f.History.ReadPageAsync(new(Run, limit: 200))).Items;
        var consumed = Assert.Single(events, e => e.Name == "BookmarkConsumed");
        var entered = Assert.Single(events, e => e.Name == "ActivityContinuationStarted");
        Assert.Equal(received.HistoryEventId, consumed.Causation!.HistoryEventId);
        Assert.Equal(received.HistoryEventId, entered.Causation!.HistoryEventId);
        Assert.True(received.Sequence < consumed.Sequence);
        Assert.True(consumed.Sequence < entered.Sequence);
        Assert.Single(events, e => e.Name == "DeliveryDuplicate");
        Assert.DoesNotContain(events, e => e.Name == "DeliveryFaulted");
        Assert.Equal(RuntimeHistoryBookmarkStatus.Consumed, (await f.History.FindBookmarkAsync(Run, Wait))!.Status);
        if (outcome == "complete")
        {
            Assert.Single(events, e => e.Name == "ActivityContinuationCompleted");
            Assert.Single(events, e => e.Name == "ActivityContinuationReturned");
            Assert.DoesNotContain(events, e => e.Name is "ActivityContinuationFaulted" or "IncidentRecorded");
        }
        else
        {
            var faulted = Assert.Single(events, e => e.Name == "ActivityContinuationFaulted");
            Assert.Single(events, e => e.Name == "IncidentRecorded");
            Assert.True(consumed.Sequence < faulted.Sequence);
            Assert.Equal(received.HistoryEventId, faulted.Causation!.HistoryEventId);
            Assert.Equal(outcome == "returned-fault" ? 1 : 0, events.Count(e => e.Name == "ActivityContinuationReturned"));
        }
        Assert.DoesNotContain("private", JsonSerializer.Serialize(events));
        Assert.False((await f.History.FindHeadAsync(Run))!.CapturedFromStart);
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("replaced")]
    public async Task A_changed_wait_after_handoff_is_acknowledged_without_consuming_a_new_registration_or_invoking_a_callback(string scenario)
    {
        using var f = await Fixture.CreateAsync("complete");
        await f.Dispatcher.DispatchAsync(await f.RequestAsync());
        var original = (await f.Bookmarks.FindAsync(Run, Wait))!;
        await f.Bookmarks.DeleteAsync(Run, Wait);
        if (scenario == "replaced") await f.Bookmarks.SaveAsync(original);
        var drained = await f.DrainAsync();
        Assert.Equal(1, drained.DrainedCount);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(drained.Items).Status);
        Assert.Empty(await f.Queue.ListAllAsync(new(Run)));
        Assert.Empty(await f.Poison.ListAsync(Run));
        Assert.Equal(0, f.Activator.Calls);
        var events = (await f.History.ReadPageAsync(new(Run))).Items;
        Assert.Single(events, e => e.Name == "DeliveryReceived");
        Assert.Single(events, e => e.Name == "DeliveryDispatched");
        Assert.DoesNotContain(events, e => e.Name is "BookmarkConsumed" or "ActivityContinuationStarted" or "ActivityContinuationCompleted");
        if (scenario == "replaced") Assert.NotNull(await f.Bookmarks.FindAsync(Run, Wait));
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("native-history")); }
    private sealed class EnqueueOnly : IWorkflowSchedulerDrainPolicy
    { public RuntimeSchedulerDrainRequest? CreateDrainRequest(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerWorkItem workItem) => null; }
    private sealed record WaitState(string RequestId);
    private sealed record Approval(bool Approved);
    private sealed class NativeActivity(string outcome) : StatefulActivity<ActivityUnit, WaitState, Approval>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ExecuteAsync(ActivityExecutionContext context) => throw new NotSupportedException();
        protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, Approval> context) => outcome switch
        {
            "throw" => ValueTask.FromException<ActivityTransition<ActivityUnit, WaitState>>(new InvalidOperationException("private callback failure")),
            "returned-fault" => ValueTask.FromResult(Fault(new ActivityFault("approval.rejected", "private returned fault"))),
            _ => ValueTask.FromResult(Complete(ActivityUnit.Value))
        };
    }
    private sealed class Activator(string outcome) : IActivityActivator
    {
        internal int Calls;
        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult(new ActivityActivationLease(new NativeActivity(outcome))); }
    }
    private static ValueTypeDescriptor Descriptor<T>() => new(TypeAliasConvention.CanonicalAlias(typeof(T)), schemaVersion: 1);

    private sealed partial class Fixture : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly IServiceScope scope;
        private readonly FakeTimeProvider clock;
        private Fixture(ServiceProvider provider, IServiceScope scope, FakeTimeProvider clock, Activator activator)
        {
            this.provider = provider; this.scope = scope; this.clock = clock; Activator = activator;
            var sp = scope.ServiceProvider;
            Dispatcher = sp.GetRequiredService<IBookmarkResumeDispatcher>(); History = sp.GetRequiredService<IRuntimeHistoryStore>();
            Bookmarks = sp.GetRequiredService<IBookmarkStateStore>(); Queue = sp.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            Poison = new InMemoryWorkflowSchedulerPoisonStore();
        }
        internal Activator Activator { get; }
        internal IBookmarkResumeDispatcher Dispatcher { get; }
        internal IRuntimeHistoryStore History { get; }
        internal IBookmarkStateStore Bookmarks { get; }
        internal IWorkflowSchedulerWorkQueue Queue { get; }
        internal InMemoryWorkflowSchedulerPoisonStore Poison { get; }
        internal static async Task<Fixture> CreateAsync(string outcome, bool publication = false, bool captured = true)
        {
            var clock = new FakeTimeProvider(At); var activator = new Activator(outcome);
            var services = new ServiceCollection(); services.AddLogging();
            services.AddSingleton<TimeProvider>(clock); services.AddSingleton<IPersistenceAccessContextAccessor, Access>();
            services.AddSingleton<IActivityActivator>(activator); services.AddSingleton<IWorkflowSchedulerDrainPolicy, EnqueueOnly>();
            services.AddSingleton<ActivityCompletionProjector>(); services.AddSingleton<RuntimeOutputCaptureProjector>();
            services.AddScoped(sp => new ActivityFaultIncidentRecorder(sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<IRuntimeActivityExecutionInspectionAccumulator>(), DefaultRuntimeFaultCapturePolicy.CreateDefault()));
            if (publication) ConfigurePublication(services);
            services.AddWorkflowRuntime();
            if (publication) services.AddWorkflowStimulusRouter();
            RuntimeHistoryRegistration.AddServices(services);
            services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "native-history-delivery-tests-signing-key-at-least-32-bytes");
            services.AddSingleton(new RuntimeHistorySourceSupport([
                RuntimeHistorySourceFactKind.ActivityContinuationStarted, RuntimeHistorySourceFactKind.ActivityContinuationReturned,
                RuntimeHistorySourceFactKind.ActivityContinuationCompleted, RuntimeHistorySourceFactKind.ActivityContinuationFaulted]));
            var provider = services.BuildServiceProvider(validateScopes: true); var scope = provider.CreateScope();
            var f = new Fixture(provider, scope, clock, activator); var sp = scope.ServiceProvider;
            var executions = sp.GetRequiredService<IWorkflowExecutionStateStore>(); var activities = sp.GetRequiredService<IActivityExecutionStateStore>();
            var writer = sp.GetRequiredService<IRuntimeCheckpointCommitStore>();
            Assert.True(writer.CanCommitExactBookmarkResume(f.Bookmarks, executions, activities));
            var policy = RuntimeHistoryRegistration.ResolvePolicy(sp, Run)!;
            var stimulusType = publication ? PublicationStimulusType : "approval";
            var stimulusHash = publication ? PublicationStimulusHash : "hash";
            await executions.SaveAsync(new(Run, Pin, WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, "native-history", new Dictionary<string, string>())
                { HistoryCapturePolicy = captured ? policy : null, HistoryCaptureEnabledAtAdmission = captured, Partition = new("native-history"), RunKind = WorkflowRunKind.PublishedRun });
            var descriptor = JsonSerializer.SerializeToElement(new { type = "native-history" });
            var contract = new ActivityContract("test/native-history", "1", "native-history", descriptor, [],
                new ActivityResultContract(new("Elsa.Unit"), true, ActivityValuePolicy.Default, []), ["Done"],
                new ActivityActivationRequirement("native-history", "test/native-history"));
            var executable = new WorkflowExecutable(Pin,
                new ExecutableNode(Node, "authored", "test/native-history", "1", "native-history", descriptor,
                    new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: contract),
                new Dictionary<string, WorkflowExecutableResumeTarget> { [Resume] = new(Resume, Node, "handler", new Dictionary<string, string>()) },
                At, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
            await sp.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
            var stateType = Descriptor<WaitState>(); var triggerType = Descriptor<Approval>();
            var state = new ActivityExecutionState(new(ActivityId, Run, Node, "authored", "test/native-history", "1"),
                ActivityExecutionStatus.Suspended, "BookmarkWaiting", At, At, null, null, null, null, null, null, [Wait], [], 0, 0, new Dictionary<string, string>())
            {
                ContractIdentity = new(contract.ActivityTypeKey, contract.ContractVersion, contract.SchemaFingerprint),
                InputSnapshot = new(ActivityId, contract.SchemaFingerprint, "seeded-no-input", new Dictionary<string, ValueEnvelope>(), At),
                Attempts = [new("initial-attempt", ActivityId, 1, ActivityAttemptReason.Initial, At, At,
                    transitionKind: Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend)],
                PrivateState = new(ActivityId, 1, ValueEnvelope.Inline(stateType, JsonSerializer.SerializeToElement(new WaitState("request")),
                    ValueProtectionPolicy.InstanceInline), "initial-attempt", At),
                TriggerRegistrations = [new Elsa.Workflows.Runtime.Core.Models.ActivityTriggerRegistration(Wait, ActivityId, Resume, triggerType,
                    stimulusType, stimulusHash, ActivityTriggerDeduplicationPolicy.IdempotencyKey)], TriggerDeliveries = []
            };
            await activities.SaveAsync(state);
            var payload = new RuntimeCreateBookmarkCommandPayload(Pin, Wait, ActivityId, Node, Resume, stimulusType, stimulusHash, null, null,
                RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason);
            var work = new RuntimeSchedulerWorkItem("create", Run, "create-command", WorkflowExecutionCommandKind.CreateBookmark,
                "create-envelope", "create-key", At, At, payload: JsonSerializer.SerializeToElement(payload));
            await new WorkflowCreateBookmarkSchedulerWorkHandler(sp.GetRequiredService<IWorkflowExecutableStore>(), activities,
                sp.GetRequiredService<RuntimeCheckpointCommitter>(), sp.GetRequiredService<IRuntimeActivityExecutionInspectionAccumulator>(), clock).HandleAsync(work);
            if (captured) Assert.NotNull(await f.History.FindHeadAsync(Run)); else Assert.Null(await f.History.FindHeadAsync(Run));
            return f;
        }
        internal async Task<BookmarkResumeDispatchRequest> RequestAsync()
        {
            var expected = StimulusResumeRecipient.From((await Bookmarks.ObserveAsync(Run, Wait))!,
                (await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(Run))!, new("native-history"));
            return new(Run, "approval", "hash", JsonSerializer.SerializeToElement(new Approval(true)), "native-history-delivery",
                payloadType: Descriptor<Approval>(), providerId: "approval-provider") { ExpectedRecipient = expected };
        }
        internal ValueTask<RuntimeSchedulerDrainResult> DrainAsync() => new WorkflowSchedulerDrainer(Queue,
            [new WorkflowResumeBookmarkSchedulerWorkHandler(provider.GetRequiredService<IServiceScopeFactory>(), clock)],
            scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>(), Poison,
            new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(new InMemoryWorkflowHoldStateStore()), clock), clock,
            claimOptions: new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = TimeSpan.FromSeconds(5) }).DrainAsync(new(Run, maxWorkItems: 1));
        public void Dispose() { scope.Dispose(); provider.Dispose(); }
    }
}
