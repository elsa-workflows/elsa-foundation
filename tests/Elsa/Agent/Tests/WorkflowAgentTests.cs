using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Extensions;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Contracts;
using Elsa.Agent.Workflows.Extensions;
using Elsa.Agent.Workflows.Models;
using Elsa.Agent.Workflows.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class WorkflowAgentTests
{
    [Fact]
    public void Workflow_graph_operation_vocabulary_covers_designer_authoring_edits()
    {
        var operationKinds = Enum.GetValues<WorkflowGraphOperationKind>();

        Assert.Equal(
            [
                WorkflowGraphOperationKind.AddActivity,
                WorkflowGraphOperationKind.UpdateActivity,
                WorkflowGraphOperationKind.RemoveActivity,
                WorkflowGraphOperationKind.ConnectActivities,
                WorkflowGraphOperationKind.DisconnectActivities,
                WorkflowGraphOperationKind.SetRoot,
                WorkflowGraphOperationKind.SetDesignerPosition,
                WorkflowGraphOperationKind.SetActivityProperty
            ],
            operationKinds);
    }

    [Fact]
    public void Workflow_graph_operation_batch_carries_schema_version_and_temporary_references()
    {
        var batch = CreateGraphOperationBatch();

        Assert.Equal(WorkflowGraphOperationBatchSchema.CurrentVersion, batch.SchemaVersion);
        Assert.Equal("wf-1", batch.WorkflowDefinitionId);
        Assert.Equal("rev-1", batch.BaseRevision);
        Assert.Equal("designer", batch.Metadata["surface"]);

        var addActivity = Assert.Single(batch.Operations, x => x.Kind == WorkflowGraphOperationKind.AddActivity);
        Assert.Equal("temp:activity:email-1", addActivity.Parameters["activityId"]);
        Assert.Contains("temp:activity:email-1", addActivity.TemporaryReferences);

        var setDesignerPosition = Assert.Single(batch.Operations, x => x.Kind == WorkflowGraphOperationKind.SetDesignerPosition);
        Assert.Equal(320, setDesignerPosition.Parameters["x"]);
        Assert.Equal(180, setDesignerPosition.Parameters["y"]);
    }

    [Fact]
    public void Agent_stream_event_can_transport_workflow_graph_operation_batch_as_typed_result()
    {
        var batch = CreateGraphOperationBatch();
        var streamEvent = new AgentStreamEvent(
            Guid.NewGuid().ToString("N"),
            AgentStreamEventKind.WorkflowGraphOperationBatchCreated,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            AgentResultKind.WorkflowGraphOperationBatch,
            batch);

        Assert.Equal(AgentStreamEventKind.WorkflowGraphOperationBatchCreated, streamEvent.Kind);
        Assert.Equal(AgentResultKind.WorkflowGraphOperationBatch, streamEvent.ResultKind);
        var payload = Assert.IsType<WorkflowGraphOperationBatch>(streamEvent.Payload);
        Assert.Same(batch, payload);
    }

    [Fact]
    public async Task Deterministic_workflow_provider_streams_one_workflow_graph_operation_batch()
    {
        var provider = new DeterministicWorkflowAgentProvider();

        var events = await CollectAsync(provider.SendMessageAsync(new(
            "session-1",
            "Create workflow graph operation batch to add activity.",
            [CreateWorkflowContext()])));

        var batchEvent = Assert.Single(events, x => x.ResultKind == AgentResultKind.WorkflowGraphOperationBatch);
        Assert.Equal(AgentStreamEventKind.WorkflowGraphOperationBatchCreated, batchEvent.Kind);

        var batch = Assert.IsType<WorkflowGraphOperationBatch>(batchEvent.Payload);
        Assert.Equal(WorkflowGraphOperationBatchSchema.CurrentVersion, batch.SchemaVersion);
        Assert.Equal("wf-1", batch.WorkflowDefinitionId);
        Assert.Equal("rev-1", batch.BaseRevision);
        Assert.Contains(batch.Operations, x => x.Kind == WorkflowGraphOperationKind.AddActivity);
        Assert.Contains(batch.Operations, x => x.Kind == WorkflowGraphOperationKind.SetRoot);
        Assert.Contains(batch.Operations, x => x.Kind == WorkflowGraphOperationKind.SetDesignerPosition);
        Assert.Contains(batch.Operations, x => x.Kind == WorkflowGraphOperationKind.SetActivityProperty);
        Assert.Equal(AgentStreamEventKind.Completed, events.Last().Kind);
    }

    [Fact]
    public async Task Deterministic_workflow_provider_can_return_message_or_error_results()
    {
        var provider = new DeterministicWorkflowAgentProvider();

        var messageEvents = await CollectAsync(provider.SendMessageAsync(new("session-1", "Explain this workflow.", [])));
        var messageDelta = Assert.Single(messageEvents, x => x.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Equal(AgentResultKind.Message, messageDelta.ResultKind);
        Assert.DoesNotContain(messageEvents, x => x.ResultKind == AgentResultKind.WorkflowGraphOperationBatch);

        var errorEvents = await CollectAsync(provider.SendMessageAsync(new("session-1", "Force error.", [])));
        var error = Assert.Single(errorEvents, x => x.Kind == AgentStreamEventKind.Error);
        Assert.Equal(AgentResultKind.Error, error.ResultKind);
        Assert.Equal("agent.workflow.deterministic_error", error.Error?.Code);
        Assert.DoesNotContain(errorEvents, x => x.ResultKind == AgentResultKind.WorkflowGraphOperationBatch);
    }

    [Fact]
    public async Task Streaming_service_preserves_deterministic_workflow_graph_operation_batch_payload()
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: true);
        var sessions = provider.GetRequiredService<IAgentSessionService>();
        var streaming = provider.GetRequiredService<IAgentStreamingService>();
        var session = await sessions.CreateAsync(new(
            "tenant-1",
            "actor-1",
            "conversation-1",
            DeterministicWorkflowAgentProvider.Id,
            "workflow-authoring",
            "Workflow authoring",
            AgentPolicy.Default,
            new Dictionary<string, string>()));
        await sessions.AddMessageAsync(session.Id, new(
            AgentRole.User,
            "Direct apply a workflow graph operation batch that adds an email activity.",
            AgentMessageStatus.Pending,
            "workflow.propose-change",
            ["ctx-1"],
            [CreateWorkflowContext()]));

        var events = await CollectAsync(streaming.StreamAsync(session.Id));

        var batchEvent = Assert.Single(events, x => x.ResultKind == AgentResultKind.WorkflowGraphOperationBatch);
        Assert.Equal(AgentStreamEventKind.WorkflowGraphOperationBatchCreated, batchEvent.Kind);
        var batch = Assert.IsType<WorkflowGraphOperationBatch>(batchEvent.Payload);
        Assert.Equal("wf-1", batch.WorkflowDefinitionId);
        Assert.Equal("rev-1", batch.BaseRevision);
        Assert.Single(events.Where(x => x.Kind == AgentStreamEventKind.WorkflowGraphOperationBatchCreated));
    }

    [Fact]
    public async Task Workflow_context_provider_returns_minimized_workflow_attachment_shape()
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: true);
        var contextProvider = provider.GetServices<IAgentContextProvider>().Single(x => x.ScopeKind == "workflow");

        var attachments = await contextProvider.CollectAsync(new(
            "session-1",
            "workflow",
            new Dictionary<string, string>
            {
                ["workflowDefinitionId"] = "wf-1",
                ["workflowVersionId"] = "v1"
            }));

        var attachment = Assert.Single(attachments);
        Assert.Equal("workflow.definition", attachment.Source);
        Assert.Equal("workflow.definition", attachment.ContentType);
        Assert.Equal(AgentContextSensitivity.Internal, attachment.Sensitivity);
        Assert.Equal("wf-1", attachment.References["workflowDefinitionId"]);
        Assert.Equal("rev-1", attachment.References["revision"]);
        Assert.Contains("Redactions:", attachment.Summary);
        Assert.NotNull(attachment.Content);
    }

    [Fact]
    public async Task Workflow_change_proposal_rejects_stale_base_revision()
    {
        using var provider = BuildWorkflowProvider("rev-2", allowChanges: true);
        var service = provider.GetRequiredService<IWorkflowChangeProposalService>();

        var result = await service.ProposeAsync(CreateRequest(baseRevision: "rev-1"));

        Assert.False(result.Succeeded);
        Assert.Equal("agent.workflow.revision_conflict", result.Error?.Code);
    }

    [Fact]
    public async Task Workflow_change_proposal_requires_permission()
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: false);
        var service = provider.GetRequiredService<IWorkflowChangeProposalService>();

        var result = await service.ProposeAsync(CreateRequest(baseRevision: "rev-1"));

        Assert.False(result.Succeeded);
        Assert.Equal("agent.workflow.permission_denied", result.Error?.Code);
    }

    [Fact]
    public async Task Workflow_change_proposal_allows_default_unknown_revision_seam()
    {
        using var provider = BuildWorkflowProvider("unknown", allowChanges: true);
        var service = provider.GetRequiredService<IWorkflowChangeProposalService>();

        var result = await service.ProposeAsync(CreateRequest(baseRevision: "rev-1"));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Workflow_change_proposal_is_reviewable_and_requires_approval()
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: true);
        var service = provider.GetRequiredService<IWorkflowChangeProposalService>();

        var result = await service.ProposeAsync(CreateRequest(baseRevision: "rev-1"));

        Assert.True(result.Succeeded);
        Assert.True(result.Value?.RequiresApproval);
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, result.Value?.Status);
        Assert.Equal("workflow-change", result.Value?.Kind);
        Assert.Equal("workflow.propose-change", result.Value?.CapabilityId);
        Assert.Equal("workflow-definition", result.Value?.ResourceType);
        Assert.Equal("wf-1", result.Value?.ResourceId);
    }

    private static ServiceProvider BuildWorkflowProvider(string revision, bool allowChanges)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowRevisionProvider>(new FixedWorkflowRevisionProvider(revision));
        services.AddSingleton<IWorkflowChangePermissionEvaluator>(new FixedWorkflowChangePermissionEvaluator(allowChanges));
        services.AddFoundationAgentAbstractions();
        services.AddFoundationWorkflowsAgent();
        return services.BuildServiceProvider();
    }

    private static WorkflowChangeProposalRequest CreateRequest(string baseRevision)
        => new(
            "session-1",
            "actor-1",
            "wf-1",
            baseRevision,
            "Update workflow",
            "Reviewable workflow update.",
            [new Dictionary<string, object?> { ["op"] = "replace-input", ["path"] = "workflow:wf-1" }],
            ["Changes draft workflow behavior."],
            "Restore the previous draft revision.");

    private static AgentContextAttachment CreateWorkflowContext()
        => new(
            "ctx-1",
            "workflow.definition",
            "wf-1",
            "Workflow",
            "workflow.definition",
            AgentContextSensitivity.Internal,
            "selection",
            "Workflow definition 'wf-1' at revision 'rev-1'.",
            null,
            new Dictionary<string, string>
            {
                ["workflowDefinitionId"] = "wf-1",
                ["revision"] = "rev-1"
            });

    private static async Task<List<AgentStreamEvent>> CollectAsync(IAsyncEnumerable<AgentStreamEvent> events)
    {
        var result = new List<AgentStreamEvent>();
        await foreach (var item in events)
            result.Add(item);

        return result;
    }

    private static WorkflowGraphOperationBatch CreateGraphOperationBatch()
        => new(
            WorkflowGraphOperationBatchSchema.CurrentVersion,
            "wf-1",
            "rev-1",
            [
                new(
                    "op-add-email",
                    WorkflowGraphOperationKind.AddActivity,
                    new Dictionary<string, object?>
                    {
                        ["activityId"] = "temp:activity:email-1",
                        ["activityType"] = "Elsa.Email.SendEmail"
                    },
                    ["temp:activity:email-1"],
                    "Add email activity."),
                new(
                    "op-connect-root",
                    WorkflowGraphOperationKind.ConnectActivities,
                    new Dictionary<string, object?>
                    {
                        ["sourceActivityId"] = "root",
                        ["targetActivityId"] = "temp:activity:email-1"
                    },
                    ["temp:activity:email-1"],
                    "Connect root to email activity."),
                new(
                    "op-position-email",
                    WorkflowGraphOperationKind.SetDesignerPosition,
                    new Dictionary<string, object?>
                    {
                        ["activityId"] = "temp:activity:email-1",
                        ["x"] = 320,
                        ["y"] = 180
                    },
                    ["temp:activity:email-1"],
                    "Place email activity on the designer canvas.")
            ],
            new Dictionary<string, string>
            {
                ["surface"] = "designer"
            });

    private sealed class FixedWorkflowRevisionProvider(string revision) : IWorkflowRevisionProvider
    {
        public Task<string> GetCurrentRevisionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => Task.FromResult(revision);
    }

    private sealed class FixedWorkflowChangePermissionEvaluator(bool allowed) : IWorkflowChangePermissionEvaluator
    {
        public Task<bool> CanProposeChangeAsync(string actorId, string workflowDefinitionId, CancellationToken cancellationToken = default) => Task.FromResult(allowed);
    }
}
