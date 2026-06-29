using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Services;

namespace Elsa.Agent.Workflows.Tools;

/// <summary>
/// Read-only tool that returns the current workflow graph (activities, connections, revision) so a
/// multi-step agent can re-read the graph after edits, or when the graph was too large to inline up front.
/// It reads from the <c>workflow.definition</c> attachment Studio supplies on the turn; a future revision can
/// read straight from the draft store. Registered now so it activates once the provider surfaces tool calls.
/// </summary>
public sealed class WorkflowReadGraphTool : IAgentTool
{
    public const string ToolName = "workflow.read_graph";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public AgentToolDescriptor Descriptor { get; } = new(
        ToolName,
        "Read workflow graph",
        "Returns the open workflow's activities, connections, and base revision so edits can reference real node ids.",
        AgentToolMutability.ReadOnly,
        AgentRisk.ReadOnly,
        """{"type":"object","properties":{},"additionalProperties":false}""",
        [],
        ["/workflows"]);

    public Task<AgentResult<AgentToolResult>> InvokeAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var graph = WorkflowAgentGraphReader.Read(invocation.Context);
        var content = JsonSerializer.Serialize(graph, SerializerOptions);
        return Task.FromResult(AgentResult<AgentToolResult>.Success(
            new(invocation.ToolCallId, Descriptor.Name, true, content, graph)));
    }
}
