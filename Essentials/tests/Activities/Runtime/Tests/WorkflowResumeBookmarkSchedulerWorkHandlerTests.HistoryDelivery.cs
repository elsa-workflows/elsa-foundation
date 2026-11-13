using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

// Seeded native invocation; real dispatcher/mailbox/queue/claim/callback/checkpoint/history, not start/HTTP/provider/restart proof.
public sealed partial class WorkflowResumeBookmarkSchedulerWorkHandlerTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("returned-fault")]
    [InlineData("throw")]
    public async Task Native_delivery_receipts_claim_callback_and_fault_are_distinct_in_the_same_recorded_history(string outcome)
    {
        var (template, clock) = await PrepareExactResume();
        IActivity activity = outcome switch
        {
            "returned-fault" => new FaultingApprovalActivity(),
            "throw" => new ThrowingApprovalActivity(),
            _ => new ApprovalActivity()
        };
        var activator = new NativeHistoryActivator(activity);
        await using var provider = NewNativeHistoryProvider(activator, clock);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await CreateRecordedNativeWaitAsync(services, clock);
        var history = services.GetRequiredService<IRuntimeHistoryStore>();
        var dispatcher = services.GetRequiredService<IBookmarkResumeDispatcher>();
        var request = await NativeHistoryRequestAsync(template);
        var first = await dispatcher.DispatchAsync(request);
        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, first.Status);
        Assert.Equal(0, activator.Calls);
        var beforeDrain = (await history.ReadPageAsync(new("wfexec-1"))).Items;
        var received = Assert.Single(beforeDrain, e => e.Name == "DeliveryReceived");
        Assert.Single(beforeDrain, e => e.Name == "DeliveryDispatched");
        Assert.DoesNotContain(beforeDrain, e => e.Name is "BookmarkConsumed" or "ActivityContinuationStarted");
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));

        var second = await dispatcher.DispatchAsync(request);
        Assert.Equal(BookmarkResumeDispatchStatus.Duplicate, second.Status); // Actual native mailbox deduplication.
        Assert.Equal(0, activator.Calls);
        var queued = Assert.Single(await _schedulerWorkQueue.ListAllAsync(new("wfexec-1")));
        Assert.Equal(received.HistoryEventId, queued.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!.HistoryCausation!.HistoryEventId);
        var poison = new InMemoryWorkflowSchedulerPoisonStore();
        var drained = await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1));
        Assert.Equal(1, drained.DrainedCount);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(drained.Items).Status);
        Assert.Equal(1, activator.Calls);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        var events = (await history.ReadPageAsync(new("wfexec-1", limit: 200))).Items;
        var consumed = Assert.Single(events, e => e.Name == "BookmarkConsumed");
        var entered = Assert.Single(events, e => e.Name == "ActivityContinuationStarted");
        Assert.Equal(received.HistoryEventId, consumed.Causation!.HistoryEventId);
        Assert.Equal(received.HistoryEventId, entered.Causation!.HistoryEventId);
        Assert.True(received.Sequence < consumed.Sequence);
        Assert.True(consumed.Sequence < entered.Sequence);
        Assert.Single(events, e => e.Name == "DeliveryDuplicate");
        Assert.DoesNotContain(events, e => e.Name == "DeliveryFaulted");
        Assert.Equal(RuntimeHistoryBookmarkStatus.Consumed, (await history.FindBookmarkAsync("wfexec-1", "bookmark-1"))!.Status);
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
        // Fixture admission happened after seeded execution: no fabricated complete-from-start claim.
        Assert.False((await history.FindHeadAsync("wfexec-1"))!.CapturedFromStart);
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("replaced")]
    public async Task A_changed_native_wait_after_handoff_cannot_turn_the_receipt_into_a_callback_or_consumption(string scenario)
    {
        var (template, clock) = await PrepareExactResume();
        var activator = new NativeHistoryActivator(new ApprovalActivity());
        await using var provider = NewNativeHistoryProvider(activator, clock);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await CreateRecordedNativeWaitAsync(services, clock);
        var request = await NativeHistoryRequestAsync(template);
        await services.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(request);
        var original = (await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"))!;
        await _bookmarkStateStore.DeleteAsync("wfexec-1", "bookmark-1");
        if (scenario == "replaced") await _bookmarkStateStore.SaveAsync(original);
        await ExactDrainer(provider, clock, new InMemoryWorkflowSchedulerPoisonStore()).DrainAsync(new("wfexec-1", maxWorkItems: 1));
        Assert.Equal(0, activator.Calls);
        var events = (await services.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new("wfexec-1"))).Items;
        Assert.Single(events, e => e.Name == "DeliveryReceived");
        Assert.Single(events, e => e.Name == "DeliveryDispatched");
        Assert.DoesNotContain(events, e => e.Name is "BookmarkConsumed" or "ActivityContinuationStarted" or "ActivityContinuationCompleted");
        if (scenario == "replaced") Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    private ServiceProvider NewNativeHistoryProvider(IActivityActivator activator, FakeTimeProvider clock)
    {
        var access = new ExactAccess();
        _checkpointWriter = new(_executableStore, new InMemoryWorkflowExecutableSourceReferenceStore(_executableStore, clock),
            _exactWorkflowStates!, _activityStateStore, _bookmarkStateStore, incidentStateStore: _incidentStateStore,
            durableValueStateStore: _durableValueStateStore, activityExecutionInspectionWriter: _inspectionStore, state: _checkpointState,
            timeProvider: clock, persistenceAccessContextAccessor: access,
            rootWriteLeaseManager: new WorkflowExecutableRootWriteLeaseManager(_executableStore,
                Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock));
        return NewProvider(activator, configure: services =>
        {
            services.AddLogging();
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<IPersistenceAccessContextAccessor>(); services.AddSingleton<IPersistenceAccessContextAccessor>(access);
            services.AddSingleton(_checkpointState);
            services.RemoveAll<RuntimeCheckpointCommitter>();
            services.RemoveAll<IBookmarkConsumptionCheckpointService>();
            services.AddWorkflowRuntime();
            services.Configure<RuntimeRecoveryContinuationOptions>(options =>
                options.SigningKey = "native-history-delivery-tests-signing-key-at-least-32-bytes");
            RuntimeHistoryRegistration.AddServices(services);
            services.AddSingleton(new RuntimeHistorySourceSupport([
                RuntimeHistorySourceFactKind.ActivityContinuationStarted, RuntimeHistorySourceFactKind.ActivityContinuationReturned,
                RuntimeHistorySourceFactKind.ActivityContinuationCompleted, RuntimeHistorySourceFactKind.ActivityContinuationFaulted]));
            services.AddSingleton<IWorkflowSchedulerDrainPolicy, NativeHistoryEnqueueOnly>();
        });
    }

    private async Task CreateRecordedNativeWaitAsync(IServiceProvider services, FakeTimeProvider clock)
    {
        var policy = RuntimeHistoryRegistration.ResolvePolicy(services, "wfexec-1")!;
        await _exactWorkflowStates!.SaveAsync((await _exactWorkflowStates.FindAsync("wfexec-1"))! with
            { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = true });
        await _bookmarkStateStore.DeleteAsync("wfexec-1", "bookmark-1");
        var payload = new RuntimeCreateBookmarkCommandPayload(NewIdentity(), "bookmark-1", "actexec-1", "node-wait",
            "resume-target:delivery", "delivery-status", "sha256:delivery-status:order-123", null, null,
            RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason);
        var work = new RuntimeSchedulerWorkItem("native-history-create", "wfexec-1", "create-command", WorkflowExecutionCommandKind.CreateBookmark,
            "create-envelope", "create-key", _now, _now, payload: JsonSerializer.SerializeToElement(payload));
        await new WorkflowCreateBookmarkSchedulerWorkHandler(_executableStore, _activityStateStore,
            services.GetRequiredService<RuntimeCheckpointCommitter>(), services.GetRequiredService<IRuntimeActivityExecutionInspectionAccumulator>(), clock).HandleAsync(work);
        Assert.NotNull(await services.GetRequiredService<IRuntimeHistoryStore>().FindHeadAsync("wfexec-1"));
    }

    private async Task<BookmarkResumeDispatchRequest> NativeHistoryRequestAsync(RuntimeSchedulerWorkItem template)
    {
        var payload = template.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
        var expected = StimulusResumeRecipient.From((await _bookmarkStateStore.ObserveAsync("wfexec-1", "bookmark-1"))!,
            (await _exactWorkflowStates!.FindAsync("wfexec-1"))!, ExactScope);
        return new("wfexec-1", payload.StimulusType, payload.StimulusHash, payload.Input, "native-history-delivery",
            payloadType: payload.TriggerDelivery!.PayloadType, providerId: payload.TriggerDelivery.ProviderId) { ExpectedRecipient = expected };
    }

    private sealed class NativeHistoryActivator(IActivity activity) : IActivityActivator
    {
        internal int Calls;
        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult(new ActivityActivationLease(activity)); }
    }

    // The actual native router records work before returning; this controlled policy leaves draining explicit.
    private sealed class NativeHistoryEnqueueOnly : IWorkflowSchedulerDrainPolicy
    {
        public RuntimeSchedulerDrainRequest? CreateDrainRequest(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerWorkItem workItem) => null;
    }
}
