using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Contracts;
using Elsa.Agent.Workflows.Models;
using Elsa.Agent.Workflows.Services;

namespace Elsa.Agent.Workflows.Tools;

/// <summary>
/// Mutating tool that turns a <see cref="WorkflowGraphOperationBatch"/> into a reviewable workflow-change
/// proposal. It hydrates the workflow context from the graph the client supplied, classifies the batch risk,
/// and routes through <see cref="IWorkflowChangeProposalService"/>. Because it is declared mutating, the
/// policy-aware tool invoker gates it (proposal under review policy, inline under full-auto). Registered now so
/// it activates once the provider surfaces tool calls; today it is exercised through the deterministic seam.
/// </summary>
public sealed class WorkflowApplyGraphOperationsTool(
    IWorkflowAgentContextProvider contextProvider,
    IWorkflowGraphOperationBatchRiskClassifier riskClassifier,
    IWorkflowChangeProposalService proposalService) : IAgentTool
{
    public const string ToolName = "workflow.apply_graph_operations";

    // Web defaults (camelCase, case-insensitive) plus string enum names, matching how a provider emits a batch.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public AgentToolDescriptor Descriptor { get; } = new(
        ToolName,
        "Apply workflow graph operations",
        "Submits a workflow graph operation batch (add/connect/update/remove activities) as a reviewable change proposal.",
        AgentToolMutability.Mutating,
        AgentRisk.ReviewRequired,
        """{"type":"object","required":["batch"],"properties":{"batch":{"type":"object","description":"An Elsa workflow graph operation batch."}},"additionalProperties":false}""",
        [],
        ["/workflows"]);

    public async Task<AgentResult<AgentToolResult>> InvokeAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        if (!TryReadBatch(invocation.Arguments, out var batch) || batch is null)
            return AgentResult<AgentToolResult>.Failure("agent.workflow.invalid_batch", "The 'batch' argument is missing or is not a valid workflow graph operation batch.");

        var graph = WorkflowAgentGraphReader.Read(invocation.Context);
        var context = await contextProvider.GetContextAsync(
            new(invocation.SessionId, batch.WorkflowDefinitionId, "draft", Graph: graph),
            cancellationToken);

        var classification = riskClassifier.Classify(new(batch, context));
        var baseRevision = batch.BaseRevision ?? graph.Revision ?? context.Revision;

        var proposal = await proposalService.ProposeAsync(new(
            invocation.SessionId,
            invocation.ActorId,
            batch.WorkflowDefinitionId,
            baseRevision,
            $"Apply {batch.Operations.Count} workflow graph operation(s)",
            classification.Summary,
            batch.Operations.Select(ToOperationDictionary).ToList(),
            classification.Reasons.Select(x => x.ToString()).ToList(),
            "Discard the proposed batch to restore the previous draft revision."), cancellationToken);

        if (!proposal.Succeeded || proposal.Value is null)
            return AgentResult<AgentToolResult>.Failure(
                proposal.Error?.Code ?? "agent.workflow.proposal_failed",
                proposal.Error?.Message ?? "Failed to create a workflow change proposal.",
                proposal.Error?.StatusCode ?? 400);

        return AgentResult<AgentToolResult>.Success(new(
            invocation.ToolCallId,
            Descriptor.Name,
            true,
            $"Created workflow change proposal '{proposal.Value.Id}' ({classification.Decision}).",
            proposal.Value));
    }

    private static IReadOnlyDictionary<string, object?> ToOperationDictionary(WorkflowGraphOperation operation)
        => new Dictionary<string, object?>
        {
            ["op"] = operation.Kind.ToString(),
            ["id"] = operation.Id,
            ["parameters"] = operation.Parameters,
            ["summary"] = operation.Summary
        };

    private static bool TryReadBatch(IReadOnlyDictionary<string, object?> arguments, out WorkflowGraphOperationBatch? batch)
    {
        batch = null;
        if (!arguments.TryGetValue("batch", out var value) || value is null)
            return false;

        switch (value)
        {
            case WorkflowGraphOperationBatch typed:
                batch = typed;
                return true;
            case JsonElement element:
                return TryDeserialize(element, out batch);
            default:
                try
                {
                    return TryDeserialize(JsonSerializer.SerializeToElement(value, SerializerOptions), out batch);
                }
                catch (NotSupportedException)
                {
                    return false;
                }
        }
    }

    private static bool TryDeserialize(JsonElement element, out WorkflowGraphOperationBatch? batch)
    {
        batch = null;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        try
        {
            batch = element.Deserialize<WorkflowGraphOperationBatch>(SerializerOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        return batch is not null && !string.IsNullOrWhiteSpace(batch.WorkflowDefinitionId);
    }
}
