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
            timeProvider,
            pauseGate,
            pipelineDispatcher,
            faultCapturePolicy,
            retryPolicy,
            claimOptions: claimOptions,
            consumedWorkClaimAccessor: consumedWorkClaimAccessor);
}
