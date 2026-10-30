using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Services;

namespace Elsa.Agent.Workflows.Tools;

/// <summary>Prepares one exact, reviewed change to the owning browser's unsaved workflow layout.</summary>
internal sealed class WorkflowSetDesignerPositionTool(IAgentToolAuthorizer authorizer) : IAgentTool, IAgentClientTool
{
    public const string ToolName = "workflow_set_designer_position";
    public const string ResultType = "workflows.layout-change";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public AgentToolDescriptor Descriptor { get; } = new(ToolName, "Set activity position",
        "Proposes a reviewed position change for an existing activity in the exact open workflow working state. Does not save or publish.",
        AgentToolMutability.Mutating, AgentRisk.ReviewRequired,
        """{"type":"object","required":["activityId","x","y"],"properties":{"activityId":{"type":"string"},"x":{"type":"number"},"y":{"type":"number"}},"additionalProperties":false}""",
        ["workflows/design:manage"], ["/workflows"])
    {
        ModuleId = WorkflowAgentSnapshot.ModuleId, ContractVersion = "1", ExecutionLocation = AgentExecutionLocation.ClientWorkingState,
        ResultType = ResultType, ResultSchemaVersion = "1", ResultRendererId = "workflows.agent.layout-result"
    };

    public async Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var attachments = invocation.Context.Where(item => item.ContentType == "workflow.definition" && item.Origin == AgentContextOrigin.ClientSnapshot).ToArray();
        if (attachments.Length != 1 || !WorkflowAgentSnapshot.TryRead(attachments[0], out var snapshot, out _))
            return AgentResult<AgentToolPreparation>.Failure("agent.workflow.snapshot_required", "One admitted workflow working-state snapshot is required.", 409);
        if (invocation.Client is not null && invocation.Client != snapshot!.Client)
            return AgentResult<AgentToolPreparation>.Failure("agent.workflow.client_mismatch", "The workflow belongs to another editor generation.", 409);
        JsonElement arguments;
        try { arguments = JsonSerializer.SerializeToElement(invocation.Arguments, JsonOptions); }
        catch (ArgumentException) { return AgentResult<AgentToolPreparation>.Failure("agent.workflow.invalid_position", "The coordinates must be finite JSON numbers."); }
        var activityId = WorkflowAgentSnapshot.Text(arguments, "activityId");
        if (arguments.EnumerateObject().Count() != 3 || !snapshot.Graph.Activities.Any(node => node.Id == activityId) ||
            !WorkflowAgentSnapshot.Number(arguments, "x", out var x) || !WorkflowAgentSnapshot.Number(arguments, "y", out var y))
            return AgentResult<AgentToolPreparation>.Failure("agent.workflow.invalid_position", "The layout change requires an existing activity and finite x/y coordinates.");
        var authorization = await authorizer.AuthorizeAsync(Descriptor, invocation.ActorId, invocation.TenantId, snapshot.Target, cancellationToken);
        if (!authorization.Allowed)
            return AgentResult<AgentToolPreparation>.Failure("agent.workflow.permission_denied", "Current workflow design permission is required.", 403);
        var frozen = JsonSerializer.SerializeToElement(new { activityId, x, y }, JsonOptions);
        var preview = new AgentPayload(ResultType, "1", JsonSerializer.SerializeToElement(new
        {
            activityId, x, y, revision = snapshot.Target.Revision, draftId = snapshot.DraftId,
            summary = "Move this existing activity in the unsaved workflow layout. Saving and publishing remain explicit."
        }, JsonOptions));
        return AgentResult<AgentToolPreparation>.Success(new(snapshot.Target, frozen, AgentRisk.ReviewRequired, preview, RequiresApproval: true)
        { Client = snapshot.Client });
    }

    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentToolResult>.Failure("agent.workflow.client_execution_required", "This layout change must be applied by the originating workflow editor.", 409));

    public Task<AgentResult<AgentToolResult>> CompleteAsync(AgentPreparedToolInvocation invocation, AgentClientAcknowledgement acknowledgement,
        CancellationToken cancellationToken = default)
    {
        var before = invocation.Target;
        var after = acknowledgement.After;
        var payload = acknowledgement.Result;
        var args = invocation.Arguments;
        var data = payload?.Data ?? default;
        if (acknowledgement.Outcome != AgentClientOutcome.Applied || before is null || after is null ||
            before.ModuleId != WorkflowAgentSnapshot.ModuleId || before.ResourceType != WorkflowAgentSnapshot.ResourceType ||
            before.ModuleId != after.ModuleId || before.ResourceType != after.ResourceType || before.ResourceId != after.ResourceId ||
            string.IsNullOrWhiteSpace(after.Revision) || after.Revision == before.Revision || after.Revision.Length > 256 ||
            invocation.Client != acknowledgement.Client || invocation.ApprovalDigest != acknowledgement.ApprovalDigest ||
            payload is not { TypeId: ResultType, SchemaVersion: "1" } || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("observed", out var observed) || observed.ValueKind != JsonValueKind.True ||
            WorkflowAgentSnapshot.Text(data, "activityId") != WorkflowAgentSnapshot.Text(args, "activityId") ||
            WorkflowAgentSnapshot.Text(data, "revision") != after.Revision ||
            !WorkflowAgentSnapshot.Number(args, "x", out var expectedX) || !WorkflowAgentSnapshot.Number(args, "y", out var expectedY) ||
            !WorkflowAgentSnapshot.Number(data, "x", out var x) || !WorkflowAgentSnapshot.Number(data, "y", out var y) || x != expectedX || y != expectedY ||
            WorkflowAgentSnapshot.Text(data, "undoToken") is not { Length: > 0 and <= 256 } undoToken)
            return Task.FromResult(AgentResult<AgentToolResult>.Failure("agent.workflow.observation_invalid", "The editor did not confirm the exact approved local layout change.", 409));
        var admitted = new AgentPayload(ResultType, "1", JsonSerializer.SerializeToElement(new
        {
            activityId = WorkflowAgentSnapshot.Text(args, "activityId"), x, y, revision = after.Revision, undoToken, observed = true
        }, JsonOptions));
        var observedTarget = new AgentResourceTarget(before.ModuleId, before.ResourceType, before.ResourceId, after.Revision);
        return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            "The owning workflow editor observed the layout change in unsaved working state. The workflow has not been saved or published.", admitted)
        { Receipt = new(invocation.InvocationId, before, observedTarget, AgentEffectScope.ClientWorkingState, admitted) }));
    }
}
