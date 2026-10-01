using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Scheduler work items for store and sweep tests, with every projection the EF store checks populated.</summary>
internal static class SchedulerWorkItems
{
    public static RuntimeSchedulerWorkItem Work(
        string workflowExecutionId,
        string workItemId,
        long sequence,
        WorkflowExecutionCommandKind kind = WorkflowExecutionCommandKind.ScheduleActivity) =>
        new(workItemId, workflowExecutionId, $"command-{workItemId}", kind,
            $"envelope-{workItemId}", $"idempotency-{workItemId}",
            new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.FromHours(1)),
            new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.FromHours(1)),
            sequence,
            JsonSerializer.SerializeToElement(new { sequence }),
            new Dictionary<string, string> { ["command"] = "metadata" },
            new Dictionary<string, string> { ["envelope"] = "metadata" });
}

internal sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class NoOutboxProcessor : IRuntimePostCommitOutboxProcessor
{
    public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
        RuntimePostCommitOutboxProcessRequest request,
        CancellationToken cancellationToken = default) => new(new RuntimePostCommitOutboxProcessResult([]));
}

/// <summary>An execution actor provider whose single agent records every re-drive and accepts it.</summary>
internal sealed class RecordingAgentProvider : IWorkflowExecutionActorProvider, IWorkflowExecutionActor
{
    public List<string> Redriven { get; } = [];

    public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;

    public WorkflowExecutionActorDescriptor Descriptor { get; } = new(
        workflowExecutionId: "wfexec-agent",
        agentId: "agent-1",
        providerName: "test",
        status: WorkflowExecutionActorStatus.Active,
        capabilities: WorkflowExecutionActorCapabilities.InProcessMailbox,
        activatedAt: DateTimeOffset.UnixEpoch);

    public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) =>
        new(this);

    public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => default;

    public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        Redriven.Add(envelope.WorkflowExecutionId);
        return new(new WorkflowExecutionCommandDispatchResult(
            envelope.EnvelopeId,
            envelope.WorkflowExecutionId,
            WorkflowExecutionCommandDispatchStatus.Accepted,
            envelope.EnqueuedAt));
    }
}
