using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Runtime.Services;
using Elsa.Weaver.Workflows.Runtime.Tools;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Elsa.Weaver.Workflows.Runtime.Tests.WorkflowRuntimeAgentTestHost;

namespace Elsa.Weaver.Workflows.Runtime.Tests;

// Spec 1193 slice F (story 8, FR-027, FR-028): one exact Executable, read-only, bound to its Artifact Hash.
public sealed class WorkflowExecutableAgentTests
{
    [Fact]
    public void The_feature_registers_the_internal_sealed_runtime_contributions_and_no_fallback_owner()
    {
        var services = new ServiceCollection();
        new WeaverWorkflowsRuntimeFeature().ConfigureServices(services);
        foreach (var (contract, implementation) in new[]
                 {
                     (typeof(IAgentContextProvider), typeof(WorkflowExecutableContextProvider)), (typeof(IAgentTool), typeof(WorkflowReadExecutableTool)),
                     (typeof(IAgentCapabilityProvider), typeof(WorkflowRuntimeAgentCapabilityProvider))
                 })
        {
            var descriptor = Assert.Single(services, item => item.ServiceType == contract && item.ImplementationType == implementation);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            Assert.True(implementation.IsSealed);
            Assert.False(implementation.IsPublic);
        }
        // The Runtime owner is never substituted: without an installed inspection the context and tool say unavailable.
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutableInspection));
    }

    [Fact]
    public async Task A_reference_bound_to_its_hash_is_admitted_as_the_servers_read_with_its_Source_References()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();

        var contribution = await host.CollectAsync(session, Reference());

        Assert.Empty(contribution.Diagnostics);
        var admitted = Assert.Single(contribution.Attachments, item => item.ContentType == "workflows.executable");
        Assert.Equal(AgentContextOrigin.AuthoritativeRead, admitted.Origin);
        Assert.Equal(new AgentResourceTarget(ModuleId, "workflow-executable", "artifact-1", Hash), admitted.Target);
        var content = (JsonElement)admitted.Content!;
        Assert.Equal(("artifact-1", Hash, 3, 2), (content.GetProperty("artifactId").GetString(), content.GetProperty("artifactHash").GetString(),
            content.GetProperty("nodeCount").GetInt32(), content.GetProperty("liveSourceReferenceCount").GetInt32()));
        Assert.Equal(["published-1", "published-2"], content.GetProperty("sourceReferences").EnumerateArray().Select(item => item.GetProperty("sourceReferenceId").GetString()));
        Assert.False(content.GetProperty("sourceReferencesTruncated").GetBoolean());
        Assert.Single(contribution.Attachments, item => item.ContentType == "workflows.executable-guidance" && item.Origin == AgentContextOrigin.InstalledGuidance);
    }

    [Fact]
    public async Task Without_an_Executable_reference_the_source_contributes_nothing()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var contribution = await host.CollectAsync(await host.SessionAsync());
        Assert.Empty(contribution.Attachments);
        Assert.Empty(contribution.Diagnostics);
        Assert.Equal(0, host.Inspection.Reads);
    }

    [Theory]
    [InlineData("stale", "weaver.workflow.executable_stale", "The workflow Executable's Artifact Hash differs from the bound hash, so the context is stale.")]
    [InlineData("unknown", "weaver.workflow.executable_not_found", "The workflow Executable is unavailable.")]
    [InlineData("denied", "weaver.workflow.permission_denied", "Current workflow runtime read permission is required for the workflow Executable.")]
    [InlineData("malformed", "weaver.workflow.executable_context_invalid", null)]
    [InlineData("unbound", "weaver.workflow.executable_context_invalid", null)]
    public async Task A_stale_unknown_denied_or_malformed_reference_is_refused_with_nothing_disclosed(string control, string code, string? message)
    {
        using var host = new WorkflowRuntimeAgentTestHost(authorizer: control == "denied" ? new Authorizer("workflows/runtime:read") : null);
        var session = await host.SessionAsync();
        var reference = control switch
        {
            "stale" => Reference(hash: "sha256:other"),
            "unknown" => Reference("artifact-missing"),
            "malformed" => Reference(content: new { artifactId = "artifact-1", artifactHash = Hash, nodes = 3 }),
            "unbound" => Reference() with { Target = new(ModuleId, "workflow-executable", "artifact-1", "sha256:other") },
            _ => Reference()
        };

        var contribution = await host.CollectAsync(session, reference);

        Assert.Empty(contribution.Attachments);
        var refusal = Assert.Single(contribution.Diagnostics);
        Assert.Equal((code, true, false), (refusal.Code, refusal.Required, refusal.OwnerUnavailable));
        if (message is not null) Assert.Equal(message, refusal.Message);
        // The host's runtime read permission is decided before anything is read.
        if (control == "denied") Assert.Equal(0, host.Inspection.Reads);
    }

    [Fact]
    public async Task A_missing_inspection_owner_is_unavailable_never_a_guess()
    {
        using var host = new WorkflowRuntimeAgentTestHost(configure: services =>
            services.RemoveAll<Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutableInspection>());
        var session = await host.SessionAsync();
        var refusal = Assert.Single((await host.CollectAsync(session, Reference())).Diagnostics);
        Assert.Equal(("workflow.executable.inspection_unavailable", true, true), (refusal.Code, refusal.Required, refusal.OwnerUnavailable));
        var decision = await ((IAgentToolAuthorization)host.Tool).AuthorizeAsync(session.ActorId, session.TenantId, null);
        Assert.False(decision.Allowed);
        Assert.True(Assert.Single(decision.Violations).OwnerUnavailable);
    }

    [Fact]
    public async Task The_tool_returns_the_exact_Executable_graph_and_Source_References_read_only()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var admitted = (await host.CollectAsync(session, Reference())).Attachments;

        var prepared = await host.PrepareAsync(session, admitted);

        Assert.True(prepared.Succeeded);
        Assert.False(prepared.Value!.RequiresApproval);
        Assert.Equal(AgentRisk.ReadOnly, prepared.Value.Risk);
        Assert.Equal(new AgentResourceTarget(ModuleId, "workflow-executable", "artifact-1", Hash), prepared.Value.Target);
        var data = prepared.Value.Preview.Data;
        Assert.Equal("workflows.executable", prepared.Value.Preview.TypeId);
        Assert.True(data.GetProperty("readOnly").GetBoolean());
        Assert.Equal((Hash, 3, false), (data.GetProperty("artifactHash").GetString(), data.GetProperty("nodesIncluded").GetInt32(), data.GetProperty("graphTruncated").GetBoolean()));
        var graph = data.GetProperty("graph");
        Assert.Equal("root", graph.GetProperty("executableNodeId").GetString());
        var input = Assert.Single(graph.GetProperty("inputs").EnumerateArray());
        Assert.Equal(("secret-key", "Literal", true), (input.GetProperty("name").GetString(), input.GetProperty("source").GetString(), input.GetProperty("sensitive").GetBoolean()));
        var slot = Assert.Single(graph.GetProperty("slots").EnumerateArray());
        Assert.Equal(["node-1", "node-2"], slot.GetProperty("nodes").EnumerateArray().Select(item => item.GetProperty("executableNodeId").GetString()));
        var connection = Assert.Single(graph.GetProperty("connections").EnumerateArray());
        Assert.Equal(("node-1", "Done", "node-2", "In"), (connection.GetProperty("source").GetString(), connection.GetProperty("sourcePort").GetString(),
            connection.GetProperty("target").GetString(), connection.GetProperty("targetPort").GetString()));
        Assert.Equal(2, data.GetProperty("sourceReferences").GetArrayLength());
        Assert.Equal("dependency-1", Assert.Single(data.GetProperty("dependencies").EnumerateArray()).GetProperty("artifactId").GetString());

        var executed = await host.Tool.ExecuteAsync(Prepared(session, prepared.Value));
        Assert.True(executed.Succeeded);
        Assert.Null(executed.Value!.Receipt);
        Assert.Equal(data.GetRawText(), executed.Value.Payload!.Data.GetRawText());
    }

    [Fact]
    public async Task A_hash_that_changed_after_admission_refuses_the_read_as_stale()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var admitted = (await host.CollectAsync(session, Reference())).Attachments;
        host.Inspection.Current = Executable("sha256:republished");

        var prepared = await host.PrepareAsync(session, admitted);

        Assert.False(prepared.Succeeded);
        Assert.Equal(("weaver.workflow.executable_stale", 409), (prepared.Error!.Code, prepared.Error.StatusCode));
        var decision = await ((IAgentToolAuthorization)host.Tool).AuthorizeAsync(session.ActorId, session.TenantId, new(ModuleId, "workflow-executable", "artifact-1", Hash));
        Assert.Equal("weaver.workflow.executable_stale", Assert.Single(decision.Violations).Code);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("caller")]
    [InlineData("other")]
    [InlineData("argument")]
    public async Task The_tool_reads_only_an_Executable_an_admitted_context_names(string control)
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var admitted = (await host.CollectAsync(session, Reference())).Attachments;
        IReadOnlyCollection<AgentContextAttachment> context = control switch
        {
            "none" => [],
            // A caller's own copy is never the server's read.
            "caller" => [Reference()],
            _ => admitted
        };
        var arguments = control switch
        {
            "other" => new Dictionary<string, object?> { ["artifactId"] = "artifact-2" },
            "argument" => new Dictionary<string, object?> { ["artifactHash"] = Hash },
            _ => null
        };

        var prepared = await host.PrepareAsync(session, context, arguments);

        Assert.False(prepared.Succeeded);
        Assert.Equal(control == "argument" ? "weaver.workflow.executable_arguments_invalid" : "weaver.workflow.executable_context_required", prepared.Error!.Code);
    }

    [Fact]
    public async Task A_large_graph_is_bounded_and_reported_as_truncated()
    {
        using var host = new WorkflowRuntimeAgentTestHost(new(Executable(children: 400)));
        var session = await host.SessionAsync();
        var prepared = await host.PrepareAsync(session, (await host.CollectAsync(session, Reference())).Attachments);
        var data = prepared.Value!.Preview.Data;
        Assert.True(data.GetProperty("graphTruncated").GetBoolean());
        Assert.InRange(data.GetProperty("nodesIncluded").GetInt32(), 2, WorkflowReadExecutableTool.NodeLimit);
        Assert.Equal(401, data.GetProperty("nodeCount").GetInt32());
        Assert.True(data.GetRawText().Length <= WorkflowReadExecutableTool.ResultBudget, "The bounded Executable read must fit the host's per-attachment budget.");
    }

    [Fact]
    public async Task A_flowchart_with_300_connections_and_long_input_lists_is_bounded_per_node_and_drops_no_connection_silently()
    {
        var children = Enumerable.Range(1, 301).Select(index => new WorkflowExecutableInspectionNode($"node-{index}", $"node-{index}", "Elsa.WriteLine", "1.0.0", null,
            Enumerable.Range(1, 60).Select(input => new WorkflowExecutableInspectionInput($"input-{input}", "Literal", false)).ToArray(), [], [])).ToArray();
        var connections = Enumerable.Range(1, 300).Select(index => new WorkflowExecutableInspectionConnection($"node-{index}", "Done", $"node-{index + 1}", "In")).ToArray();
        var executable = Executable() with
        {
            NodeCount = 302,
            RootActivity = new("root", "root", "Elsa.Flowchart", "1.0.0", "elsa.flowchart.structure", [], [new("activities", children)], connections)
        };
        using var host = new WorkflowRuntimeAgentTestHost(new(executable));
        var session = await host.SessionAsync();

        var prepared = await host.PrepareAsync(session, (await host.CollectAsync(session, Reference())).Attachments);

        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var data = prepared.Value!.Preview.Data;
        Assert.True(data.GetRawText().Length <= WorkflowReadExecutableTool.ResultBudget);
        Assert.True(data.GetProperty("graphTruncated").GetBoolean());
        var included = new HashSet<string>();
        void Visit(JsonElement node)
        {
            included.Add(node.GetProperty("executableNodeId").GetString()!);
            foreach (var slot in node.GetProperty("slots").EnumerateArray())
                foreach (var child in slot.GetProperty("nodes").EnumerateArray()) Visit(child);
        }
        var graph = data.GetProperty("graph");
        Visit(graph);
        Assert.Equal(included.Count, data.GetProperty("nodesIncluded").GetInt32());
        // No kept connection names an omitted node, and the omitted ones are counted.
        var kept = graph.GetProperty("connections").EnumerateArray().ToArray();
        Assert.All(kept, connection => Assert.True(included.Contains(connection.GetProperty("source").GetString()!) && included.Contains(connection.GetProperty("target").GetString()!)));
        Assert.Equal(300 - kept.Length, data.GetProperty("connectionsOmitted").GetInt32());
        Assert.True(graph.GetProperty("connectionsTruncated").GetBoolean());
        // Each node's inputs are bounded and say so.
        var first = graph.GetProperty("slots")[0].GetProperty("nodes")[0];
        Assert.True(first.GetProperty("inputs").GetArrayLength() < 60);
        Assert.True(first.GetProperty("inputsTruncated").GetBoolean());
    }

    [Fact]
    public async Task A_cut_text_value_is_counted_never_cut_silently()
    {
        var executable = Executable() with { RootActivity = Executable().RootActivity with { ActivityType = new string('t', 300) } };
        using var host = new WorkflowRuntimeAgentTestHost(new(executable));
        var session = await host.SessionAsync();
        var data = (await host.PrepareAsync(session, (await host.CollectAsync(session, Reference())).Attachments)).Value!.Preview.Data;
        Assert.Equal(256, data.GetProperty("graph").GetProperty("activityType").GetString()!.Length);
        Assert.Equal(1, data.GetProperty("truncatedFields").GetInt32());
        using var plain = new WorkflowRuntimeAgentTestHost();
        var plainSession = await plain.SessionAsync();
        Assert.Equal(0, (await plain.PrepareAsync(plainSession, (await plain.CollectAsync(plainSession, Reference())).Attachments)).Value!.Preview.Data
            .GetProperty("truncatedFields").GetInt32());
    }

    [Fact]
    public async Task A_read_that_cannot_fit_even_its_smallest_bound_is_refused_never_returned_unbounded()
    {
        var huge = new string('x', 40 * 1024);
        using var host = new WorkflowRuntimeAgentTestHost(new(Executable() with { RootActivity = Executable().RootActivity with { ExecutableNodeId = "root" } }));
        var session = await host.SessionAsync();
        var admitted = (await host.CollectAsync(session, Reference())).Attachments;
        host.Inspection.Current = Executable() with { RootActivity = Executable().RootActivity with { AuthoredActivityId = huge } };

        var prepared = await host.PrepareAsync(session, admitted);

        Assert.False(prepared.Succeeded);
        Assert.Equal(("weaver.workflow.executable_too_large", 413), (prepared.Error!.Code, prepared.Error.StatusCode));
    }

    [Fact]
    public async Task Retained_context_is_read_again_and_withheld_once_the_hash_changes()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var admitted = (await host.CollectAsync(session, Reference())).Attachments
            .Select(item => item with { ContributorId = "workflows.executable-context" }).ToArray();

        var current = await host.Source.AdmitDisclosureAsync(new(session.Id, admitted, [], false));
        Assert.Empty(current.Diagnostics);
        Assert.Equal(admitted.Select(item => JsonSerializer.Serialize(item.Content)), current.Attachments.Select(item => JsonSerializer.Serialize(item.Content)));

        host.Inspection.Current = Executable("sha256:republished");
        var stale = await host.Source.AdmitDisclosureAsync(new(session.Id, admitted, [], false));
        Assert.Empty(stale.Attachments);
        Assert.Equal("weaver.workflow.executable_stale", Assert.Single(stale.Diagnostics).Code);

        // A caller cannot retain its own copy as the server's read.
        var forged = await host.Source.AdmitDisclosureAsync(new(session.Id, [Reference()], [], false));
        Assert.Equal("weaver.workflow.disclosure_invalid", Assert.Single(forged.Diagnostics).Code);
    }

    [Fact]
    public async Task The_capability_is_read_only_explain_under_runtime_read()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var capability = Assert.Single(await host.Services.GetServices<IAgentCapabilityProvider>().OfType<WorkflowRuntimeAgentCapabilityProvider>().Single().GetCapabilitiesAsync(),
            item => item.Id == "workflow.executable-explain");
        Assert.Equal(("workflow.executable-explain", ModuleId, AgentRisk.ReadOnly), (capability.Id, capability.ModuleId, capability.Risk));
        Assert.Equal(["workflows/runtime:read"], capability.RequiredPermissions);
        Assert.Equal(["workflows.executable"], capability.ContextKinds);
        Assert.Equal(["workflows/runtime:read"], host.Tool.Descriptor.RequiredPermissions);
        Assert.Equal(AgentToolMutability.ReadOnly, host.Tool.Descriptor.Mutability);
    }

    private static AgentPreparedToolInvocation Prepared(AgentSession session, AgentToolPreparation preparation) =>
        new("invocation-1", session.Id, "turn-1", "call-1", "workflow_read_executable", "1", ModuleId, session.ActorId, session.TenantId, preparation.Target,
            preparation.Arguments, "digest", preparation.Risk, preparation.Preview, AgentExecutionLocation.Server, false, null, DateTimeOffset.UtcNow.AddMinutes(5));
}
