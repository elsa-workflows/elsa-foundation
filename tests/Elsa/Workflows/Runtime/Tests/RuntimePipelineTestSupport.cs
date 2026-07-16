using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Middleware;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Shared fixtures for the runtime execution-pipeline tests: a fixed clock, a scheduler work-item factory, a recording
/// activity middleware, and a recording work handler. Kept in one place so the two pipeline test classes don't drift.
/// </summary>
public abstract class RuntimePipelineTestSupport
{
    protected readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    protected RuntimeSchedulerWorkItem NewWorkItem(
        WorkflowExecutionCommandKind commandKind,
        int index = 1,
        JsonElement? payload = null)
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new(
            workItemId: $"work-{index}",
            workflowExecutionId: "wfexec-1",
            commandId: $"command-{index}",
            commandKind: commandKind,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"wfexec-1:command-{index}",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: index,
            payload: payload ?? document.RootElement.Clone());
    }

    protected static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    protected static WorkflowExecutable NewExecutable() =>
        new(
            NewIdentity(),
            new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "activity-root",
                activityType: "Elsa.Test",
                activityTypeVersion: "1.0.0",
                descriptorType: "Test",
                descriptorPayload: JsonSerializer.SerializeToElement(new { }),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>());

    protected static WorkflowExecutionState NewWorkflowState(WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: "wf-1",
            PinnedExecutable: NewIdentity(),
            Status: status,
            SubStatus: null,
            CreatedAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            UpdatedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

    protected static ActivityExecutionState NewActivityStateForStatus(ActivityExecutionStatus status) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: $"actexec-{status.ToString().ToLowerInvariant()}",
                WorkflowExecutionId: "wf-1",
                ExecutableNodeId: "node-a",
                AuthoredActivityId: "authored-a",
                ActivityType: "Elsa.Test",
                ActivityTypeVersion: "1.0.0"),
            Status: status,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: status == ActivityExecutionStatus.Running ? null : DateTimeOffset.UnixEpoch,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.From(
                "wf-1",
                parentActivityExecutionId: null,
                schedulingActivityExecutionId: null,
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    protected RuntimeSchedulerWorkItem NewCancelWorkItem() =>
        new(
            workItemId: "cancel-work",
            workflowExecutionId: "wf-1",
            commandId: "command-cancel",
            commandKind: WorkflowExecutionCommandKind.Cancel,
            envelopeId: "envelope-cancel",
            idempotencyKey: "wf-1:cancel",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: 10);

    protected sealed class RecordingActivityMiddleware : ActivityRuntimeMiddlewareBase
    {
        public int Invocations { get; private set; }

        public override ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate next)
        {
            Invocations++;
            return next(context);
        }
    }

    protected sealed class RecordingHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(RecordingHandler);
        public List<string> WorkItemIds { get; } = [];

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            WorkItemIds.Add(workItem.WorkItemId);
            return ValueTask.CompletedTask;
        }
    }

    protected sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
