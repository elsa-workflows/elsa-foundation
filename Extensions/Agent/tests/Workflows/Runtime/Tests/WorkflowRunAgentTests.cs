using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Runtime.Services;
using Elsa.Agent.Workflows.Runtime.Tools;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Elsa.Agent.Workflows.Runtime.Tests.WorkflowRuntimeAgentTestHost;

namespace Elsa.Agent.Workflows.Runtime.Tests;

// Spec 1193 slice G (story 9, FR-005, FR-027, FR-029): one exact Run and its selected activity execution, read-only, with
// execution-evidence redaction kept and unavailable data reported by reason.
public sealed class WorkflowRunAgentTests
{
    [Fact]
    public void The_feature_registers_the_internal_sealed_run_contributions_no_fallback_owner_and_no_mutating_tool()
    {
        var services = new ServiceCollection();
        new FoundationWorkflowsRuntimeAgentFeature().ConfigureServices(services);
        foreach (var (contract, implementation) in new[]
                 {
                     (typeof(IAgentContextProvider), typeof(WorkflowRunContextProvider)), (typeof(IAgentTool), typeof(WorkflowReadRunTool))
                 })
        {
            var descriptor = Assert.Single(services, item => item.ServiceType == contract && item.ImplementationType == implementation);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            Assert.True(implementation.IsSealed);
            Assert.False(implementation.IsPublic);
        }
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IWorkflowRunInspection));
        // Retry, cancel, delete and dispatch are the run page's own controls: every tool this package registers is a read.
        using var host = new WorkflowRuntimeAgentTestHost();
        Assert.All(host.Services.GetServices<IAgentTool>().Where(tool => tool.Descriptor.ModuleId == ModuleId),
            tool => Assert.Equal((AgentToolMutability.ReadOnly, AgentRisk.ReadOnly), (tool.Descriptor.Mutability, tool.Descriptor.Risk)));
    }

    [Fact]
    public async Task A_run_reference_is_admitted_as_the_servers_read_bound_to_the_instance_and_the_selected_activity_execution()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();

        var contribution = await host.CollectRunAsync(session, RunReference());

        Assert.Empty(contribution.Diagnostics);
        var admitted = Assert.Single(contribution.Attachments, item => item.ContentType == "workflows.run");
        Assert.Equal(AgentContextOrigin.AuthoritativeRead, admitted.Origin);
        Assert.Equal(new AgentResourceTarget(ModuleId, "workflow-run", "run-1", "act-write"), admitted.Target);
        var content = (JsonElement)admitted.Content!;
        Assert.Equal(("run-1", "act-write", "Faulted", false), (content.GetProperty("workflowExecutionId").GetString(),
            content.GetProperty("activityExecutionId").GetString(), content.GetProperty("status").GetString(), content.GetProperty("inProgress").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, content.GetProperty("readAt").ValueKind);
        Assert.Equal(("artifact-1", Hash), (content.GetProperty("artifactId").GetString(), content.GetProperty("artifactHash").GetString()));
        var selected = content.GetProperty("selectedActivity");
        Assert.Equal(("act-write", "Elsa.Activities.Primitives.Activities.WriteLine", 4), (selected.GetProperty("activityExecutionId").GetString(),
            selected.GetProperty("activityType").GetString(), selected.GetProperty("valueCount").GetInt32()));
        // The context never resolves a value or carries an incident message: values are read only by workflow_read_run.
        var request = Assert.Single(host.Runs.Requests);
        Assert.Equal(("run-1", "act-write", (IReadOnlySet<string>?)null), (request.WorkflowExecutionId, request.ActivityExecutionId, request.ResolveEvidenceIds));
        Assert.DoesNotContain("Qualification fault", content.GetRawText());
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(contribution.Attachments));
        Assert.Single(contribution.Attachments, item => item.ContentType == "workflows.run-guidance" && item.Origin == AgentContextOrigin.InstalledGuidance);
    }

    [Fact]
    public async Task A_run_without_a_selection_is_bound_to_its_Workflow_Instance_identity()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var contribution = await host.CollectRunAsync(await host.SessionAsync(), RunReference(activityExecutionId: null));
        var admitted = Assert.Single(contribution.Attachments, item => item.ContentType == "workflows.run");
        Assert.Equal(new AgentResourceTarget(ModuleId, "workflow-run", "run-1", "run-1"), admitted.Target);
        var content = (JsonElement)admitted.Content!;
        Assert.Equal(JsonValueKind.Null, content.GetProperty("activityExecutionId").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.GetProperty("selectedActivity").ValueKind);
    }

    [Theory]
    [InlineData("unknown-run", "agent.workflow.run_not_found", "The workflow Run is unavailable.")]
    [InlineData("unknown-activity", "agent.workflow.run_activity_not_found", "The selected activity execution is not part of this workflow Run.")]
    [InlineData("denied", "agent.workflow.permission_denied", "Current workflow runtime read permission is required for the workflow Run.")]
    [InlineData("malformed", "agent.workflow.run_context_invalid", null)]
    [InlineData("unbound", "agent.workflow.run_context_invalid", null)]
    [InlineData("latest", "agent.workflow.run_context_invalid", null)]
    [InlineData("self-selected", "agent.workflow.run_context_invalid", null)]
    public async Task An_unknown_denied_or_malformed_run_reference_is_refused_with_nothing_disclosed(string control, string code, string? message)
    {
        using var host = new WorkflowRuntimeAgentTestHost(authorizer: control == "denied" ? new Authorizer("workflows/runtime:read") : null);
        var session = await host.SessionAsync();
        var reference = control switch
        {
            "unknown-run" => RunReference("run-missing"),
            "unknown-activity" => RunReference(activityExecutionId: "act-missing"),
            "malformed" => RunReference(content: new { workflowExecutionId = "run-1", activityExecutionId = "act-write", status = "Faulted" }),
            "unbound" => RunReference() with { Target = new(ModuleId, "workflow-run", "run-1", "act-fault") },
            // A reference names one exact Run; "the latest run" of a definition is never a Run identity.
            "latest" => RunReference(content: new { definitionId = "definition-1", latest = true }),
            "self-selected" => RunReference(activityExecutionId: "run-1"),
            _ => RunReference()
        };

        var contribution = await host.CollectRunAsync(session, reference);

        Assert.Empty(contribution.Attachments);
        var refusal = Assert.Single(contribution.Diagnostics);
        Assert.Equal((code, true, false), (refusal.Code, refusal.Required, refusal.OwnerUnavailable));
        if (message is not null) Assert.Equal(message, refusal.Message);
        // The host's runtime read permission is decided before anything is read, and a malformed reference reads nothing.
        if (control is "denied" or "malformed" or "unbound" or "latest" or "self-selected") Assert.Empty(host.Runs.Requests);
    }

    [Fact]
    public async Task A_missing_run_inspection_owner_is_unavailable_never_a_guess()
    {
        using var host = new WorkflowRuntimeAgentTestHost(configure: services => services.RemoveAll<IWorkflowRunInspection>());
        var session = await host.SessionAsync();
        var refusal = Assert.Single((await host.CollectRunAsync(session, RunReference())).Diagnostics);
        Assert.Equal(("agent.workflow.run_inspection_unavailable", true, true), (refusal.Code, refusal.Required, refusal.OwnerUnavailable));
        var decision = await ((IAgentToolAuthorization)host.RunTool).AuthorizeAsync(session.ActorId, session.TenantId, null);
        Assert.False(decision.Allowed);
        Assert.True(Assert.Single(decision.Violations).OwnerUnavailable);
    }

    [Fact]
    public async Task The_tool_returns_the_exact_Run_its_incidents_and_the_selected_activity_execution_with_redaction_and_reasons()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference())).Attachments;

        var prepared = await host.PrepareRunAsync(session, admitted);

        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        Assert.False(prepared.Value!.RequiresApproval);
        Assert.Equal(AgentRisk.ReadOnly, prepared.Value.Risk);
        Assert.Equal(new AgentResourceTarget(ModuleId, "workflow-run", "run-1", "act-write"), prepared.Value.Target);
        // The tool alone asks the owner to resolve values, through its audited payload read, and only the ones it discloses: a
        // first read resolves nothing and decides the bounds, the second resolves exactly the disclosed resolvable values.
        Assert.Null(host.Runs.Requests[^2].ResolveEvidenceIds);
        Assert.Equal(["evidence-result"], host.Runs.Requests[^1].ResolveEvidenceIds!);
        var data = prepared.Value.Preview.Data;
        Assert.Equal("workflows.run", prepared.Value.Preview.TypeId);
        Assert.True(data.GetProperty("readOnly").GetBoolean());
        Assert.False(data.GetProperty("inProgress").GetBoolean());
        Assert.Contains("has finished (Faulted)", data.GetProperty("attribution").GetString());
        Assert.Equal(("run-1", "artifact-1", Hash), (data.GetProperty("instance").GetProperty("workflowExecutionId").GetString(),
            data.GetProperty("instance").GetProperty("artifactId").GetString(), data.GetProperty("instance").GetProperty("artifactHash").GetString()));
        var incident = Assert.Single(data.GetProperty("incidents").EnumerateArray());
        Assert.Equal(("incident-1", "act-fault", "Qualification fault.", true), (incident.GetProperty("incidentId").GetString(),
            incident.GetProperty("activityExecutionId").GetString(), incident.GetProperty("message").GetString(), incident.GetProperty("isBlocking").GetBoolean()));
        Assert.Equal(["act-write", "act-fault"], data.GetProperty("activities").GetProperty("items").EnumerateArray().Select(item => item.GetProperty("activityExecutionId").GetString()));

        var values = data.GetProperty("selectedActivity").GetProperty("values").EnumerateArray().ToDictionary(item => item.GetProperty("name").GetString()!);
        Assert.Equal(("withheld", true, JsonValueKind.Null), (values["text"].GetProperty("disclosure").GetString(), values["text"].GetProperty("sensitive").GetBoolean(),
            values["text"].GetProperty("value").ValueKind));
        Assert.Equal(("resolved", true), (values["result"].GetProperty("disclosure").GetString(), values["result"].GetProperty("value").GetProperty("ok").GetBoolean()));
        Assert.Equal(("unavailable", "notPermitted"), (values["limited"].GetProperty("disclosure").GetString(), values["limited"].GetProperty("unavailableReason").GetString()));
        Assert.Equal(("unavailable", "notCaptured"), (values["skipped"].GetProperty("disclosure").GetString(), values["skipped"].GetProperty("unavailableReason").GetString()));
        var state = data.GetProperty("selectedActivity").GetProperty("stateView");
        Assert.Equal((false, "unsupported"), (state.GetProperty("available").GetBoolean(), state.GetProperty("unavailableReason").GetString()));
        // What is unavailable is listed by reason, in product terms; nothing is filled in.
        var unavailable = data.GetProperty("unavailable").EnumerateArray().Select(item => (item.GetProperty("data").GetString(), item.GetProperty("reason").GetString())).ToArray();
        Assert.Contains(("ActivityInput 'text'", "withheld"), unavailable);
        Assert.Contains(("ActivityInput 'limited'", "notPermitted"), unavailable);
        Assert.Contains(("ActivityInput 'skipped'", "notCaptured"), unavailable);
        Assert.Contains(("selected activity execution state view", "unsupported"), unavailable);
        Assert.Contains("run page's own controls", data.GetProperty("runControls").GetString());
        // A withheld value never leaves, even when an owner handed one over beside its marker.
        Assert.DoesNotContain(Canary, data.GetRawText());

        var executed = await host.RunTool.ExecuteAsync(Prepared(session, prepared.Value));
        Assert.True(executed.Succeeded);
        Assert.Null(executed.Value!.Receipt);
        Assert.Equal(data.GetRawText(), executed.Value.Payload!.Data.GetRawText());
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(executed.Value));
    }

    [Fact]
    public async Task A_pruned_selection_is_admitted_and_reported_as_pruned_never_inferred()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference(activityExecutionId: "act-pruned"))).Attachments;
        var content = (JsonElement)Assert.Single(admitted, item => item.ContentType == "workflows.run").Content!;
        Assert.Equal("pruned", content.GetProperty("selectedActivity").GetProperty("unavailable").GetString());

        var data = (await host.PrepareRunAsync(session, admitted)).Value!.Preview.Data;
        var selected = data.GetProperty("selectedActivity");
        Assert.Equal(("act-pruned", "pruned"), (selected.GetProperty("activityExecutionId").GetString(), selected.GetProperty("unavailable").GetString()));
        Assert.Contains("no longer retained", selected.GetProperty("explanation").GetString());
        Assert.Contains(data.GetProperty("unavailable").EnumerateArray(), item => item.GetProperty("reason").GetString() == "pruned");
        Assert.False(selected.TryGetProperty("values", out _));
    }

    [Fact]
    public async Task An_unsupported_activity_execution_inspection_is_reported_as_unsupported()
    {
        // A backend without activity execution inspection: the Runtime read reports the selection unsupported.
        using var host = new WorkflowRuntimeAgentTestHost();
        host.Runs.Read = request => Run(request with { ActivityExecutionId = null }) with
        {
            Selection = new(request.ActivityExecutionId!, null, WorkflowRunUnavailableReason.Unsupported)
        };
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference())).Attachments;
        var data = (await host.PrepareRunAsync(session, admitted)).Value!.Preview.Data;
        Assert.Equal("unsupported", data.GetProperty("selectedActivity").GetProperty("unavailable").GetString());
        Assert.Contains("not provided by this backend", data.GetProperty("selectedActivity").GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task A_run_still_in_progress_is_attributed_to_the_read_time()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        host.Runs.Read = request => Run(request, inProgress: true);
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference())).Attachments;
        var run = Assert.Single(admitted, item => item.ContentType == "workflows.run");
        var content = (JsonElement)run.Content!;
        Assert.True(content.GetProperty("inProgress").GetBoolean());
        Assert.Equal(ReadAt, content.GetProperty("readAt").GetDateTimeOffset());
        Assert.Contains("still in progress", run.Summary);

        var data = (await host.PrepareRunAsync(session, admitted)).Value!.Preview.Data;
        Assert.True(data.GetProperty("inProgress").GetBoolean());
        Assert.Equal(ReadAt, data.GetProperty("readAt").GetDateTimeOffset());
        Assert.Contains("still in progress (Running)", data.GetProperty("attribution").GetString());
        Assert.Contains(ReadAt.ToString("O"), data.GetProperty("attribution").GetString());
    }

    [Fact]
    public async Task Without_value_inspection_incident_messages_are_redacted_and_reported_as_not_permitted()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        host.Runs.Read = request => Run(request, canInspectValues: false);
        var session = await host.SessionAsync();
        var data = (await host.PrepareRunAsync(session, (await host.CollectRunAsync(session, RunReference())).Attachments)).Value!.Preview.Data;
        Assert.False(data.GetProperty("canInspectValues").GetBoolean());
        var incident = Assert.Single(data.GetProperty("incidents").EnumerateArray());
        Assert.Equal(("Incident details are redacted.", true), (incident.GetProperty("message").GetString(), incident.GetProperty("messageRedacted").GetBoolean()));
        Assert.Contains(data.GetProperty("unavailable").EnumerateArray(),
            item => item.GetProperty("data").GetString() == "incident messages" && item.GetProperty("reason").GetString() == "notPermitted");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("caller")]
    [InlineData("other")]
    [InlineData("argument")]
    [InlineData("page-size")]
    public async Task The_tool_reads_only_a_Run_an_admitted_context_names(string control)
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference())).Attachments;
        IReadOnlyCollection<AgentContextAttachment> context = control switch
        {
            "none" => [],
            // A caller's own copy is never the server's read.
            "caller" => [RunReference()],
            _ => admitted
        };
        var arguments = control switch
        {
            "other" => new Dictionary<string, object?> { ["workflowExecutionId"] = "run-2" },
            // No argument retargets the selection: the activity execution is the one the page selected.
            "argument" => new Dictionary<string, object?> { ["activityExecutionId"] = "act-fault" },
            "page-size" => new Dictionary<string, object?> { ["activityPageSize"] = 51 },
            _ => null
        };
        var reads = host.Runs.Requests.Count;

        var prepared = await host.PrepareRunAsync(session, context, arguments);

        Assert.False(prepared.Succeeded);
        Assert.Equal(control is "argument" or "page-size" ? "agent.workflow.run_arguments_invalid" : "agent.workflow.run_context_required", prepared.Error!.Code);
        Assert.Equal(reads, host.Runs.Requests.Count);
    }

    [Fact]
    public async Task The_tool_pages_the_activity_executions_of_the_bound_Run()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        host.Runs.Read = request => Run(request, activities: 5);
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference(activityExecutionId: null))).Attachments;
        var data = (await host.PrepareRunAsync(session, admitted, new Dictionary<string, object?> { ["activityPageSize"] = 2, ["activityContinuationToken"] = "page-2" }))
            .Value!.Preview.Data;
        var request = host.Runs.Requests[^1];
        Assert.Equal((2, "page-2", (IReadOnlySet<string>?)null), (request.ActivityPageSize, request.ActivityContinuationToken, request.ResolveEvidenceIds));
        var page = data.GetProperty("activities");
        Assert.Equal((2, 5L, "next"), (page.GetProperty("items").GetArrayLength(), page.GetProperty("totalCount").GetInt64(), page.GetProperty("nextContinuationToken").GetString()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("selectedActivity").ValueKind);
    }

    [Fact]
    public async Task A_large_run_is_bounded_under_the_attachment_budget_and_reported_as_truncated()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        host.Runs.Read = request => Run(request, activities: 50, incidents: 200);
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference())).Attachments;
        var data = (await host.PrepareRunAsync(session, admitted, new Dictionary<string, object?> { ["activityPageSize"] = 50 })).Value!.Preview.Data;
        Assert.True(data.GetRawText().Length <= WorkflowReadRunTool.ResultBudget, "The bounded Run read must fit the host's per-attachment budget.");
        Assert.True(data.GetProperty("incidentsTruncated").GetBoolean());
        Assert.Equal(200, data.GetProperty("instance").GetProperty("incidentCount").GetInt32());
        Assert.True(admitted.All(item => JsonSerializer.Serialize(item.Content).Length < 4096), "The Run context is a small identity read.");
    }

    [Fact]
    public async Task Retained_context_is_read_again_stable_while_the_Run_is_final_and_withheld_once_it_is_gone()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference())).Attachments
            .Select(item => item with { ContributorId = "workflows.run-context" }).ToArray();

        var current = await host.RunSource.AdmitDisclosureAsync(new(session.Id, admitted, [], false));
        Assert.Empty(current.Diagnostics);
        Assert.Equal(admitted.Select(item => JsonSerializer.Serialize(item.Content)), current.Attachments.Select(item => JsonSerializer.Serialize(item.Content)));

        // A read result is disclosed again only while the Run it describes is still readable.
        var result = new AgentToolResult("call-1", WorkflowReadRunTool.ToolName, true, "read")
        {
            Access = new(ModuleId, "1", WorkflowRunContext.Target("run-1", "act-write"))
        };
        Assert.Empty((await host.RunSource.AdmitDisclosureAsync(new(session.Id, admitted, [result], false))).Diagnostics);

        host.Runs.Known.Clear();
        var gone = await host.RunSource.AdmitDisclosureAsync(new(session.Id, admitted, [], false));
        Assert.Empty(gone.Attachments);
        Assert.Equal("agent.workflow.run_not_found", Assert.Single(gone.Diagnostics).Code);
        Assert.Equal("agent.workflow.run_not_found", Assert.Single((await host.RunSource.AdmitDisclosureAsync(new(session.Id, [], [result], false))).Diagnostics).Code);

        // A caller cannot retain its own copy as the server's read.
        var forged = await host.RunSource.AdmitDisclosureAsync(new(session.Id, [RunReference()], [], false));
        Assert.Equal("agent.workflow.disclosure_invalid", Assert.Single(forged.Diagnostics).Code);
    }

    [Fact]
    public async Task The_tool_authorizes_a_concrete_target_by_the_Runs_current_visibility()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var authorization = (IAgentToolAuthorization)host.RunTool;
        Assert.True((await authorization.AuthorizeAsync(session.ActorId, session.TenantId, WorkflowRunContext.Target("run-1", "act-write"))).Allowed);
        Assert.Equal("agent.workflow.run_activity_not_found",
            Assert.Single((await authorization.AuthorizeAsync(session.ActorId, session.TenantId, WorkflowRunContext.Target("run-1", "act-missing"))).Violations).Code);
        Assert.Equal("agent.workflow.run_not_found",
            Assert.Single((await authorization.AuthorizeAsync(session.ActorId, session.TenantId, WorkflowRunContext.Target("run-2", null))).Violations).Code);
        Assert.Equal("agent.workflow.resource_required",
            Assert.Single((await authorization.AuthorizeAsync(session.ActorId, session.TenantId, new(ModuleId, "workflow-definition", "run-1", "run-1"))).Violations).Code);
    }

    [Fact]
    public async Task The_capability_and_guidance_are_read_only_and_refuse_run_controls()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var capabilities = await host.Services.GetServices<IAgentCapabilityProvider>().OfType<WorkflowRuntimeAgentCapabilityProvider>().Single().GetCapabilitiesAsync();
        var capability = Assert.Single(capabilities, item => item.Id == "workflow.run-explain");
        Assert.Equal((ModuleId, AgentRisk.ReadOnly, AgentCapabilityKind.Answer), (capability.ModuleId, capability.Risk, capability.Kind));
        Assert.Equal(["workflows/runtime:read"], capability.RequiredPermissions);
        Assert.Equal(["workflows.run"], capability.ContextKinds);
        Assert.Equal(["workflows/runtime:read"], host.RunTool.Descriptor.RequiredPermissions);
        var guidance = WorkflowRunContext.Guidance().Summary;
        foreach (var phrase in new[] { "retry, cancel, delete or dispatch", "run page's own controls", "never guess or reconstruct", "not captured, pruned",
                     "in progress", "never the latest run" })
            Assert.Contains(phrase, guidance);
    }

    [Theory]
    [InlineData("inspect-values")]
    [InlineData("resolve-value-payloads")]
    public async Task A_retained_read_is_withheld_once_the_actor_loses_the_value_access_it_was_read_under(string revoked)
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference())).Attachments
            .Select(item => item with { ContributorId = "workflows.run-context" }).ToArray();
        var prepared = (await host.PrepareRunAsync(session, admitted)).Value!;
        Assert.Contains(prepared.Preview.Data.GetProperty("selectedActivity").GetProperty("values").EnumerateArray(),
            value => value.GetProperty("disclosure").GetString() == "resolved");
        var result = (await host.RunTool.ExecuteAsync(Prepared(session, prepared))).Value! with
        {
            Access = new(ModuleId, "1", WorkflowRunContext.Target("run-1", "act-write"))
        };
        Assert.Empty((await host.RunSource.AdmitDisclosureAsync(new(session.Id, admitted, [result], false))).Diagnostics);

        // An administrator revokes value inspection (incident details) or payload resolution (released values).
        host.Runs.Read = request => Run(request, canInspectValues: revoked != "inspect-values", canResolveValues: false);
        var withheld = await host.RunSource.AdmitDisclosureAsync(new(session.Id, admitted, [result], false));
        Assert.Empty(withheld.Attachments);
        var refusal = Assert.Single(withheld.Diagnostics);
        Assert.Equal((WorkflowRunContext.AccessChangedCode, true), (refusal.Code, refusal.Required));
        // The retained context itself carries no value or incident message, so it is still admitted, read again.
        Assert.Empty((await host.RunSource.AdmitDisclosureAsync(new(session.Id, admitted, [], false))).Diagnostics);
    }

    [Fact]
    public async Task A_retained_read_that_released_nothing_survives_a_revoked_payload_grant()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        host.Runs.Read = request => Run(request, canResolveValues: false);
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference(activityExecutionId: null))).Attachments
            .Select(item => item with { ContributorId = "workflows.run-context" }).ToArray();
        var prepared = (await host.PrepareRunAsync(session, admitted)).Value!;
        var result = (await host.RunTool.ExecuteAsync(Prepared(session, prepared))).Value! with
        {
            Access = new(ModuleId, "1", WorkflowRunContext.Target("run-1", null))
        };
        Assert.Empty((await host.RunSource.AdmitDisclosureAsync(new(session.Id, admitted, [result], false))).Diagnostics);
        Assert.True(WorkflowReadRunTool.Exceeds(new("workflows.run", "1", JsonSerializer.SerializeToElement(new { other = true })),
            Run(new("run-1"))), "A result whose disclosure cannot be read is treated as exceeding.");
    }

    [Fact]
    public async Task Hundreds_of_bookmarks_and_a_40_KB_exception_message_stay_under_the_budget_with_truncation_marked()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var message = "System.InvalidOperationException: " + new string('m', 40 * 1024);
        host.Runs.Read = request => Run(request, bookmarks: 600, incidents: 40, message: message, resultValue: new string('v', 40 * 1024), activities: 50);
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference())).Attachments;

        var prepared = await host.PrepareRunAsync(session, admitted, new Dictionary<string, object?> { ["activityPageSize"] = 50 });

        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var data = prepared.Value!.Preview.Data;
        Assert.True(data.GetRawText().Length <= WorkflowReadRunTool.ResultBudget);
        Assert.True(WorkflowReadRunTool.ResultBudget <= 24 * 1024, "The bound stays well under the host's 32 KiB check.");
        var selected = data.GetProperty("selectedActivity");
        Assert.True(selected.GetProperty("bookmarksTruncated").GetBoolean());
        Assert.Equal(600, selected.GetProperty("bookmarkCount").GetInt32());
        Assert.True(data.GetProperty("incidentsTruncated").GetBoolean());
        foreach (var incident in data.GetProperty("incidents").EnumerateArray())
        {
            Assert.True(incident.GetProperty("messageTruncated").GetBoolean());
            if (incident.GetProperty("message").GetString() is { } text) Assert.EndsWith(WorkflowReadRunTool.TruncationMarker, text);
        }
        // Every value the second read resolved is disclosed, so the payload read audited only what the result shows.
        var resolved = host.Runs.Requests[^1].ResolveEvidenceIds ?? new HashSet<string>();
        var values = selected.GetProperty("values").EnumerateArray().ToDictionary(item => item.GetProperty("evidenceId").GetString()!);
        Assert.All(resolved, id => Assert.Equal("resolved", values[id].GetProperty("disclosure").GetString()));
        Assert.All(resolved, id => Assert.False(values[id].GetProperty("valueOmitted").GetBoolean()));
        if (resolved.Count > 0) Assert.True(values["evidence-result"].GetProperty("valueTruncated").GetBoolean());
    }

    [Fact]
    public async Task A_value_that_escapes_to_several_times_its_length_is_measured_as_serialized_and_never_refused_after_resolution()
    {
        // 3 000 non-ASCII characters: about 3 KB of raw JSON, about 18 KB once the host's serializer escapes it.
        using var host = new WorkflowRuntimeAgentTestHost();
        host.Runs.Read = request => Run(request, resultValue: string.Concat(Enumerable.Repeat("é", 3000)));
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference())).Attachments;

        var prepared = await host.PrepareRunAsync(session, admitted);

        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var data = prepared.Value!.Preview.Data;
        Assert.True(data.GetRawText().Length <= WorkflowReadRunTool.ResultBudget);
        var result = data.GetProperty("selectedActivity").GetProperty("values").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "result");
        Assert.Equal("resolved", result.GetProperty("disclosure").GetString());
        Assert.True(result.GetProperty("valueTruncated").GetBoolean());
        Assert.Equal(["evidence-result"], host.Runs.Requests[^1].ResolveEvidenceIds!);
    }

    [Theory]
    [InlineData("""{"canInspectValues":false,"selectedActivity":{"values":[{"disclosure":7}]}}""")]
    [InlineData("""{"canInspectValues":false,"selectedActivity":{"values":[{"disclosure":"leaked"}]}}""")]
    [InlineData("""{"canInspectValues":false,"selectedActivity":{"values":[{}]}}""")]
    public void A_retained_disclosure_that_cannot_be_read_counts_as_released_and_never_throws(string data)
    {
        var current = Run(new("run-1")) with { CanResolveValues = false };
        Assert.True(WorkflowReadRunTool.Exceeds(new("workflows.run", "1", JsonDocument.Parse(data).RootElement.Clone()), current));
        Assert.False(WorkflowReadRunTool.Exceeds(new("workflows.run", "1",
            JsonDocument.Parse("""{"canInspectValues":false,"selectedActivity":{"values":[{"disclosure":"withheld"}]}}""").RootElement.Clone()), current));
    }

    [Fact]
    public async Task A_run_that_cannot_fit_even_its_smallest_bound_is_refused_never_sent_over_budget()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        host.Runs.Read = request => Run(request) with { Instance = Run(request).Instance with { DefinitionId = new string('d', 30 * 1024) } };
        var session = await host.SessionAsync();
        var admitted = (await host.CollectRunAsync(session, RunReference(activityExecutionId: null))).Attachments;
        var prepared = await host.PrepareRunAsync(session, admitted);
        Assert.False(prepared.Succeeded);
        Assert.Equal((WorkflowRunContext.TooLargeCode, 413), (prepared.Error!.Code, prepared.Error.StatusCode));
    }

    [Fact]
    public async Task Incident_messages_withheld_for_sensitive_inputs_are_listed_as_withheld()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        host.Runs.Read = request => Run(request) with
        {
            Incidents = [new("incident-1", "act-fault", "fault", "Error", "Active", "workflow.fault", "Incident details are withheld because their text could echo a sensitive input or data this read cannot inspect.",
                true, true, true, DateTimeOffset.UnixEpoch, null)]
        };
        var session = await host.SessionAsync();
        var data = (await host.PrepareRunAsync(session, (await host.CollectRunAsync(session, RunReference(activityExecutionId: null))).Attachments)).Value!.Preview.Data;
        var incident = Assert.Single(data.GetProperty("incidents").EnumerateArray());
        Assert.True(incident.GetProperty("sensitiveInputs").GetBoolean());
        Assert.True(incident.GetProperty("messageRedacted").GetBoolean());
        Assert.Contains(data.GetProperty("unavailable").EnumerateArray(), item => item.GetProperty("reason").GetString() == "withheld"
            && item.GetProperty("data").GetString() == "incident messages that could echo sensitive or uninspectable data");
    }

    private static AgentPreparedToolInvocation Prepared(AgentSession session, AgentToolPreparation preparation) =>
        new("invocation-1", session.Id, "turn-1", "call-1", "workflow_read_run", "1", ModuleId, session.ActorId, session.TenantId, preparation.Target,
            preparation.Arguments, "digest", preparation.Risk, preparation.Preview, AgentExecutionLocation.Server, false, null, DateTimeOffset.UtcNow.AddMinutes(5));
}
