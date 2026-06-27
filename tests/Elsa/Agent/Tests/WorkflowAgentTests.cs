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
    public async Task Deterministic_workflow_provider_can_return_structured_clarification_results()
    {
        var provider = new DeterministicWorkflowAgentProvider();

        var events = await CollectAsync(provider.SendMessageAsync(new(
            "session-1",
            "This workflow request is ambiguous; ask a question.",
            [CreateWorkflowContext()])));

        var clarificationEvent = Assert.Single(events, x => x.ResultKind == AgentResultKind.Clarification);
        Assert.Equal(AgentStreamEventKind.ClarificationRequested, clarificationEvent.Kind);
        var clarification = Assert.IsType<WorkflowClarificationResult>(clarificationEvent.Payload);
        Assert.Equal("Which workflow branch should Weaver update?", clarification.Question);
        Assert.Equal("session-1:clarification:workflow-target", clarification.ContinuationToken);
        Assert.Equal("wf-1", clarification.Metadata["workflowDefinitionId"]);
        Assert.Contains(clarification.Options, x => x.Id == "active-draft" && x.Value == "active-draft");
        Assert.Equal(AgentStreamEventKind.Completed, events.Last().Kind);
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
    public async Task Streaming_service_continues_same_session_after_structured_clarification()
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
            "Ambiguous workflow request; clarify first.",
            AgentMessageStatus.Pending,
            "workflow.propose-change",
            ["ctx-1"],
            [CreateWorkflowContext()]));

        var clarificationEvents = await CollectAsync(streaming.StreamAsync(session.Id));
        var clarification = Assert.IsType<WorkflowClarificationResult>(Assert.Single(clarificationEvents, x => x.ResultKind == AgentResultKind.Clarification).Payload);
        Assert.StartsWith(session.Id, clarification.ContinuationToken, StringComparison.Ordinal);

        await sessions.AddMessageAsync(session.Id, new(
            AgentRole.User,
            "Use the active draft and create workflow graph operation batch.",
            AgentMessageStatus.Pending,
            "workflow.propose-change",
            ["ctx-1"],
            [CreateWorkflowContext()]));

        var followUpEvents = await CollectAsync(streaming.StreamAsync(session.Id));

        Assert.Contains(followUpEvents, x => x.ResultKind == AgentResultKind.WorkflowGraphOperationBatch);
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
        Assert.Contains("catalog items", attachment.Summary);
        Assert.NotNull(attachment.Content);
    }

    [Fact]
    public async Task Workflow_context_provider_selects_bounded_activity_catalog_subset_from_prompt_and_selection_hint()
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: true);
        var contextProvider = provider.GetRequiredService<IWorkflowAgentContextProvider>();

        var context = await contextProvider.GetContextAsync(new(
            "session-1",
            "wf-1",
            "draft",
            "Send an email notification.",
            "node-1",
            "Elsa.Email.SendEmail"));

        Assert.Equal("wf-1", context.WorkflowDefinitionId);
        Assert.Equal("rev-1", context.Revision);
        Assert.Equal("Draft workflow wf-1", context.Summary);
        Assert.Equal("node-1", context.Selection.NodeId);
        Assert.Equal("Elsa.Email.SendEmail", context.Selection.ActivityType);
        Assert.Equal("studio-hint", context.Selection.Source);
        Assert.True(context.ActivityCatalog.Count <= context.DesignerConstraints.MaxActivityCatalogItems);

        var item = Assert.Single(context.ActivityCatalog);
        Assert.Equal("Elsa.Email.SendEmail", item.Type);
        Assert.True(item.IsAvailable);
        Assert.Contains(WorkflowGraphOperationKind.SetActivityProperty, context.DesignerConstraints.SupportedOperations);
        Assert.True(context.Permissions.CanProposeChange);
        Assert.False(context.Permissions.CanDirectApply);
        Assert.Contains("workflow.propose-change", context.Permissions.Capabilities);
    }

    [Fact]
    public async Task Workflow_context_provider_excludes_unavailable_catalog_items_even_when_studio_hints_them()
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: true);
        var contextProvider = provider.GetRequiredService<IWorkflowAgentContextProvider>();

        var context = await contextProvider.GetContextAsync(new(
            "session-1",
            "wf-1",
            "draft",
            "Use the secret credential activity.",
            "node-secret",
            "Elsa.Secrets.LegacySecretActivity"));

        Assert.Equal("Elsa.Secrets.LegacySecretActivity", context.Selection.ActivityType);
        Assert.DoesNotContain(context.ActivityCatalog, x => x.Type == "Elsa.Secrets.LegacySecretActivity");
        Assert.Empty(context.ActivityCatalog);
        Assert.Contains(context.Diagnostics, x => x.Severity == "Warning" && x.Message.Contains("No available Activity Catalog items", StringComparison.Ordinal));
        Assert.Contains(context.Redactions, x => x.Contains("Runtime payloads", StringComparison.Ordinal));
        Assert.Contains(context.Redactions, x => x.Contains("Unavailable activities", StringComparison.Ordinal));
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

    [Fact]
    public void Workflow_batch_risk_classifier_allows_low_risk_direct_apply()
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: true);
        var classifier = provider.GetRequiredService<IWorkflowGraphOperationBatchRiskClassifier>();
        var context = CreateWorkflowAgentContext(canDirectApply: true, revision: "rev-1");

        var result = classifier.Classify(new(CreateGraphOperationBatch(), context));

        Assert.True(result.CanDirectApply);
        Assert.Equal(WorkflowGraphOperationBatchRiskDecision.DirectApply, result.Decision);
        Assert.Equal(AgentRisk.ReadOnly, result.Risk);
        Assert.Equal(AgentResultKind.WorkflowGraphOperationBatch, result.ResultKind);
        Assert.Equal([WorkflowGraphOperationBatchRiskReason.LowRisk], result.Reasons);
    }

    [Fact]
    public void Workflow_batch_risk_classifier_fails_closed_for_stale_destructive_or_invalid_batches()
    {
        using var provider = BuildWorkflowProvider("rev-2", allowChanges: true);
        var classifier = provider.GetRequiredService<IWorkflowGraphOperationBatchRiskClassifier>();
        var context = CreateWorkflowAgentContext(canDirectApply: true, revision: "rev-2");
        var batch = CreateGraphOperationBatch(
            [
                new(
                    "",
                    WorkflowGraphOperationKind.RemoveActivity,
                    new Dictionary<string, object?> { ["activityId"] = "root" },
                    [],
                    "Remove an existing activity.")
            ]);

        var result = classifier.Classify(new(batch, context));

        Assert.False(result.CanDirectApply);
        Assert.Equal(WorkflowGraphOperationBatchRiskDecision.Proposal, result.Decision);
        Assert.Equal(AgentRisk.ReviewRequired, result.Risk);
        Assert.Equal(AgentResultKind.Proposal, result.ResultKind);
        Assert.Contains(WorkflowGraphOperationBatchRiskReason.StaleRevision, result.Reasons);
        Assert.Contains(WorkflowGraphOperationBatchRiskReason.DestructiveOperation, result.Reasons);
        Assert.Contains(WorkflowGraphOperationBatchRiskReason.InvalidBatch, result.Reasons);
        Assert.Contains(WorkflowGraphOperationBatchRiskReason.Uncertain, result.Reasons);
    }

    [Fact]
    public void Workflow_batch_risk_classifier_asks_for_clarification_when_activity_is_unavailable()
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: true);
        var classifier = provider.GetRequiredService<IWorkflowGraphOperationBatchRiskClassifier>();
        var context = CreateWorkflowAgentContext(canDirectApply: true, revision: "rev-1");
        var batch = CreateGraphOperationBatch(
            [
                new(
                    "op-add-missing",
                    WorkflowGraphOperationKind.AddActivity,
                    new Dictionary<string, object?>
                    {
                        ["activityId"] = "temp:activity:missing",
                        ["activityType"] = "Elsa.Missing.Activity"
                    },
                    ["temp:activity:missing"],
                    "Add an activity that is not available.")
            ]);

        var result = classifier.Classify(new(batch, context));

        Assert.False(result.CanDirectApply);
        Assert.Equal(WorkflowGraphOperationBatchRiskDecision.Clarification, result.Decision);
        Assert.Equal(AgentResultKind.Clarification, result.ResultKind);
        Assert.Contains(WorkflowGraphOperationBatchRiskReason.UnavailableActivity, result.Reasons);
    }

    [Theory]
    [InlineData("direct-apply-succeeded", AgentResultKind.WorkflowGraphOperationBatch)]
    [InlineData("direct-apply-rejected", AgentResultKind.Proposal)]
    [InlineData("provider-error", AgentResultKind.Error)]
    public async Task Workflow_authoring_audit_records_interaction_outcomes_with_attribution(string outcome, AgentResultKind resultKind)
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: true);
        var audit = provider.GetRequiredService<IWorkflowAuthoringAuditService>();
        var auditReader = provider.GetRequiredService<IAgentAuditReader>();

        await audit.EmitAsync(CreateAuditRequest(outcome, resultKind));

        var auditEvent = Assert.Single(await auditReader.ListAsync("session-1"), x => x.Kind == AgentAuditEventKind.WorkflowAuthoringInteraction);
        Assert.Equal("actor-1", auditEvent.ActorId);
        Assert.Equal("wf-1", auditEvent.Metadata["workflowDefinitionId"]);
        Assert.Equal("workflow.propose-change", auditEvent.Metadata["capabilityId"]);
        Assert.Equal(DeterministicWorkflowAgentProvider.Id, auditEvent.Metadata["providerId"]);
        Assert.Equal("Prepared one workflow graph operation batch.", auditEvent.Metadata["operationSummary"]);
        Assert.Equal(outcome, auditEvent.Metadata["outcome"]);
        Assert.Equal(resultKind.ToString(), auditEvent.Metadata["resultKind"]);
        Assert.Equal("deterministic-model", auditEvent.Metadata["modelId"]);
        Assert.Equal("run-1", auditEvent.Metadata["runId"]);
        Assert.Equal("false", auditEvent.Metadata["persistedWorkflowRevisionChanged"]);
        Assert.Equal(string.Empty, auditEvent.Metadata["workflowProvenanceState"]);
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

    private static WorkflowAgentContext CreateWorkflowAgentContext(bool canDirectApply, string revision)
        => new(
            "wf-1",
            "draft",
            revision,
            "Draft workflow wf-1",
            [new("root", "Elsa.Workflows.WriteLine", "Write line")],
            [],
            [],
            new(null, null, "studio-hint"),
            new(12, Enum.GetValues<WorkflowGraphOperationKind>()),
            new(canDirectApply, true, ["workflow.propose-change"]),
            [
                new("Elsa.Email.SendEmail", "Send email", true, ["email"]),
                new("Elsa.Workflows.WriteLine", "Write line", true, ["write"])
            ]);

    private static WorkflowAuthoringAuditRequest CreateAuditRequest(string outcome, AgentResultKind resultKind)
        => new(
            "session-1",
            "actor-1",
            "wf-1",
            "workflow.propose-change",
            DeterministicWorkflowAgentProvider.Id,
            "Prepared one workflow graph operation batch.",
            outcome,
            resultKind,
            "deterministic-model",
            "run-1",
            new Dictionary<string, string>
            {
                ["persistedWorkflowRevisionChanged"] = "false",
                ["workflowProvenanceState"] = string.Empty
            });

    private static async Task<List<AgentStreamEvent>> CollectAsync(IAsyncEnumerable<AgentStreamEvent> events)
    {
        var result = new List<AgentStreamEvent>();
        await foreach (var item in events)
            result.Add(item);

        return result;
    }

    private static WorkflowGraphOperationBatch CreateGraphOperationBatch(IReadOnlyCollection<WorkflowGraphOperation>? operations = null)
        => new(
            WorkflowGraphOperationBatchSchema.CurrentVersion,
            "wf-1",
            "rev-1",
            operations ??
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
