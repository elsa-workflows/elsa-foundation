using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Agent.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Services;

internal sealed record AgentCancelledPayload(int Version, AgentInvocationIdentity Identity, string? ChildInvocationId, DateTimeOffset CancelledAt);

internal sealed record AgentAppliedPayload(int Version, string Handle, AgentInvocationIdentity Identity, string ChildInvocationId, DateTimeOffset AppliedAt);

internal sealed class AgentSettlementBoundary(IWorkflowExecutionStateStore workflows, IActivityExecutionStateStore activities,
    IWorkflowExecutableStore executables, IPersistenceAccessContextAccessor access)
{
    internal const string CancelKind = "elsa.agent.cancel/1";
    internal const string AppliedKind = "elsa.agent.applied/1";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal async ValueTask<(WorkflowExecutionState Workflow, WorkflowExecutable Executable)> WorkflowAsync(string workflowId, CancellationToken cancellationToken)
    {
        var scope = access.Current.RequireScope();
        var workflow = await workflows.FindAsync(workflowId, cancellationToken) ?? throw Invalid();
        access.Current.EnsureTenantScope(workflow.TenantId);
        if (workflow.Partition?.Value != scope.Value) throw Invalid();
        var executable = await executables.FindAsync(workflow.PinnedExecutable.ArtifactId, cancellationToken) ?? throw Invalid();
        if (executable.Identity.ArtifactHash != workflow.PinnedExecutable.ArtifactHash) throw Invalid();
        return (workflow, executable);
    }

    internal async ValueTask<AgentAppliedPayload> AppliedAsync(WorkflowExecutionState workflow, ActivityExecutionState parent,
        ExecutableNode node, CancellationToken cancellationToken)
    {
        if (parent.Execution.WorkflowExecutionId != workflow.WorkflowExecutionId || parent.Status != ActivityExecutionStatus.Completed ||
            parent.Completion is null || parent.CompletedAt is not { } appliedAt) throw Invalid();
        var identity = AgentNativeBinding.Identity(workflow, parent, node);
        var childNode = AgentNativeBinding.ChildNode(node);
        ActivityExecutionState? acceptedChild = null;
        string? continuation = null;
        do
        {
            var page = await activities.ListByParentPageAsync(new(workflow.WorkflowExecutionId, parent.InvocationId, continuationToken: continuation), cancellationToken);
            foreach (var child in page.Items.Where(item => item.Execution.ExecutableNodeId == childNode.ExecutableNodeId && item.Status == ActivityExecutionStatus.Completed))
            {
                if (acceptedChild is not null) throw Invalid();
                acceptedChild = child;
            }
            if (page.NextContinuationToken is not null && page.NextContinuationToken == continuation) throw Invalid();
            continuation = page.NextContinuationToken;
        } while (continuation is not null);
        if (acceptedChild is null || acceptedChild.ParentActivityExecutionId != parent.InvocationId || acceptedChild.Execution.WorkflowExecutionId != workflow.WorkflowExecutionId ||
            acceptedChild.Completion?.Result.InlineValue is not { ValueKind: JsonValueKind.String } receipt ||
            receipt.GetString() is not { Length: 64 } handle || !AgentTurnDocumentRules.IsToken(handle) ||
            acceptedChild.Provenance.Metadata.GetValueOrDefault(AgentNativeBinding.TurnHandleMetadataKey) != handle)
            throw Invalid();
        return new(1, handle, identity, acceptedChild.InvocationId, appliedAt);
    }

    internal async ValueTask<AgentCancelledPayload> CancelledAsync(WorkflowExecutionState workflow, ActivityExecutionState parent,
        ExecutableNode node, CancellationToken cancellationToken)
    {
        if (parent.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Superseded ||
            workflow.Status != WorkflowExecutionStatus.Cancelled && parent.Status != ActivityExecutionStatus.Cancelled) throw Invalid();
        var times = new List<DateTimeOffset>();
        if (parent.Status == ActivityExecutionStatus.Cancelled) times.Add(parent.CompletedAt ?? throw Invalid());
        if (workflow.Status == WorkflowExecutionStatus.Cancelled) times.Add(workflow.CompletedAt ?? throw Invalid());
        var identity = AgentNativeBinding.CancellationIdentity(workflow, parent, node);
        var worker = AgentNativeBinding.ChildNode(node);
        string? childId = null;
        string? continuation = null;
        do
        {
            var page = await activities.ListByParentPageAsync(new(workflow.WorkflowExecutionId, parent.InvocationId, continuationToken: continuation), cancellationToken);
            foreach (var child in page.Items.Where(item => item.Execution.ExecutableNodeId == worker.ExecutableNodeId))
            {
                if (childId is not null || child.Execution.WorkflowExecutionId != workflow.WorkflowExecutionId || child.ParentActivityExecutionId != parent.InvocationId) throw Invalid();
                childId = child.InvocationId;
            }
            if (page.NextContinuationToken is not null && page.NextContinuationToken == continuation) throw Invalid();
            continuation = page.NextContinuationToken;
        } while (continuation is not null);
        return new(1, identity, childId, times.Min());
    }

    internal static RuntimePostCommitIntent Intent(AgentCancelledPayload payload)
    {
        var value = JsonSerializer.SerializeToElement(payload, Json);
        var key = "agent-cancel:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, Json))).ToLowerInvariant();
        return new(key, payload.Identity.WorkflowExecutionId, CancelKind, payload.CancelledAt, payload.Identity.ActivityExecutionId, key, value);
    }

    internal static RuntimePostCommitIntent Intent(AgentAppliedPayload payload)
    {
        var value = JsonSerializer.SerializeToElement(payload, Json);
        var key = "agent-applied:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, Json))).ToLowerInvariant();
        return new(key, payload.Identity.WorkflowExecutionId, AppliedKind, payload.AppliedAt, payload.Identity.ActivityExecutionId, key, value);
    }

    internal static AgentTurnException Failure(string contextId, string workflowExecutionId, Exception cause)
    {
        var failure = AgentModelRunner.Failure("agent.settlement.failed", "The native agent application settlement could not be confirmed.", cause);
        failure.Data["contextId"] = contextId;
        failure.Data["workflowExecutionId"] = workflowExecutionId;
        return failure;
    }

    internal static InvalidDataException Invalid() => new("The agent settlement does not match its committed native delivery.");
}
