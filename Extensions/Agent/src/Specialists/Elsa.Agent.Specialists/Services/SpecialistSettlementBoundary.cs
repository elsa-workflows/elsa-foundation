using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Services;

internal sealed record SpecialistAppliedPayload(int Version, string Handle, SpecialistInvocationIdentity Identity, string ChildInvocationId, DateTimeOffset AppliedAt);

internal sealed class SpecialistSettlementBoundary(IWorkflowExecutionStateStore workflows, IActivityExecutionStateStore activities,
    IWorkflowExecutableStore executables, IPersistenceAccessContextAccessor access)
{
    internal const string AppliedKind = "elsa.specialist.applied/1";
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

    internal async ValueTask<SpecialistAppliedPayload> AppliedAsync(WorkflowExecutionState workflow, ActivityExecutionState parent,
        ExecutableNode node, CancellationToken cancellationToken)
    {
        if (parent.Execution.WorkflowExecutionId != workflow.WorkflowExecutionId || parent.Status != ActivityExecutionStatus.Completed ||
            parent.Completion is null || parent.CompletedAt is not { } appliedAt) throw Invalid();
        var identity = SpecialistNativeBinding.Identity(workflow, parent, node);
        var childNode = SpecialistNativeBinding.ChildNode(node);
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
            receipt.GetString() is not { Length: 64 } handle || !SpecialistTurnDocumentRules.IsToken(handle) ||
            acceptedChild.Provenance.Metadata.GetValueOrDefault(SpecialistNativeBinding.TurnHandleMetadataKey) != handle)
            throw Invalid();
        return new(1, handle, identity, acceptedChild.InvocationId, appliedAt);
    }

    internal static RuntimePostCommitIntent Intent(SpecialistAppliedPayload payload)
    {
        var value = JsonSerializer.SerializeToElement(payload, Json);
        var key = "specialist-applied:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, Json))).ToLowerInvariant();
        return new(key, payload.Identity.WorkflowExecutionId, AppliedKind, payload.AppliedAt, payload.Identity.ActivityExecutionId, key, value);
    }

    internal static InvalidDataException Invalid() => new("The specialist settlement does not match its committed native delivery.");
}
