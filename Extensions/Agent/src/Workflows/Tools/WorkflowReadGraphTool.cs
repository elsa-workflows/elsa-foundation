using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Services;

namespace Elsa.Agent.Workflows.Tools;

/// <summary>Reads the admitted, value-free graph snapshot without claiming a durable resource read.</summary>
public sealed class WorkflowReadGraphTool : IAgentTool
{
    public const string ToolName = "workflow_read_graph";
    public AgentToolDescriptor Descriptor { get; } = new(ToolName, "Read open workflow graph",
        "Returns admitted activity identities, connections, positions and working-state revision. Activity property values are excluded.",
        AgentToolMutability.ReadOnly, AgentRisk.ReadOnly, """{"type":"object","properties":{},"additionalProperties":false}""",
        ["workflows/design:read"], ["/workflows"])
    { ModuleId = WorkflowAgentSnapshot.ModuleId, ResultType = "workflows.graph", ResultSchemaVersion = "1" };

    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var attachments = invocation.Context.Where(item => item.ContentType == "workflow.definition" && item.Origin == AgentContextOrigin.ClientSnapshot).ToArray();
        if (invocation.Arguments.Count != 0 || attachments.Length != 1 || !WorkflowAgentSnapshot.TryRead(attachments[0], out var snapshot, out _))
            return Task.FromResult(AgentResult<AgentToolPreparation>.Failure("agent.workflow.snapshot_required", "One admitted workflow snapshot and no arguments are required.", 409));
        return Task.FromResult(AgentResult<AgentToolPreparation>.Success(new(snapshot!.Target, snapshot.Content, AgentRisk.ReadOnly,
            new("workflows.graph", "1", snapshot.Content), RequiresApproval: false)));
    }

    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            "Read the admitted workflow working-state snapshot; no resource was changed.", new("workflows.graph", "1", invocation.Arguments))));
}
