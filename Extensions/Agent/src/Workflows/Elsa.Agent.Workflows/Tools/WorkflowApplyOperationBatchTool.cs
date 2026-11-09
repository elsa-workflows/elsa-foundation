using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Workflows.Tools;

/// <summary>
/// Prepares one reviewed Operation Batch for the owning browser's unsaved workflow working draft (spec 1193 FR-007,
/// FR-012, FR-013, FR-015, FR-017). The batch is validated against the admitted snapshot and the owning activity catalog,
/// frozen with every reference resolved, classified per operation, and always reviewed. Only the originating editor
/// applies it, atomically, as one designer-history step; this tool never saves or publishes.
/// </summary>
internal sealed class WorkflowApplyOperationBatchTool(IAgentToolAuthorizer authorizer,
    IEnumerable<IWorkflowDefinitionAuthorizer>? resourceAuthorizers = null,
    IEnumerable<IActivityAuthoringCatalog>? catalogs = null,
    ILogger<WorkflowApplyOperationBatchTool>? logger = null) : IAgentTool, IAgentClientTool, IAgentToolAuthorization
{
    public const string ToolName = "workflow_apply_operation_batch";
    public const string RendererId = "weaver.workflows.batch-result";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Static so the context source can report the same host decision the engine applies to this tool's advertisement.
    internal static AgentToolDescriptor Contract { get; } = new(ToolName, "Change workflow draft",
        "Proposes one reviewed Operation Batch for the exact open workflow working draft: add catalog activities into an explicit scope and slot, " +
        "set the root, move, set declared inputs (literal or a visible Secret Reference), update the display name, description or start setting, " +
        "remove, connect and disconnect. All operations apply together or not at all, as one Undo step. Does not save or publish.",
        AgentToolMutability.Mutating, AgentRisk.ReviewRequired, WorkflowOperationBatch.InputSchema,
        ["workflows/design:manage"], ["/workflows"])
    {
        ModuleId = WorkflowAgentSnapshot.ModuleId, ContractVersion = "1", ExecutionLocation = AgentExecutionLocation.ClientWorkingState,
        ResultType = WorkflowOperationBatch.ResultType, ResultSchemaVersion = "1", ResultRendererId = RendererId,
        DisclosureSourceId = "workflows.context"
    };

    public AgentToolDescriptor Descriptor => Contract;

    // A Reusable Workflow Template is its own resource (FR-024): its authority is the host's design manage decision, never a
    // workflow definition's owner. A definition draft keeps the owning definition authorizer.
    public ValueTask<AgentPolicyDecision> AuthorizeAsync(string actorId, string tenantId, AgentResourceTarget? target,
        CancellationToken cancellationToken = default)
        => target is { ModuleId: WorkflowAgentSnapshot.ModuleId, ResourceType: WorkflowAgentSnapshot.TemplateResourceType, ResourceId.Length: > 0 }
            ? ValueTask.FromResult(new AgentPolicyDecision(true, []))
            : WorkflowResourceAuthority.AuthorizeAsync(resourceAuthorizers, actorId, tenantId, target, true, cancellationToken);

    public async Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var attachments = invocation.Context.Where(item => item.ContentType is "workflow.definition" or WorkflowAgentSnapshot.TemplateContentType
            && item.Origin == AgentContextOrigin.ClientSnapshot).ToArray();
        if (attachments.Length != 1 || !WorkflowAgentSnapshot.TryRead(attachments[0], out var snapshot, out _))
            return Failure("agent.workflow.snapshot_required", "One admitted workflow working-state snapshot is required; nothing was changed.", 409);
        if (invocation.Client is not null && invocation.Client != snapshot!.Client)
            return Failure("agent.workflow.client_mismatch", "The workflow belongs to another editor generation; nothing was changed.", 409);

        // Authority first, so an actor without manage permission learns nothing from validation. The shared denied outcome
        // keeps explanation available; an owner that cannot decide is unavailable, never a denial.
        var host = await authorizer.AuthorizeAsync(Descriptor, invocation.ActorId, invocation.TenantId, snapshot.Target, cancellationToken);
        var owner = !host.Allowed ? null : snapshot.IsTemplate ? host
            : await WorkflowResourceAuthority.AuthorizeAsync(resourceAuthorizers, invocation.ActorId, invocation.TenantId, snapshot.Target, true, cancellationToken);
        if (owner is not null && WorkflowResourceAuthority.IsUnavailable(owner))
            return Failure(WorkflowDefinitionAuthorizationException.UnavailableCode, WorkflowResourceAuthority.UnavailableMessage, 503);
        if (owner is not { Allowed: true })
            return Failure("agent.workflow.permission_denied",
                "Current workflow design manage permission is required to change this workflow; nothing was changed. Explaining it remains available.", 403);

        JsonElement arguments;
        try { arguments = JsonSerializer.SerializeToElement(invocation.Arguments, JsonOptions); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or JsonException)
        {
            return Failure(WorkflowOperationBatch.SchemaInvalid, "The batch arguments must be JSON. The whole batch was refused and nothing changed.", 400);
        }
        var (operations, parseFailure) = WorkflowOperationBatch.Parse(arguments);
        if (parseFailure is not null) return Failure(parseFailure);

        // The catalog is read fresh from its owner for this preparation, never taken from the (possibly truncated) disclosure.
        var catalog = WorkflowOperationBatch.NeedsCatalog(operations!) ? await WorkflowActivityCatalog.ReadAsync(catalogs, cancellationToken, logger) : null;
        var (prepared, failure) = WorkflowOperationBatch.Validate(operations!, snapshot, catalog);
        if (failure is not null) return Failure(failure);
        return AgentResult<AgentToolPreparation>.Success(new(snapshot.Target, prepared!.Arguments, prepared.Risk,
            new(WorkflowOperationBatch.ResultType, "1", prepared.Preview), RequiresApproval: true) { Client = snapshot.Client });
    }

    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentToolResult>.Failure("agent.workflow.client_execution_required",
            "This batch must be applied by the originating workflow editor.", 409));

    public Task<AgentResult<AgentToolResult>> CompleteAsync(AgentPreparedToolInvocation invocation, AgentClientAcknowledgement acknowledgement,
        CancellationToken cancellationToken = default)
    {
        var before = invocation.Target;
        var after = acknowledgement.After;
        var payload = acknowledgement.Result;
        var data = payload?.Data ?? default;
        var frozen = invocation.Arguments.TryGetProperty("operations", out var operations) && operations.ValueKind == JsonValueKind.Array
            ? operations.EnumerateArray().ToArray() : [];
        var addedNodeIds = frozen.Where(operation => WorkflowAgentSnapshot.Text(operation, "kind") == "add")
            .Select(operation => WorkflowAgentSnapshot.Text(operation, "nodeId")).ToArray();
        if (acknowledgement.Outcome != AgentClientOutcome.Applied || before is null || after is null || frozen.Length == 0 ||
            !WorkflowAgentSnapshot.IsEditorTarget(before) ||
            before.ModuleId != after.ModuleId || before.ResourceType != after.ResourceType || before.ResourceId != after.ResourceId ||
            string.IsNullOrWhiteSpace(after.Revision) || after.Revision == before.Revision || after.Revision.Length > 256 ||
            invocation.Client != acknowledgement.Client || invocation.ApprovalDigest != acknowledgement.ApprovalDigest ||
            payload is not { TypeId: WorkflowOperationBatch.ResultType, SchemaVersion: "1" } || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("observed", out var observed) || observed.ValueKind != JsonValueKind.True ||
            WorkflowAgentSnapshot.Text(data, "revision") != after.Revision || WorkflowAgentSnapshot.Text(data, "baseRevision") != before.Revision ||
            !data.TryGetProperty("appliedCount", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var applied) || applied != frozen.Length ||
            !data.TryGetProperty("addedNodeIds", out var added) || added.ValueKind != JsonValueKind.Array ||
            !added.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).SequenceEqual(addedNodeIds) ||
            !data.TryGetProperty("historyEntry", out var history) || history.ValueKind != JsonValueKind.True ||
            WorkflowAgentSnapshot.Text(data, "undoToken") is not { Length: > 0 and <= 256 } undoToken)
            return Task.FromResult(AgentResult<AgentToolResult>.Failure("agent.workflow.observation_invalid",
                "The editor did not confirm the exact approved batch in local working state.", 409));
        // The admitted result carries the server's own review labels, never the client's account of them.
        var preview = invocation.Preview.Data;
        var admitted = new AgentPayload(WorkflowOperationBatch.ResultType, "1", JsonSerializer.SerializeToElement(new
        {
            revision = after.Revision, baseRevision = before.Revision, appliedCount = applied, addedNodeIds, undoToken, observed = true,
            historyEntry = true, risk = WorkflowAgentSnapshot.Text(preview, "risk"),
            highComplexity = preview.TryGetProperty("highComplexity", out var complexity) && complexity.ValueKind == JsonValueKind.True,
            operations = preview.TryGetProperty("operations", out var labels) ? labels : default(JsonElement?),
            clears = new[] { "testRunResult", "publishedArtifactAssociation" }
        }, JsonOptions));
        var observedTarget = new AgentResourceTarget(before.ModuleId, before.ResourceType, before.ResourceId, after.Revision);
        return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            $"The owning workflow editor applied all {applied} operations to unsaved working state as one Undo step. The workflow has not been saved or published.", admitted)
        { Receipt = new(invocation.InvocationId, before, observedTarget, AgentEffectScope.ClientWorkingState, admitted) }));
    }

    private static AgentResult<AgentToolPreparation> Failure(string code, string message, int status)
        => AgentResult<AgentToolPreparation>.Failure(code, message, status);

    private static AgentResult<AgentToolPreparation> Failure(WorkflowOperationBatch.Failure failure)
        => AgentResult<AgentToolPreparation>.Failure(failure.Code, failure.Message, failure.Status);
}
