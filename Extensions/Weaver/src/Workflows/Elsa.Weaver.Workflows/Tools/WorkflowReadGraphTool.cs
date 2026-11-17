using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Services;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Weaver.Workflows.Tools;

/// <summary>
/// Reads the admitted working-state snapshot without claiming a durable resource read: the activities with their scope-path
/// addresses, connections, positions, the designer's current diagnostics, graph activity references by definition and
/// version, and the selected activity's bounded property context, all at the snapshot's revision.
/// </summary>
public sealed class WorkflowReadGraphTool(IEnumerable<IWorkflowDefinitionAuthorizer>? resourceAuthorizers = null) : IWeaverTool, IWeaverToolAuthorization
{
    public const string ToolName = "workflow_read_graph";
    public WeaverToolDescriptor Descriptor { get; } = new(ToolName, "Read open workflow graph",
        "Returns the open workflow's admitted working state at its revision: activity identities with their scope paths through nested containers, " +
        "connections, positions, the designer's current validation diagnostics, graph activities by definition and version (never their bodies), and the " +
        "selected activity's declared properties with expression types and bounded content. Credential values are never included.",
        WeaverToolMutability.ReadOnly, WeaverRisk.ReadOnly, """{"type":"object","properties":{},"additionalProperties":false}""",
        ["workflows/design:read"], ["/workflows"])
    {
        ModuleId = WorkflowWeaverSnapshot.ModuleId, ResultType = "workflows.graph", ResultSchemaVersion = "1",
        DisclosureSourceId = "workflows.context"
    };

    public ValueTask<WeaverPolicyDecision> AuthorizeAsync(string actorId, string tenantId, WeaverResourceTarget? target,
        CancellationToken cancellationToken = default)
        => WorkflowResourceAuthority.AuthorizeAsync(resourceAuthorizers, actorId, tenantId, target, false, cancellationToken);

    public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var attachments = invocation.Context.Where(item => item.ContentType == "workflow.definition" && item.Origin == WeaverContextOrigin.ClientSnapshot).ToArray();
        if (invocation.Arguments.Count != 0 || attachments.Length != 1 || !WorkflowWeaverSnapshot.TryRead(attachments[0], out var snapshot, out _))
            return Task.FromResult(WeaverResult<WeaverToolPreparation>.Failure("weaver.workflow.snapshot_required", "One admitted workflow snapshot and no arguments are required.", 409));
        var content = Result(snapshot!);
        return Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(new(snapshot.Target, content, WeaverRisk.ReadOnly,
            new("workflows.graph", "1", content), RequiresApproval: false)));
    }

    /// <summary>
    /// The snapshot plus each activity's scope-path address (FR-014), when the editor disclosed its structure. The addresses
    /// are bounded; <c>addressesTruncated</c> says when some were left out.
    /// </summary>
    private static JsonElement Result(WorkflowWeaverSnapshot snapshot)
    {
        if (snapshot.Structure is null || snapshot.Structure.Count == 0) return snapshot.Content;
        var body = JsonNode.Parse(snapshot.Content.GetRawText())!.AsObject();
        var (addresses, truncated) = WorkflowWeaverExplainContext.Addresses(snapshot.Graph.Activities.Select(item => item.Id).ToArray(), snapshot.Structure);
        body["addresses"] = JsonSerializer.SerializeToNode(addresses, JsonOptions);
        body["addressesTruncated"] = truncated;
        return JsonSerializer.SerializeToElement(body, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            "Read the admitted workflow working-state snapshot; no resource was changed.", new("workflows.graph", "1", invocation.Arguments))));
}
