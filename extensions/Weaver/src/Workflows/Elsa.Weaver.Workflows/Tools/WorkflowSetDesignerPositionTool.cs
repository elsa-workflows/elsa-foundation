using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Services;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Weaver.Workflows.Tools;

/// <summary>Prepares one exact, reviewed change to the owning browser's unsaved workflow layout.</summary>
internal sealed class WorkflowSetDesignerPositionTool(IWeaverToolAuthorizer authorizer,
    IEnumerable<IWorkflowDefinitionAuthorizer>? resourceAuthorizers = null) : IWeaverTool, IWeaverClientTool, IWeaverToolAuthorization
{
    public const string ToolName = "workflow_set_designer_position";
    public const string ResultType = "workflows.layout-change";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // Static so the context source can report the same host decision the engine applies to this tool's advertisement.
    internal static WeaverToolDescriptor Contract { get; } = new(ToolName, "Set activity position",
        "Proposes a reviewed position change for an existing activity in the exact open workflow working state. Does not save or publish.",
        WeaverToolMutability.Mutating, WeaverRisk.ReviewRequired,
        """{"type":"object","required":["activityId","x","y"],"properties":{"activityId":{"type":"string"},"x":{"type":"number"},"y":{"type":"number"}},"additionalProperties":false}""",
        ["workflows/design:manage"], ["/workflows"])
    {
        ModuleId = WorkflowWeaverSnapshot.ModuleId, ContractVersion = "1", ExecutionLocation = WeaverExecutionLocation.ClientWorkingState,
        ResultType = ResultType, ResultSchemaVersion = "1", ResultRendererId = "workflows.weaver.layout-result",
        DisclosureSourceId = "workflows.context"
    };

    public WeaverToolDescriptor Descriptor => Contract;

    public ValueTask<WeaverPolicyDecision> AuthorizeAsync(string actorId, string tenantId, WeaverResourceTarget? target,
        CancellationToken cancellationToken = default)
        => WorkflowResourceAuthority.AuthorizeAsync(resourceAuthorizers, actorId, tenantId, target, true, cancellationToken);

    public async Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var attachments = invocation.Context.Where(item => item.ContentType == "workflow.definition" && item.Origin == WeaverContextOrigin.ClientSnapshot).ToArray();
        if (attachments.Length != 1 || !WorkflowWeaverSnapshot.TryRead(attachments[0], out var snapshot, out _))
            return WeaverResult<WeaverToolPreparation>.Failure("weaver.workflow.snapshot_required", "One admitted workflow working-state snapshot is required.", 409);
        if (invocation.Client is not null && invocation.Client != snapshot!.Client)
            return WeaverResult<WeaverToolPreparation>.Failure("weaver.workflow.client_mismatch", "The workflow belongs to another editor generation.", 409);
        JsonElement arguments;
        try { arguments = JsonSerializer.SerializeToElement(invocation.Arguments, JsonOptions); }
        catch (ArgumentException) { return WeaverResult<WeaverToolPreparation>.Failure("weaver.workflow.invalid_position", "The coordinates must be finite JSON numbers."); }
        var activityId = WorkflowWeaverSnapshot.Text(arguments, "activityId");
        if (arguments.EnumerateObject().Count() != 3 || !snapshot.Graph.Activities.Any(node => node.Id == activityId) ||
            !WorkflowWeaverSnapshot.Number(arguments, "x", out var x) || !WorkflowWeaverSnapshot.Number(arguments, "y", out var y))
            return WeaverResult<WeaverToolPreparation>.Failure("weaver.workflow.invalid_position", "The layout change requires an existing activity and finite x/y coordinates.");
        var authorization = await authorizer.AuthorizeAsync(Descriptor, invocation.ActorId, invocation.TenantId, snapshot.Target, cancellationToken);
        if (!authorization.Allowed)
            return WeaverResult<WeaverToolPreparation>.Failure("weaver.workflow.permission_denied", "Current workflow design permission is required.", 403);
        var frozen = JsonSerializer.SerializeToElement(new { activityId, x, y }, JsonOptions);
        var preview = new WeaverPayload(ResultType, "1", JsonSerializer.SerializeToElement(new
        {
            activityId, x, y, revision = snapshot.Target.Revision, draftId = snapshot.DraftId,
            summary = "Move this existing activity in the unsaved workflow layout. Saving and publishing remain explicit."
        }, JsonOptions));
        return WeaverResult<WeaverToolPreparation>.Success(new(snapshot.Target, frozen, WeaverRisk.ReviewRequired, preview, RequiresApproval: true)
        { Client = snapshot.Client });
    }

    public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(WeaverResult<WeaverToolResult>.Failure("weaver.workflow.client_execution_required", "This layout change must be applied by the originating workflow editor.", 409));

    public Task<WeaverResult<WeaverToolResult>> CompleteAsync(WeaverPreparedToolInvocation invocation, WeaverClientAcknowledgement acknowledgement,
        CancellationToken cancellationToken = default)
    {
        var before = invocation.Target;
        var after = acknowledgement.After;
        var payload = acknowledgement.Result;
        var args = invocation.Arguments;
        var data = payload?.Data ?? default;
        if (acknowledgement.Outcome != WeaverClientOutcome.Applied || before is null || after is null ||
            before.ModuleId != WorkflowWeaverSnapshot.ModuleId || before.ResourceType != WorkflowWeaverSnapshot.ResourceType ||
            before.ModuleId != after.ModuleId || before.ResourceType != after.ResourceType || before.ResourceId != after.ResourceId ||
            string.IsNullOrWhiteSpace(after.Revision) || after.Revision == before.Revision || after.Revision.Length > 256 ||
            invocation.Client != acknowledgement.Client || invocation.ApprovalDigest != acknowledgement.ApprovalDigest ||
            payload is not { TypeId: ResultType, SchemaVersion: "1" } || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("observed", out var observed) || observed.ValueKind != JsonValueKind.True ||
            WorkflowWeaverSnapshot.Text(data, "activityId") != WorkflowWeaverSnapshot.Text(args, "activityId") ||
            WorkflowWeaverSnapshot.Text(data, "revision") != after.Revision ||
            !WorkflowWeaverSnapshot.Number(args, "x", out var expectedX) || !WorkflowWeaverSnapshot.Number(args, "y", out var expectedY) ||
            !WorkflowWeaverSnapshot.Number(data, "x", out var x) || !WorkflowWeaverSnapshot.Number(data, "y", out var y) || x != expectedX || y != expectedY ||
            WorkflowWeaverSnapshot.Text(data, "undoToken") is not { Length: > 0 and <= 256 } undoToken)
            return Task.FromResult(WeaverResult<WeaverToolResult>.Failure("weaver.workflow.observation_invalid", "The editor did not confirm the exact approved local layout change.", 409));
        var admitted = new WeaverPayload(ResultType, "1", JsonSerializer.SerializeToElement(new
        {
            activityId = WorkflowWeaverSnapshot.Text(args, "activityId"), x, y, revision = after.Revision, undoToken, observed = true
        }, JsonOptions));
        var observedTarget = new WeaverResourceTarget(before.ModuleId, before.ResourceType, before.ResourceId, after.Revision);
        return Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            "The owning workflow editor observed the layout change in unsaved working state. The workflow has not been saved or published.", admitted)
        { Receipt = new(invocation.InvocationId, before, observedTarget, WeaverEffectScope.ClientWorkingState, admitted) }));
    }
}
