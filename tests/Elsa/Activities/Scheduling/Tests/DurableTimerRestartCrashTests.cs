using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Scheduling.Tests;

// End-to-end restart/crash coverage for the durable timer chain (W8 / finding E3-2).
//
// A single shared IDocumentStore stands in for the durable substrate that survives a process crash.
// Each scenario runs multiple provider generations over the SAME store: the first generation runs a
// Delay activity to durable suspension (a durable timer + a matching bookmark are persisted), its
// ServiceProvider is disposed (the "crash"), and a later generation rebuilds honest services over the
// surviving documents and drives the workflow to the same terminal state a crash-free run reaches.
//
// The pump is driven manually against a controllable clock (MutableTimeProvider) so the due-time race
// and grace windows are deterministic rather than wall-clock dependent.
public sealed class DurableTimerRestartCrashTests
{
    private const string TimerId = DelayTestExecutable.TimerId;
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Delay5s = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Delay_SuspendsDurably_SurvivesRestart_AndResumesWhenTimerFires()
    {
        var manifest = ElsaRuntimeStorageManifest.CreatePhysicalized();
        var store = new InMemoryDocumentStore(manifest);
        var clock = new MutableTimeProvider(T0);

        // Generation 1: run the Delay activity to durable suspension. A durable timer (due at T0 + 5s)
        // and a matching bookmark are persisted to the shared store.
        await using (var gen1 = BuildHarness(store, clock))
        {
            var run = await gen1.RunAsync(DelayTestExecutable.Create(Delay5s, T0));

            Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
            var timer = await TimerStore(gen1).FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, TimerId);
            Assert.NotNull(timer);
            Assert.Equal(T0 + Delay5s, timer!.DueTime);
        }
        // gen1 disposed == process crash/restart. The in-memory runtime state is gone; only the durable
        // documents survive in `store`.

        // Generation 2: honest services over the surviving store. The timer survived the restart, and a
        // single pump tick past its due time fires it through the real dispatcher, resuming the workflow.
        await using (var gen2 = BuildHarness(store, clock))
        {
            var survived = await TimerStore(gen2).FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, TimerId);
            Assert.NotNull(survived); // durable across the restart

            clock.Advance(Delay5s + TimeSpan.FromSeconds(1)); // now past the due time
            await NewPump(gen2, clock).ExecuteAsync(CancellationToken.None);

