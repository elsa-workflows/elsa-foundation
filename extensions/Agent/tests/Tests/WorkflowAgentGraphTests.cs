using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Extensions;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Contracts;
using Elsa.Agent.Workflows.Extensions;
using Elsa.Agent.Workflows.Models;
using Elsa.Agent.Workflows.Services;
using Elsa.Agent.Workflows.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

/// <summary>
/// Covers the graph-aware Weaver context path: Studio sends the live workflow graph inline as a
/// <c>workflow.definition</c> attachment, the backend hydrates context/classifier from it, and the
/// authoring tools read/apply against the real node ids.
/// </summary>
public sealed class WorkflowAgentGraphTests
{
    private const string GraphJson = """
    {
        "workflowId": "wf-1",
        "version": "draft",
        "revision": "rev-7",
        "activities": [
            { "id": "WriteLine1", "type": "Elsa.Workflows.WriteLine", "displayName": "Write Line" },
            { "id": "WriteLine2", "type": "Elsa.Workflows.WriteLine", "displayName": "Write Line" }
        ],
        "connections": [
            { "source": "WriteLine1", "target": "WriteLine2", "sourcePort": "Done" }
        ]
    }
    """;

    [Fact]
    public void Graph_reader_parses_activities_connections_and_revision()
    {
        var graph = WorkflowAgentGraphReader.Read([CreateGraphAttachment()]);

        Assert.Equal("rev-7", graph.Revision);
        Assert.Collection(graph.Activities,
            x => Assert.Equal("WriteLine1", x.Id),
            x => Assert.Equal("WriteLine2", x.Id));
        var connection = Assert.Single(graph.Connections);
        Assert.Equal("WriteLine1", connection.Source);
        Assert.Equal("WriteLine2", connection.Target);
        Assert.Equal("Done", connection.SourcePort);
    }

    [Fact]
    public void Graph_reader_is_empty_when_no_workflow_definition_attachment_is_present()
    {
        var graph = WorkflowAgentGraphReader.Read([]);

        Assert.Null(graph.Revision);
        Assert.Empty(graph.Activities);
        Assert.Empty(graph.Connections);
    }

    [Fact]
    public async Task Context_provider_hydrates_activities_and_revision_from_supplied_graph()
    {
        using var provider = BuildProvider();
        var contextProvider = provider.GetRequiredService<IWorkflowAgentContextProvider>();

        var context = await contextProvider.GetContextAsync(new(
            "session-1",
            "wf-1",
            "draft",
            Prompt: "Add a third write line.",
            Graph: WorkflowAgentGraphReader.Read([CreateGraphAttachment()])));

        Assert.Equal("rev-7", context.Revision); // from the graph, not the "unknown" seam
        Assert.Equal(2, context.Activities.Count);
        Assert.Contains(context.Activities, x => x.Id == "WriteLine2");
    }

    [Fact]
    public void Classifier_validates_connect_against_node_ids_from_supplied_graph()
    {
        using var provider = BuildProvider();
        var classifier = provider.GetRequiredService<IWorkflowGraphOperationBatchRiskClassifier>();
        var context = ContextFromGraph(WorkflowAgentGraphReader.Read([CreateGraphAttachment()]));

        // Add a third Write Line and connect it after the existing second one — the reported scenario.
        var batch = new WorkflowGraphOperationBatch(
            WorkflowGraphOperationBatchSchema.CurrentVersion,
            "wf-1",
            "rev-7",
            [
                new("op-add", WorkflowGraphOperationKind.AddActivity, new Dictionary<string, object?>
                {
                    ["activityId"] = "temp:WriteLine3",
                    ["activityType"] = "Elsa.Workflows.WriteLine"
                }, ["temp:WriteLine3"], "Add a third write line."),
                new("op-connect", WorkflowGraphOperationKind.ConnectActivities, new Dictionary<string, object?>
                {
                    ["sourceActivityId"] = "WriteLine2",
                    ["targetActivityId"] = "temp:WriteLine3"
                }, [], "Connect the second write line to the third.")
            ],
            new Dictionary<string, string> { ["surface"] = "designer" });

        var result = classifier.Classify(new(batch, context));

        // The connect op references WriteLine2 (a real node from the graph) and the freshly added temp node,
        // so it is not flagged as an invalid/unknown reference.
        Assert.DoesNotContain(WorkflowGraphOperationBatchRiskReason.InvalidBatch, result.Reasons);
        Assert.DoesNotContain(WorkflowGraphOperationBatchRiskReason.UnavailableActivity, result.Reasons);
    }

