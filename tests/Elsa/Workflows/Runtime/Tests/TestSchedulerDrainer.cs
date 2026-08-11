using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Test-only factory that keeps the scheduler-drain tests terse after RT-8 collapsed the drainer's telescoping
/// constructors into a single primary constructor with a <b>required</b> workflow execution state store. It preserves
/// the historic positional shape <c>(queue, handlers, timeProvider, pauseGate, stateStore, ...)</c> and defaults the
/// state store to a fresh empty <see cref="InMemoryWorkflowExecutionStateStore"/> when a test does not supply one —
/// behaviour-identical to the pre-RT-8 no-store path (an empty store reports no terminal execution, so the
/// terminal-status guard stays inert). Tests that exercise the required-store contract construct the drainer directly.
///
/// <para>The poison store is defaulted the same way (#1271), to a fresh <see cref="InMemoryWorkflowSchedulerPoisonStore"/>
/// that mirrors what the core composition root registers. This default is deliberately <em>not</em> equivalent to the
/// old null: a handler fault now always leaves a record, and a test that does not care about poison simply drops the
/// throwaway store.</para>
///
/// <para>The pause gate is defaulted to a real <see cref="WorkflowSchedulerPauseGate"/> over an empty
/// <see cref="InMemoryWorkflowHoldStateStore"/> (#1277 R1). Unlike the poison store, this default <em>is</em>
/// behaviour-identical to the old null for every test that holds nothing: with no effective hold the gate answers
/// <c>CanAdvance: true</c>, so work advances exactly as it did when the gate was absent. The difference is that the
/// pause contract is now expressed by the hold store's contents rather than by whether a collaborator was passed.</para>
/// </summary>
internal static class TestSchedulerDrainer
{
    public static WorkflowSchedulerDrainer Create(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers,
        TimeProvider? timeProvider = null,
        IWorkflowSchedulerPauseGate? pauseGate = null,
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null,
        IRuntimeExecutionPipelineDispatcher? pipelineDispatcher = null,
        IRuntimeFaultCapturePolicy? faultCapturePolicy = null,
        IWorkflowSchedulerPoisonStore? poisonStore = null,
        IRuntimeDomainRetryPolicy? retryPolicy = null,
        RuntimeSchedulerWorkClaimOptions? claimOptions = null,
        IRuntimeConsumedSchedulerWorkClaimAccessor? consumedWorkClaimAccessor = null) =>
        new(
            schedulerWorkQueue,
            handlers,
            workflowExecutionStateStore ?? new InMemoryWorkflowExecutionStateStore(),
            poisonStore ?? new InMemoryWorkflowSchedulerPoisonStore(),
            pauseGate ?? NewInertPauseGate(timeProvider),
            timeProvider,
            pipelineDispatcher,
            faultCapturePolicy,
            retryPolicy,
            claimOptions: claimOptions,
            consumedWorkClaimAccessor: consumedWorkClaimAccessor);

    // A real gate over an empty hold store: holds nothing, so it answers CanAdvance: true for every boundary.
    public static IWorkflowSchedulerPauseGate NewInertPauseGate(TimeProvider? timeProvider = null) =>
        new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(new InMemoryWorkflowHoldStateStore()), timeProvider);
}