            var workflow = await gen2.Services.GetRequiredService<IWorkflowExecutionStateStore>()
                .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId);
            Assert.True(workflow?.Status == WorkflowExecutionStatus.Completed, await DescribeActivityAsync(gen2));
            Assert.Equal(1, await CompletedDelayCountAsync(gen2));

            var deleted = await TimerStore(gen2).FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, TimerId);
            Assert.Null(deleted); // deleted on Dispatched (resume durably enqueued before the dispatcher returned)
        }
    }

    [Fact]
    public async Task TimerFire_IsIdempotent_UnderAtLeastOnceDuplicateDelivery()
    {
        var manifest = ElsaRuntimeStorageManifest.CreatePhysicalized();
        var store = new InMemoryDocumentStore(manifest);
        var clock = new MutableTimeProvider(T0);

        await using (var gen1 = BuildHarness(store, clock))
            await gen1.RunAsync(DelayTestExecutable.Create(Delay5s, T0));

        await using (var gen2 = BuildHarness(store, clock))
        {
            clock.Advance(Delay5s + TimeSpan.FromSeconds(1));
            var dispatcher = gen2.Services.GetRequiredService<IBookmarkResumeDispatcher>();
            var request = ResumeRequestFor(TimerId);

            // First delivery resumes the workflow and consumes the (single-use) bookmark.
            var first = await dispatcher.DispatchAsync(request);
            Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, first.Status);
            Assert.True(await StatusAsync(gen2) == WorkflowExecutionStatus.Completed, await DescribeActivityAsync(gen2));
            Assert.Equal(1, await CompletedDelayCountAsync(gen2));

            // A duplicate at-least-once delivery of the same fire finds no bookmark and cannot resume again.
            var second = await dispatcher.DispatchAsync(request);
            Assert.Equal(BookmarkResumeDispatchStatus.NotFound, second.Status);
            Assert.Equal(1, await CompletedDelayCountAsync(gen2)); // still exactly one resume
        }
    }

    // Condition 1 (the correctness crux): the pump deletes a timer on Dispatched. That is only safe if the
    // resume is DURABLY enqueued before the dispatcher returns. This proves it: with the drain suppressed,
    // the pump still deletes the timer on Dispatched; a crash before the resume commits leaves the resume
    // durably queued; W2's resumption sweep re-drives it to the terminal state. Delete-on-Dispatched never
    // loses a resume.
    [Fact]
    public async Task DeleteOnDispatched_IsSafe_ResumeSurvivesCrashBeforeDrain_AndConverges()
    {
        var manifest = ElsaRuntimeStorageManifest.CreatePhysicalized();
        var store = new InMemoryDocumentStore(manifest);
        var clock = new MutableTimeProvider(T0);

        await using (var gen1 = BuildHarness(store, clock))
            await gen1.RunAsync(DelayTestExecutable.Create(Delay5s, T0));

        // Generation 2: the drain policy refuses to drain, so ProcessAsync durably enqueues the resume work
        // item but never drives it. The pump still observes Dispatched and deletes the timer.
        await using (var gen2 = BuildHarness(store, clock, services =>
        {
            services.RemoveAll<IWorkflowSchedulerDrainPolicy>();
            services.AddSingleton<IWorkflowSchedulerDrainPolicy>(new NoDrainPolicy());
        }))
        {
            clock.Advance(Delay5s + TimeSpan.FromSeconds(1));
            await NewPump(gen2, clock).ExecuteAsync(CancellationToken.None);

            Assert.Null(await TimerStore(gen2).FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, TimerId)); // deleted on Dispatched
            Assert.NotEqual(WorkflowExecutionStatus.Completed, await StatusAsync(gen2)); // resume not yet driven
            var queued = await gen2.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                .ListPendingWorkflowExecutionIdsAsync(10);
            Assert.Contains(WorkflowExecutionHarness.WorkflowExecutionId, queued); // resume durably enqueued
        }
        // gen2 disposed == crash after the timer delete but before the resume committed.

        // Generation 3: honest services. The resumption sweep discovers the durable backlog and re-drives
        // the workflow to completion — even though the timer is already gone.
        await using (var gen3 = BuildHarness(store, clock))
        {
            await ResolveResumptionService(gen3).SweepAsync(new RuntimeResumptionSweepRequest());

            Assert.True(await StatusAsync(gen3) == WorkflowExecutionStatus.Completed, await DescribeActivityAsync(gen3));
            Assert.Equal(1, await CompletedDelayCountAsync(gen3));
        }
    }

    private static WorkflowExecutionHarness BuildHarness(
        IDocumentStore store,
        TimeProvider clock,
        Action<IServiceCollection>? customize = null)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                // Swap the in-memory runtime stores for the Groundwork-backed bridges over the shared
                // document store so state survives across provider generations (the "restart").
                services.AddSingleton(store);
                services.AddGroundworkRuntimeStores();
                // Override the runtime clock so due-time computation and the pump sweep share one
                // controllable timeline.
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
                customize?.Invoke(services);
            })
            .Build(DelayTestExecutable.ActivityExecutionId);
        harness.InitializeActivityTypes();
        return harness;
    }

    private static DurableTimerPumpTask NewPump(WorkflowExecutionHarness harness, TimeProvider clock) =>
        new(
            harness.Services.GetRequiredService<IDurableTimerStore>(),
            harness.Services.GetRequiredService<IBookmarkResumeDispatcher>(),
            Microsoft.Extensions.Options.Options.Create(new DurableTimerPumpOptions()),
            clock,
            NullLogger<DurableTimerPumpTask>.Instance);

    private static RuntimeResumptionService ResolveResumptionService(WorkflowExecutionHarness harness) =>
        new(
            harness.Services.GetRequiredService<IRuntimePostCommitOutboxProcessor>(),
            harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>(),
            harness.Services.GetRequiredService<IRuntimeRecoveryScanner>(),
            harness.Services.GetRequiredService<IWorkflowExecutionActorProvider>(),
            harness.Services.GetRequiredService<IRuntimeExecutionIdGenerator>(),
            harness.Services.GetRequiredService<TimeProvider>());

    private static IDurableTimerStore TimerStore(WorkflowExecutionHarness harness) =>
        harness.Services.GetRequiredService<IDurableTimerStore>();

    private static BookmarkResumeDispatchRequest ResumeRequestFor(string timerId) =>
        new(
            WorkflowExecutionHarness.WorkflowExecutionId,
            DurableTimerConstants.TimerStimulusType,
            timerId,
            input: JsonSerializer.SerializeToElement(new DurableTimerElapsed(timerId)),
            idempotencyKey: $"timer:{timerId}",
            requestedBy: DurableTimerConstants.PumpRequestedBy,
            payloadType: new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(DurableTimerElapsed)), schemaVersion: 1),
            providerId: Delay.TimerTriggerProviderId);

    private static async Task<WorkflowExecutionStatus?> StatusAsync(WorkflowExecutionHarness harness) =>
        (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId))?.Status;

    private static async Task<int> CompletedDelayCountAsync(WorkflowExecutionHarness harness)
    {
        var states = await harness.Services.GetRequiredService<IActivityExecutionStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        return states.Count(s =>
            s.Execution.ExecutableNodeId == "node-delay" && s.Status == ActivityExecutionStatus.Completed);
    }

    private static async Task<string> DescribeActivityAsync(WorkflowExecutionHarness harness)
    {
        var states = await harness.Services.GetRequiredService<IActivityExecutionStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        return string.Join(Environment.NewLine, states.Select(state =>
            $"{state.Execution.ExecutableNodeId}: {state.Status}/{state.SubStatus}; fault={state.Metadata.GetValueOrDefault("runtime.faultMessage")}"));
    }

    // Suppresses the scheduler drain so recorded work stays durably queued without being driven.
    private sealed class NoDrainPolicy : IWorkflowSchedulerDrainPolicy
    {
        public RuntimeSchedulerDrainRequest? CreateDrainRequest(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerWorkItem workItem) => null;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