    [Fact]
    public async Task Read_graph_tool_returns_graph_from_context_attachment()
    {
        using var provider = BuildProvider();
        var tool = provider.GetRequiredService<IEnumerable<IAgentTool>>().OfType<WorkflowReadGraphTool>().Single();

        var result = await tool.InvokeAsync(new(
            "call-1", WorkflowReadGraphTool.ToolName, "session-1", "actor-1",
            new Dictionary<string, object?>(), [CreateGraphAttachment()]));

        Assert.True(result.Value?.Succeeded);
        var graph = Assert.IsType<WorkflowAgentGraph>(result.Value!.Payload);
        Assert.Equal(2, graph.Activities.Count);
        Assert.Equal("rev-7", graph.Revision);
    }

    [Fact]
    public async Task Apply_graph_operations_tool_creates_a_reviewable_proposal()
    {
        using var provider = BuildProvider(allowChanges: true);
        var tool = provider.GetRequiredService<IEnumerable<IAgentTool>>().OfType<WorkflowApplyGraphOperationsTool>().Single();
        var batch = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = WorkflowGraphOperationBatchSchema.CurrentVersion,
            workflowDefinitionId = "wf-1",
            baseRevision = "rev-7",
            operations = new[]
            {
                new
                {
                    id = "op-add",
                    kind = "AddActivity",
                    parameters = new Dictionary<string, object?> { ["activityId"] = "temp:WriteLine3", ["activityType"] = "Elsa.Workflows.WriteLine" },
                    temporaryReferences = new[] { "temp:WriteLine3" },
                    summary = "Add a third write line."
                }
            },
            metadata = new Dictionary<string, string> { ["surface"] = "designer" }
        });

        var result = await tool.InvokeAsync(new(
            "call-1", WorkflowApplyGraphOperationsTool.ToolName, "session-1", "actor-1",
            new Dictionary<string, object?> { ["batch"] = batch }, [CreateGraphAttachment()]));

        Assert.True(result.Value?.Succeeded);
        var proposal = Assert.IsType<AgentActionProposal>(result.Value!.Payload);
        Assert.True(proposal.RequiresApproval);
        Assert.Equal("wf-1", proposal.ResourceId);
    }

    [Fact]
    public async Task Apply_graph_operations_tool_rejects_a_missing_batch_argument()
    {
        using var provider = BuildProvider(allowChanges: true);
        var tool = provider.GetRequiredService<IEnumerable<IAgentTool>>().OfType<WorkflowApplyGraphOperationsTool>().Single();

        var result = await tool.InvokeAsync(new(
            "call-1", WorkflowApplyGraphOperationsTool.ToolName, "session-1", "actor-1",
            new Dictionary<string, object?>(), [CreateGraphAttachment()]));

        Assert.False(result.Succeeded);
        Assert.Equal("agent.workflow.invalid_batch", result.Error?.Code);
    }

    private static AgentContextAttachment CreateGraphAttachment()
        => new(
            "ctx-1",
            "workflow.definition",
            "wf-1",
            "Workflow",
            "workflow.definition",
            AgentContextSensitivity.Internal,
            "selection",
            "Live workflow graph.",
            JsonSerializer.Deserialize<JsonElement>(GraphJson),
            new Dictionary<string, string> { ["workflowDefinitionId"] = "wf-1", ["revision"] = "rev-7" });

    private static ServiceProvider BuildProvider(bool allowChanges = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowChangePermissionEvaluator>(new AllowWorkflowChangePermissionEvaluator(allowChanges));
        services.AddFoundationAgentAbstractions();
        services.AddFoundationWorkflowsAgent();
        return services.BuildServiceProvider();
    }

    private static WorkflowAgentContext ContextFromGraph(WorkflowAgentGraph graph)
        => new(
            "wf-1",
            "draft",
            graph.Revision ?? "unknown",
            "Draft workflow wf-1",
            graph.Activities,
            [],
            [],
            new(null, null, "studio-hint"),
            new(12, Enum.GetValues<WorkflowGraphOperationKind>()),
            new(true, true, ["workflow.propose-change"]),
            [new("Elsa.Workflows.WriteLine", "Write line", true, ["write"])]);

    private sealed class AllowWorkflowChangePermissionEvaluator(bool allowed) : IWorkflowChangePermissionEvaluator
    {
        public Task<bool> CanProposeChangeAsync(string actorId, string workflowDefinitionId, CancellationToken cancellationToken = default)
            => Task.FromResult(allowed);
    }
}
