using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Services;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Agent.Workflows.Tools;

/// <summary>
/// Reads the admitted working-state snapshot without claiming a durable resource read: the activities with their scope-path
/// addresses, connections, positions, the designer's current diagnostics, graph activity references by definition and
/// version, and the selected activity's bounded property context, all at the snapshot's revision.
/// </summary>
public sealed class WorkflowReadGraphTool(IEnumerable<IWorkflowDefinitionAuthorizer>? resourceAuthorizers = null) : IAgentTool, IAgentToolAuthorization
{
    public const string ToolName = "workflow_read_graph";
    public AgentToolDescriptor Descriptor { get; } = new(ToolName, "Read open workflow graph",
        "Returns the open workflow's admitted working state at its revision: activity identities with their scope paths through nested containers, " +
        "connections, positions, the designer's current validation diagnostics, graph activities by definition and version (never their bodies), and the " +
        "selected activity's declared properties with expression types and bounded content. Credential values are never included.",
        AgentToolMutability.ReadOnly, AgentRisk.ReadOnly, """{"type":"object","properties":{},"additionalProperties":false}""",
        ["workflows/design:read"], ["/workflows"])
    {
        ModuleId = WorkflowAgentSnapshot.ModuleId, ResultType = "workflows.graph", ResultSchemaVersion = "1",
        DisclosureSourceId = "workflows.context"
    };

    public ValueTask<AgentPolicyDecision> AuthorizeAsync(string actorId, string tenantId, AgentResourceTarget? target,
        CancellationToken cancellationToken = default)
        => WorkflowResourceAuthority.AuthorizeAsync(resourceAuthorizers, actorId, tenantId, target, false, cancellationToken);

    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var attachments = invocation.Context.Where(item => item.ContentType == "workflow.definition" && item.Origin == AgentContextOrigin.ClientSnapshot).ToArray();
        if (invocation.Arguments.Count != 0 || attachments.Length != 1 || !WorkflowAgentSnapshot.TryRead(attachments[0], out var snapshot, out _))
            return Task.FromResult(AgentResult<AgentToolPreparation>.Failure("agent.workflow.snapshot_required", "One admitted workflow snapshot and no arguments are required.", 409));
        var content = Result(snapshot!);
        return Task.FromResult(AgentResult<AgentToolPreparation>.Success(new(snapshot.Target, content, AgentRisk.ReadOnly,
            new("workflows.graph", "1", content), RequiresApproval: false)));
    }

    /// <summary>
    /// The snapshot plus each activity's scope-path address (FR-014), when the editor disclosed its structure. The addresses
    /// are bounded; <c>addressesTruncated</c> says when some were left out.
    /// </summary>
    private static JsonElement Result(WorkflowAgentSnapshot snapshot)
    {
        if (snapshot.Structure is null || snapshot.Structure.Count == 0) return snapshot.Content;
        var body = JsonNode.Parse(snapshot.Content.GetRawText())!.AsObject();
        var (addresses, truncated) = WorkflowAgentExplainContext.Addresses(snapshot.Graph.Activities.Select(item => item.Id).ToArray(), snapshot.Structure);
        body["addresses"] = JsonSerializer.SerializeToNode(addresses, JsonOptions);
        body["addressesTruncated"] = truncated;
        return JsonSerializer.SerializeToElement(body, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            "Read the admitted workflow working-state snapshot; no resource was changed.", new("workflows.graph", "1", invocation.Arguments))));
}
