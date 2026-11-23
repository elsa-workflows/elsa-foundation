using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_native_worker_first_checkpoint_failure_keeps_its_model_origin_and_delivers_the_original_typed_fault(bool recoverFromQueuedStart, bool legacyCheckpoint)
    {
        var failure = new FailFirstModelWorkerCheckpoint();
        var clock = new DurationClock(DateTimeOffset.UtcNow);
        var pause = new PauseInitialWorker();
        await using var host = await OpenAsync(configure: services =>
        {
            services.AddSingleton<IRuntimeCheckpointCommitEnricher>(failure);
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            if (recoverFromQueuedStart) services.Replace(ServiceDescriptor.Singleton<IWorkflowSchedulerPauseGate>(pause));
        },
            activityIds: ["never-runs"]);
        var worker = new WorkflowExecutable(WorkflowExecutionHarness.Identity,
            WorkflowExecutionHarness.NewFaultingNode("never-runs", "The root must never execute"),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(),
            inputContract: new(1, [new("message", new("String"), true)]), dependencies: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        await host.PublishAsync(worker, "first-fault-worker-source");
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("bpmn-test", null);
        var process = new BpmnProcessDefinition("first-fault-service", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "worker"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-service", "start", "service"), new("to-end", "service", "end")]);
        var material = await runtime.PreparePublicationAsync(new(3, JsonSerializer.SerializeToElement(process, Web), [],
                [new("worker", worker.Identity, "business-error", new(1, BpmnServiceFaultDisposition.FinalFault, 1))]),
            new(1, [new("text", new("String"), true)]),
            [new("worker", new Dictionary<string, BpmnModelInputSource> { ["message"] = new("Input", "text") })], caller);
        await SendAsync(host, material, "first-fault-start", "start", new { inputs = new { text = "original-value" } });
        var parent = (await runtime.FindExecutionAsync("direct-execution", caller))!;
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(parent).Work).NativeWork!;
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        var completionFailure = await Record.ExceptionAsync(() => processor.ProcessAsync(new(100,
            workflowExecutionId: parent.ExecutionId, intentKind: BpmnNativeWorkBoundary.InputStartKind)).AsTask());
        if (recoverFromQueuedStart)
        {
            Assert.Null(completionFailure); Assert.False(failure.Fired);
            Assert.Null(await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId));
            var retainedQueue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            var retainedStart = Assert.Single((await retainedQueue.ListAsync(new(origin.WorkerExecutionId))).Items,
                item => item.CommandKind == WorkflowExecutionCommandKind.Start);
            if (legacyCheckpoint)
            {
                // Prepare the same durable successor produced by the predecessor's successful Start handler.
                // No new metadata/property is added to that queued checkpoint by this repair.
                await services.GetServices<IWorkflowSchedulerWorkHandler>().Single(handler => handler.Name ==
                    Elsa.Workflows.Runtime.Services.WorkHandlers.WorkflowStartSchedulerWorkHandler.HandlerName).HandleAsync(retainedStart);
                await retainedQueue.DeleteAsync(retainedStart.WorkflowExecutionId, retainedStart.WorkItemId);
                var queuedCheckpoint = Assert.Single((await retainedQueue.ListAsync(new(origin.WorkerExecutionId))).Items);
                Assert.Equal(WorkflowExecutionCommandKind.Checkpoint, queuedCheckpoint.CommandKind);
                Assert.DoesNotContain("runtime.initialStartFault", string.Join(" ", queuedCheckpoint.CommandMetadata.Keys));
            }
            await scope.DisposeAsync(); await host.DisposeAsync();
        }
        await using var restartedHost = recoverFromQueuedStart ? await OpenAsync(configure: selected =>
        {
            selected.AddSingleton<IRuntimeCheckpointCommitEnricher>(failure);
            selected.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            new WorkflowsRuntimeResumptionFeature().ConfigureServices(selected);
        }, activityIds: ["never-runs"]) : null;
        await using var restartedScope = restartedHost is null ? null : await ScopeAsync(restartedHost);
        if (restartedScope is not null)
        {
            services = restartedScope.ServiceProvider;
            // The first source's normal retry is not due on the unchanged clock. Actual resumption therefore
            // discovers the retained native Start and dispatches RunSchedulerWork through the new provider.
            await services.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new());
            runtime = services.GetRequiredService<IBpmnModelRuntime>();
        }
        if (completionFailure is not null)
        {
            var changedClaim = Assert.IsType<InvalidOperationException>(completionFailure);
            Assert.IsType<DbUpdateConcurrencyException>(changedClaim.InnerException);
        }
        // Initial fault admission CASes the source row in the worker's own transaction. If its caller held an older
        // tracked revision, the original claim remains owed; its normal expiry/replay must settle that same source.
        clock.Advance(TimeSpan.FromMinutes(2));
        await using var recoveryScope = await ScopeAsync(restartedHost ?? host);
        services = recoveryScope.ServiceProvider;
        processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        await processor.ProcessAsync(new(100, workflowExecutionId: parent.ExecutionId, intentKind: BpmnNativeWorkBoundary.InputStartKind));
        Assert.True(failure.Fired);
        var actual = await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId);
        Assert.NotNull(actual); Assert.Equal(WorkflowExecutionStatus.Faulted, actual.Status);
        Assert.True(origin.IsEquivalentTo(actual.ModelWorkOrigin));
        Assert.Null(actual.IngressOrigin); Assert.Null(actual.StimulusDeliveryOrigin);
        Assert.Empty((await services.GetRequiredService<IActivityExecutionStateStore>().ListPageAsync(new(origin.WorkerExecutionId))).Items);
        Assert.Empty((await services.GetRequiredService<IDurableValueStateStore>().ListPageAsync(new(origin.WorkerExecutionId))).Items);
        var parked = Assert.Single(await services.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(origin.WorkerExecutionId));
        var source = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(origin.SourceOutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, source.Status);
        var faultCommitId = $"commit:{origin.WorkerExecutionId}:scheduler-poison:{RuntimeChainId.Fingerprint(parked.WorkItemId)}";
        var resultId = "bpmn-native-result:" + BpmnModelIdentity.Create(origin.WorkerExecutionId, origin.InvocationId, origin.AttemptId);
        var resultSource = (await services.GetRequiredService<IPostCommitOutboxLookupStore>()
            .FindDurableAsync(RuntimePostCommitOutboxItems.OutboxItemId(faultCommitId, resultId)))!;
        Assert.NotNull(resultSource);
        var result = resultSource.Intent.Payload!.Value.Deserialize<BpmnNativeWorkResult>(Web)!;
        Assert.True(origin.IsEquivalentTo(result.Origin)); Assert.Equal(WorkflowExecutionStatus.Faulted, result.Status);
        Assert.Null(result.Completion);
        await processor.ProcessAsync(new(100, workflowExecutionId: origin.WorkerExecutionId, intentKind: BpmnNativeWorkBoundary.ResultKind));
        await DrainModelAsync(restartedHost ?? host, parent.ExecutionId);
        clock.Advance(TimeSpan.FromSeconds(2));
        await processor.ProcessAsync(new(100, workflowExecutionId: origin.WorkerExecutionId, intentKind: BpmnNativeWorkBoundary.ResultKind));
        var settledResult = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(resultSource.OutboxItemId))!;
        Assert.True(settledResult.Status == RuntimePostCommitOutboxStatus.Delivered, settledResult.LastFailureMessage ?? settledResult.Status.ToString());
        var completed = (await runtime.FindExecutionAsync(parent.ExecutionId, caller))!;
        Assert.True(completed.TerminalResult is not null, $"Model lifecycle {completed.Lifecycle}; retained work {BpmnDirectExecutionDriver.Read(completed).Work.Count}; result delivery {settledResult.Status}");
        Assert.Equal("Faulted", completed.TerminalResult!.Value.GetProperty("status").GetString());
        Assert.Empty(BpmnDirectExecutionDriver.Read(completed).Work);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered,
            (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(resultSource.OutboxItemId))!.Status);
    }

    private sealed class PauseInitialWorker : IWorkflowSchedulerPauseGate
    {
        public ValueTask<SchedulerPauseDecision?> EvaluateAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SchedulerPauseDecision?>(workItem.CommandKind == WorkflowExecutionCommandKind.Start
                ? new(false, RuntimePauseBoundary.BeforeActivityExecutionStart, RuntimePauseContinuationPolicy.StrictPause,
                    "owned-first-start", "Retain the original native Start for provider recovery") : null);
    }

    private sealed class FailFirstModelWorkerCheckpoint : IRuntimeCheckpointCommitEnricher
    {
        internal bool Fired;
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            if (!Fired && commit.Checkpoint.Name == RuntimeCheckpointNames.WorkflowStarted && commit.StateChanges.WorkflowExecution?.State.ModelWorkOrigin is not null)
            { Fired = true; throw new InvalidOperationException("Owned first-checkpoint failure before any native activity."); }
            return ValueTask.FromResult(commit);
        }
    }
}
