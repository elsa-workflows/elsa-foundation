using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows;
using Elsa.Agent.Workflows.Services;
using Elsa.Agent.Workflows.Tools;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Agent.Tests;

// Spec 1193 slice F (story 8, FR-005, FR-015, FR-026): one exact persisted version, read-only, through the owning design
// services, with the tool's own structural comparison of two exact versions.
public sealed class WorkflowVersionAgentTests
{
    private const string Module = WorkflowVersionContext.ModuleId;

    [Fact]
    public async Task A_version_reference_is_admitted_as_the_servers_read_of_that_exact_version()
    {
        using var host = new Host();
        var contribution = await host.CollectAsync(Reference("definition-1", "v2"));

        Assert.Empty(contribution.Diagnostics);
        var admitted = Assert.Single(contribution.Attachments, item => item.ContentType == WorkflowVersionContext.VersionKind);
        Assert.Equal(AgentContextOrigin.AuthoritativeRead, admitted.Origin);
        Assert.Equal(WorkflowVersionContext.Target("v2"), admitted.Target);
        var content = (JsonElement)admitted.Content!;
        Assert.Equal(("definition-1", "Onboarding", "v2", "2.0.0", 4), (content.GetProperty("definitionId").GetString(), content.GetProperty("definitionName").GetString(),
            content.GetProperty("versionId").GetString(), content.GetProperty("version").GetString(), content.GetProperty("activityCount").GetInt32()));
        Assert.Equal(["v1", "v2", "v3"], content.GetProperty("versions").EnumerateArray().Select(item => item.GetProperty("versionId").GetString()));
        Assert.False(content.GetProperty("versionsTruncated").GetBoolean());
        Assert.False(content.GetProperty("activitiesTruncated").GetBoolean());
        // Identity only: no authored value is part of the context.
        Assert.DoesNotContain("value-", admitted.Content!.ToString(), StringComparison.Ordinal);
        Assert.Single(contribution.Attachments, item => item.ContentType == WorkflowVersionContext.GuidanceKind && item.Origin == AgentContextOrigin.InstalledGuidance);
    }

    [Fact]
    public async Task Without_a_version_reference_the_source_contributes_nothing()
    {
        using var host = new Host();
        var contribution = await host.CollectAsync();
        Assert.Empty(contribution.Attachments);
        Assert.Empty(contribution.Diagnostics);
        Assert.Equal(0, host.Reader.Reads);
    }

    [Theory]
    [InlineData("unknown", WorkflowVersionContext.NotFoundCode, WorkflowVersionContext.NotFoundMessage)]
    [InlineData("other-definition", WorkflowVersionContext.NotFoundCode, WorkflowVersionContext.NotFoundMessage)]
    [InlineData("owner-denied", WorkflowVersionContext.NotFoundCode, WorkflowVersionContext.NotFoundMessage)]
    [InlineData("host-denied", "agent.workflow.permission_denied", WorkflowVersionContext.DeniedMessage)]
    [InlineData("malformed", "agent.workflow.version_context_invalid", null)]
    [InlineData("unbound", "agent.workflow.version_context_invalid", null)]
    public async Task An_unknown_withheld_or_malformed_version_reference_is_refused_with_nothing_disclosed(string control, string code, string? message)
    {
        using var host = new Host(hostDenied: control == "host-denied", owner: control == "owner-denied" ? new Owner("definition-2") : null);
        var reference = control switch
        {
            "unknown" => Reference("definition-1", "missing"),
            "other-definition" => Reference("definition-1", "other-v1"),
            "malformed" => Reference("definition-1", "v2", new { definitionId = "definition-1", versionId = "v2", version = "2.0.0" }),
            "unbound" => Reference("definition-1", "v2") with { Target = new(Module, "workflow-version", "v1", "v1") },
            _ => Reference("definition-1", "v2")
        };

        var contribution = await host.CollectAsync(reference);

        Assert.Empty(contribution.Attachments);
        var refusal = Assert.Single(contribution.Diagnostics);
        Assert.Equal((code, true, false), (refusal.Code, refusal.Required, refusal.OwnerUnavailable));
        if (message is not null) Assert.Equal(message, refusal.Message);
        // The host's design read permission and the owner are both decided before the version is read.
        if (control is "host-denied" or "owner-denied") Assert.Equal(0, host.Reader.Reads);
    }

    [Fact]
    public async Task An_owner_that_cannot_decide_or_a_missing_reader_is_unavailable_never_denied()
    {
        using (var host = new Host(owner: new Owner(throws: true)))
        {
            var refusal = Assert.Single((await host.CollectAsync(Reference("definition-1", "v2"))).Diagnostics);
            Assert.Equal((WorkflowDefinitionAuthorizationException.UnavailableCode, true), (refusal.Code, refusal.OwnerUnavailable));
        }
        using (var host = new Host(configure: services => services.RemoveAll<IWorkflowVersionReader>()))
        {
            var refusal = Assert.Single((await host.CollectAsync(Reference("definition-1", "v2"))).Diagnostics);
            Assert.Equal((WorkflowVersionContext.ReaderUnavailableCode, true), (refusal.Code, refusal.OwnerUnavailable));
            var decision = await host.Tool.AuthorizeAsync("actor-1", "tenant-1", null);
            Assert.True(Assert.Single(decision.Violations).OwnerUnavailable);
        }
    }

    [Fact]
    public async Task The_tool_reads_the_exact_version_by_scope_path_with_input_names_only()
    {
        using var host = new Host();
        var admitted = (await host.CollectAsync(Reference("definition-1", "v2"))).Attachments;

        var prepared = await host.PrepareAsync(admitted);

        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        Assert.False(prepared.Value!.RequiresApproval);
        Assert.Equal(AgentRisk.ReadOnly, prepared.Value.Risk);
        Assert.Equal(WorkflowVersionContext.Target("v2"), prepared.Value.Target);
        var data = prepared.Value.Preview.Data;
        Assert.True(data.GetProperty("readOnly").GetBoolean());
        var version = data.GetProperty("version");
        Assert.Equal(("v2", "2.0.0", 4), (version.GetProperty("versionId").GetString(), version.GetProperty("version").GetString(), version.GetProperty("activityCount").GetInt32()));
        var rows = version.GetProperty("activities").EnumerateArray().ToDictionary(item => item.GetProperty("nodeId").GetString()!);
        Assert.Equal(JsonValueKind.Null, rows["flow"].GetProperty("slot").ValueKind);
        Assert.Empty(rows["flow"].GetProperty("scopePath").EnumerateArray());
        Assert.Equal(["flow", "nested"], rows["inner"].GetProperty("scopePath").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("Activities", rows["inner"].GetProperty("slot").GetString());
        Assert.Equal(["text"], rows["a"].GetProperty("inputs").EnumerateArray().Select(item => item.GetString()));
        var connection = Assert.Single(version.GetProperty("connections").EnumerateArray());
        // A target without a port is the Done outcome, as the flowchart reads it.
        Assert.Equal(("flow", "a", "Done", "nested", (string?)"Done"), (connection.GetProperty("container").GetString(), connection.GetProperty("source").GetString(),
            connection.GetProperty("sourcePort").GetString(), connection.GetProperty("target").GetString(), connection.GetProperty("targetPort").GetString()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("comparison").ValueKind);
        Assert.DoesNotContain("value-", data.GetRawText(), StringComparison.Ordinal);

        var executed = await host.Tool.ExecuteAsync(Prepared(prepared.Value));
        Assert.True(executed.Succeeded);
        Assert.Null(executed.Value!.Receipt);
        Assert.Equal(data.GetRawText(), executed.Value.Payload!.Data.GetRawText());
    }

    [Fact]
    public async Task Comparing_two_exact_versions_is_the_tools_own_structural_comparison()
    {
        using var host = new Host();
        var admitted = (await host.CollectAsync(Reference("definition-1", "v2"))).Attachments;

        var prepared = await host.PrepareAsync(admitted, new Dictionary<string, object?> { ["compareToVersionId"] = "v1" });

        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var data = prepared.Value!.Preview.Data;
        Assert.Equal("v1", data.GetProperty("comparedTo").GetProperty("versionId").GetString());
        var comparison = data.GetProperty("comparison");
        Assert.Equal("workflow_read_version", comparison.GetProperty("computedBy").GetString());
        Assert.Contains("own structural comparison", comparison.GetProperty("note").GetString());
        // v1: flow > { a, b }, a -> b. v2: flow > { a (text changed), nested > { inner } }, a -> nested.
        Assert.Equal(["inner", "nested"], comparison.GetProperty("activitiesAdded").EnumerateArray().Select(item => item.GetProperty("nodeId").GetString()));
        Assert.Equal(["b"], comparison.GetProperty("activitiesRemoved").EnumerateArray().Select(item => item.GetProperty("nodeId").GetString()));
        var changed = Assert.Single(comparison.GetProperty("activitiesChanged").EnumerateArray());
        Assert.Equal(("a", "inputs", "text"), (changed.GetProperty("nodeId").GetString(), Assert.Single(changed.GetProperty("aspects").EnumerateArray()).GetString(),
            Assert.Single(changed.GetProperty("inputs").EnumerateArray()).GetString()));
        Assert.Equal("nested", Assert.Single(comparison.GetProperty("connectionsAdded").EnumerateArray()).GetProperty("target").GetString());
        Assert.Equal("b", Assert.Single(comparison.GetProperty("connectionsRemoved").EnumerateArray()).GetProperty("target").GetString());
        Assert.False(comparison.GetProperty("truncated").GetBoolean());
        Assert.DoesNotContain("value-", data.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Structural_changes_without_new_activities_are_compared_too()
    {
        using var host = new Host(owner: new Owner("definition-3"));
        var admitted = (await host.CollectAsync(Reference("definition-3", "s2"))).Attachments;

        var prepared = await host.PrepareAsync(admitted, new Dictionary<string, object?> { ["compareToVersionId"] = "s1" });

        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var comparison = prepared.Value!.Preview.Data.GetProperty("comparison");
        Assert.Empty(comparison.GetProperty("activitiesAdded").EnumerateArray());
        Assert.Empty(comparison.GetProperty("activitiesRemoved").EnumerateArray());
        var changed = comparison.GetProperty("activitiesChanged").EnumerateArray().ToDictionary(item => item.GetProperty("nodeId").GetString()!,
            item => item.GetProperty("aspects").EnumerateArray().Select(aspect => aspect.GetString()).ToArray());
        // The flowchart's start node moved; a's placement and intrinsic changed; b did not change. Its children swapped places
        // in its list, which a flowchart's connections make meaningless, so no child order is reported for it.
        Assert.Equal(["structure"], changed["flow"]);
        Assert.Equal(["intrinsic", "placement"], changed["a"]);
        Assert.False(changed.ContainsKey("b"));
        Assert.False(comparison.GetProperty("incomplete").GetBoolean());
    }

    [Fact]
    public async Task An_ordered_containers_child_order_is_compared()
    {
        using var host = new Host(owner: new Owner("definition-5"));
        var prepared = await host.PrepareAsync((await host.CollectAsync(Reference("definition-5", "q2"))).Attachments,
            new Dictionary<string, object?> { ["compareToVersionId"] = "q1" });
        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var changed = Assert.Single(prepared.Value!.Preview.Data.GetProperty("comparison").GetProperty("activitiesChanged").EnumerateArray());
        Assert.Equal(("sequence", "childOrder"), (changed.GetProperty("nodeId").GetString(), Assert.Single(changed.GetProperty("aspects").EnumerateArray()).GetString()));
        Assert.Equal(["Activities"], changed.GetProperty("reorderedSlots").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task Connection_and_node_metadata_belong_to_their_connection_and_child_not_to_the_container()
    {
        using var host = new Host(owner: new Owner("definition-5"));
        var prepared = await host.PrepareAsync((await host.CollectAsync(Reference("definition-5", "m2"))).Attachments,
            new Dictionary<string, object?> { ["compareToVersionId"] = "m1" });
        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var comparison = prepared.Value!.Preview.Data.GetProperty("comparison");
        var changed = comparison.GetProperty("activitiesChanged").EnumerateArray().ToDictionary(item => item.GetProperty("nodeId").GetString()!,
            item => item.GetProperty("aspects").EnumerateArray().Select(aspect => aspect.GetString()).ToArray());
        // m2 adds c with its own metadata and a connection b -> c with metadata, relabels a -> b and changes b's join policy:
        // the flowchart itself is unchanged.
        Assert.False(changed.ContainsKey("flow"));
        Assert.Equal(["containerMetadata"], changed["b"]);
        Assert.Equal(["c"], comparison.GetProperty("activitiesAdded").EnumerateArray().Select(item => item.GetProperty("nodeId").GetString()));
        Assert.Equal("c", Assert.Single(comparison.GetProperty("connectionsAdded").EnumerateArray()).GetProperty("target").GetString());
        Assert.Equal("b", Assert.Single(comparison.GetProperty("connectionsChanged").EnumerateArray()).GetProperty("target").GetString());
        Assert.Empty(comparison.GetProperty("connectionsRemoved").EnumerateArray());
    }

    [Fact]
    public async Task Connections_are_compared_by_the_flowcharts_own_identity_and_id_less_metadata_is_never_lost()
    {
        using var host = new Host(owner: new Owner("definition-5"));
        var prepared = await host.PrepareAsync((await host.CollectAsync(Reference("definition-5", "i2"))).Attachments,
            new Dictionary<string, object?> { ["compareToVersionId"] = "i1" });
        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var comparison = prepared.Value!.Preview.Data.GetProperty("comparison");
        // The id-less a -> b keeps the flowchart's key; its default flag change is reported on it.
        var changed = Assert.Single(comparison.GetProperty("connectionsChanged").EnumerateArray());
        Assert.Equal(("a:Done->b", "metadata"), (changed.GetProperty("connectionId").GetString(), Assert.Single(changed.GetProperty("aspects").EnumerateArray()).GetString()));
        // Two connections with the same endpoints are two connections: the one that went away is reported removed.
        Assert.Equal("twin-2", Assert.Single(comparison.GetProperty("connectionsRemoved").EnumerateArray()).GetProperty("connectionId").GetString());
        Assert.Empty(comparison.GetProperty("connectionsAdded").EnumerateArray());
        // Metadata for no connection or child stays the container's own structure.
        var flow = Assert.Single(comparison.GetProperty("activitiesChanged").EnumerateArray());
        Assert.Equal(("flow", "structure"), (flow.GetProperty("nodeId").GetString(), Assert.Single(flow.GetProperty("aspects").EnumerateArray()).GetString()));
    }

    [Fact]
    public async Task Connections_are_normalized_as_the_runtime_reads_them()
    {
        using var host = new Host(owner: new Owner("definition-5"));
        var prepared = await host.PrepareAsync((await host.CollectAsync(Reference("definition-5", "p2"))).Attachments,
            new Dictionary<string, object?> { ["compareToVersionId"] = "p1" });
        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var data = prepared.Value!.Preview.Data;
        var comparison = data.GetProperty("comparison");
        // Omitted, blank and padded ports, a blank id, a padded id and other letter case are the same two connections.
        Assert.Empty(comparison.GetProperty("connectionsAdded").EnumerateArray());
        Assert.Empty(comparison.GetProperty("connectionsRemoved").EnumerateArray());
        // The id-less connection's metadata attached under the runtime's key, so its default flag change is reported on it.
        var changed = Assert.Single(comparison.GetProperty("connectionsChanged").EnumerateArray());
        Assert.Equal(("a:Done->b", "metadata"), (changed.GetProperty("connectionId").GetString(), Assert.Single(changed.GetProperty("aspects").EnumerateArray()).GetString()));
        Assert.Empty(comparison.GetProperty("activitiesChanged").EnumerateArray());
        var rows = data.GetProperty("version").GetProperty("connections").EnumerateArray().Select(row => (row.GetProperty("connectionId").GetString(),
            row.GetProperty("sourcePort").GetString(), row.GetProperty("targetPort").GetString())).ToArray();
        Assert.Equal([("a:Done->b", "Done", "Done"), ("twin", "Done", "Done")], rows);
        Assert.Equal("Done", WorkflowVersionStructure.NormalizePort(null));
        Assert.Equal("True", WorkflowVersionStructure.NormalizePort(" True "));
    }

    [Theory]
    // Of several case-insensitively matching properties the runtime binds the last; so does the reading.
    [InlineData("""{"connections":[{"id":"x","Id":"y","source":{"nodeId":"a"},"target":{"nodeId":"b"}}]}""", "y", "Done")]
    [InlineData("""{"connections":[{"id":"x","id":"z","source":{"nodeId":"a"},"target":{"nodeId":"b"}}]}""", "z", "Done")]
    [InlineData("""{"connections":[{"source":{"nodeId":"a","port":"Skip","Port":" True "},"target":{"nodeId":"b"}}]}""", "a:True->b", "True")]
    [InlineData("""{"Connections":[],"connections":[{"source":{"nodeId":"a"},"target":{"nodeId":"b"}}]}""", "a:Done->b", "Done")]
    [InlineData("""{"connections":[{"source":{"nodeId":"q"},"Source":{"nodeId":"a"},"target":{"nodeId":"b"}}]}""", "a:Done->b", "Done")]
    public void Duplicate_properties_read_as_the_runtime_binds_them(string payload, string key, string port)
    {
        var read = WorkflowVersionStructure.Flatten(Raw(payload), new DefaultActivityStructureService([new FlowHandler()]));
        Assert.False(read.Incomplete);
        var connection = Assert.Single(read.Connections);
        Assert.Equal((key, port), (connection.Key, connection.SourcePort));
    }

    [Fact]
    public void Duplicate_metadata_entries_read_as_the_runtime_binds_them()
    {
        var read = WorkflowVersionStructure.Flatten(Raw("""
            {"connections":[{"id":"ab","source":{"nodeId":"a"},"target":{"nodeId":"b"}}],
             "connectionMetadata":{"ab":{"label":"first"}},"ConnectionMetadata":{"ab":{"label":"one"},"ab":{"label":"last"}}}
            """), new DefaultActivityStructureService([new FlowHandler()]));
        Assert.Contains("last", Assert.Single(read.Connections).Metadata);
    }

    [Theory]
    [InlineData("""{"connections":[{"id":5,"source":{"nodeId":"a"},"target":{"nodeId":"b"}}]}""")]
    [InlineData("""{"connections":[{"source":{"nodeId":"a","port":true},"target":{"nodeId":"b"}}]}""")]
    [InlineData("""{"connections":[{"source":{"nodeId":"  "},"target":{"nodeId":"b"}}]}""")]
    [InlineData("""{"connections":[{"source":"a","target":{"nodeId":"b"}}]}""")]
    [InlineData("""{"connections":[{"target":{"nodeId":"b"}}]}""")]
    [InlineData("""{"connections":{"source":{"nodeId":"a"}}}""")]
    public void Connections_the_runtime_would_refuse_make_the_reading_incomplete_instead_of_valid(string payload)
    {
        var read = WorkflowVersionStructure.Flatten(Raw(payload), new DefaultActivityStructureService([new FlowHandler()]));
        Assert.True(read.Incomplete);
        Assert.Contains("connections of 'raw'", Assert.Single(read.IncompleteReasons));
        Assert.Empty(read.Connections);
    }

    // A container with the given raw payload (duplicate properties kept) and the children a and b.
    private static ActivityNode Raw(string payload) =>
        FlowHandler.Register(new ActivityNode("raw", "version-flow", [], [], new ActivityNodeStructure("Acme.Flow", "1", JsonDocument.Parse(payload).RootElement.Clone())),
            [Leaf("a", "same"), Leaf("b", "same")]);

    [Fact]
    public async Task A_retained_result_is_admitted_by_definition_lookup_then_owner_then_one_read()
    {
        var owner = new Owner("definition-1");
        using var host = new Host(owner: owner);
        owner.Reader = host.Reader;
        var admitted = (await host.CollectAsync(Reference("definition-1", "v2"))).Attachments;
        var prepared = await host.PrepareAsync(admitted);
        var result = (await host.Tool.ExecuteAsync(Prepared(prepared.Value!))).Value! with
        {
            Access = new(Module, "1", prepared.Value!.Target) { DisclosureSourceId = WorkflowVersionContext.ContextSourceId, InvocationId = "invocation-1" }
        };
        owner.Asked.Clear();
        var (reads, lookups) = (host.Reader.Reads, host.Reader.Lookups);

        var admittedResult = await host.Source.AdmitDisclosureAsync(new(host.Session.Id, [], [result], false));

        Assert.Empty(admittedResult.Diagnostics);
        // The owner was asked after the lookup and before the read; then exactly one read.
        Assert.Equal([("definition-1", reads, lookups + 1)], owner.Asked);
        Assert.Equal((reads + 1, lookups + 1), (host.Reader.Reads, host.Reader.Lookups));
        owner.Readable.Clear();
        Assert.Equal(WorkflowVersionContext.NotFoundCode, Assert.Single((await host.Source.AdmitDisclosureAsync(new(host.Session.Id, [], [result], false))).Diagnostics).Code);
        Assert.Equal(reads + 1, host.Reader.Reads);
    }

    [Fact]
    public async Task During_an_owner_outage_a_version_named_only_by_its_id_answers_not_found_when_unknown_and_unavailable_when_known()
    {
        // The documented caveat of id-only references: without a named definition, an unknown id has no owner to ask.
        using var host = new Host(owner: new Owner(throws: true));
        var unknown = await host.Tool.AuthorizeAsync("actor-1", "tenant-1", WorkflowVersionContext.Target("missing"));
        var known = await host.Tool.AuthorizeAsync("actor-1", "tenant-1", WorkflowVersionContext.Target("v2"));
        Assert.Equal((WorkflowVersionContext.NotFoundCode, false), (Assert.Single(unknown.Violations).Code, unknown.Violations.Single().OwnerUnavailable));
        Assert.Equal((WorkflowDefinitionAuthorizationException.UnavailableCode, true), (Assert.Single(known.Violations).Code, known.Violations.Single().OwnerUnavailable));
        Assert.Equal(0, host.Reader.Reads);
        Assert.Contains("connectionsChanged", WorkflowReadVersionTool.Contract.Description);
    }

    [Fact]
    public async Task A_version_larger_than_the_walk_is_admitted_as_truncated_not_complete()
    {
        using var host = new Host(owner: new Owner("definition-4"));
        var admitted = (await host.CollectAsync(Reference("definition-4", "t1"))).Attachments;
        var content = (JsonElement)Assert.Single(admitted, item => item.ContentType == WorkflowVersionContext.VersionKind).Content!;
        Assert.True(content.GetProperty("activitiesTruncated").GetBoolean());
        Assert.False(content.GetProperty("readingIncomplete").GetBoolean());
        Assert.Equal(WorkflowVersionStructure.WalkLimit, content.GetProperty("activityCount").GetInt32());
    }

    [Fact]
    public async Task A_container_no_installed_handler_reads_makes_the_reading_explicitly_incomplete()
    {
        using var host = new Host(owner: new Owner("definition-3"));
        var admitted = (await host.CollectAsync(Reference("definition-3", "u1"))).Attachments;
        Assert.True(((JsonElement)Assert.Single(admitted, item => item.ContentType == WorkflowVersionContext.VersionKind).Content!).GetProperty("readingIncomplete").GetBoolean());

        var prepared = await host.PrepareAsync(admitted, new Dictionary<string, object?> { ["compareToVersionId"] = "s1" });

        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var data = prepared.Value!.Preview.Data;
        Assert.True(data.GetProperty("version").GetProperty("incomplete").GetBoolean());
        Assert.Contains("opaque", Assert.Single(data.GetProperty("version").GetProperty("incompleteReasons").EnumerateArray()).GetString());
        // The hidden child is neither listed nor reported as removed or added: the comparison says it could not read it.
        Assert.DoesNotContain(data.GetProperty("version").GetProperty("activities").EnumerateArray(), item => item.GetProperty("nodeId").GetString() == "hidden");
        var comparison = data.GetProperty("comparison");
        Assert.True(comparison.GetProperty("incomplete").GetBoolean());
        Assert.Contains("incomplete", comparison.GetProperty("note").GetString());
    }

    [Fact]
    public async Task The_context_lists_the_newest_versions_and_says_when_older_ones_are_left_out()
    {
        using var host = new Host();
        host.Reader.ManyVersions = true;
        var content = (JsonElement)Assert.Single((await host.CollectAsync(Reference("definition-1", "v2"))).Attachments,
            item => item.ContentType == WorkflowVersionContext.VersionKind).Content!;
        var listed = content.GetProperty("versions").EnumerateArray().Select(item => item.GetProperty("version").GetString()).ToArray();
        Assert.Equal(WorkflowVersionContext.VersionListLimit, listed.Length);
        Assert.Equal(("11.0.0", "60.0.0"), (listed[0], listed[^1]));
        Assert.True(content.GetProperty("versionsTruncated").GetBoolean());
    }

    [Fact]
    public async Task During_an_owner_outage_an_unknown_and_an_existing_version_answer_alike_and_nothing_is_read()
    {
        using var host = new Host(owner: new Owner(throws: true));
        var unknown = Assert.Single((await host.CollectAsync(Reference("definition-1", "missing"))).Diagnostics);
        var existing = Assert.Single((await host.CollectAsync(Reference("definition-1", "v2"))).Diagnostics);
        Assert.Equal((unknown.Code, unknown.Message, unknown.OwnerUnavailable), (existing.Code, existing.Message, existing.OwnerUnavailable));
        Assert.Equal(WorkflowDefinitionAuthorizationException.UnavailableCode, existing.Code);
        Assert.Equal(0, host.Reader.Reads);
    }

    [Fact]
    public async Task Zero_or_several_owning_authorizers_are_a_composition_fault_never_a_not_found()
    {
        using var host = new Host(configure: services => services.AddSingleton<IWorkflowDefinitionAuthorizer>(new Owner("definition-1")));
        var refusal = Assert.Single((await host.CollectAsync(Reference("definition-1", "v2"))).Diagnostics);
        Assert.Equal(("workflow.authority.composition_unavailable", true), (refusal.Code, refusal.OwnerUnavailable));
        Assert.NotEqual(WorkflowVersionContext.NotFoundCode, refusal.Code);
        Assert.Equal(0, host.Reader.Reads);
    }

    [Theory]
    [InlineData("no-context", "agent.workflow.version_context_required")]
    [InlineData("caller-copy", "agent.workflow.version_context_required")]
    [InlineData("other-version", "agent.workflow.version_context_required")]
    [InlineData("compare-self", "agent.workflow.version_arguments_invalid")]
    [InlineData("unknown-argument", "agent.workflow.version_arguments_invalid")]
    [InlineData("compare-other-definition", WorkflowVersionContext.NotFoundCode)]
    [InlineData("compare-unknown", WorkflowVersionContext.NotFoundCode)]
    public async Task The_tool_reads_only_versions_of_the_admitted_context_and_its_definition(string control, string code)
    {
        using var host = new Host();
        var admitted = (await host.CollectAsync(Reference("definition-1", "v2"))).Attachments;
        IReadOnlyCollection<AgentContextAttachment> context = control switch
        {
            "no-context" => [],
            "caller-copy" => [Reference("definition-1", "v2")],
            _ => admitted
        };
        var arguments = control switch
        {
            "other-version" => new Dictionary<string, object?> { ["versionId"] = "v1" },
            "compare-self" => new Dictionary<string, object?> { ["compareToVersionId"] = "v2" },
            "unknown-argument" => new Dictionary<string, object?> { ["definitionId"] = "definition-1" },
            "compare-other-definition" => new Dictionary<string, object?> { ["compareToVersionId"] = "other-v1" },
            "compare-unknown" => new Dictionary<string, object?> { ["compareToVersionId"] = "missing" },
            _ => null
        };

        var prepared = await host.PrepareAsync(context, arguments);

        Assert.False(prepared.Succeeded);
        Assert.Equal(code, prepared.Error!.Code);
        if (code == WorkflowVersionContext.NotFoundCode) Assert.Equal(WorkflowVersionContext.NotFoundMessage, prepared.Error.Message);
    }

    [Fact]
    public async Task Tool_authority_for_a_version_is_its_definitions_owner_read()
    {
        using var host = new Host(owner: new Owner("definition-1"));
        // Authority is the definition owner's decision: the version's definition is looked up, the owner asked, and nothing read.
        Assert.True((await host.Tool.AuthorizeAsync("actor-1", "tenant-1", WorkflowVersionContext.Target("v2"))).Allowed);
        Assert.Equal(WorkflowVersionContext.NotFoundCode, Assert.Single((await host.Tool.AuthorizeAsync("actor-1", "tenant-1",
            WorkflowVersionContext.Target("other-v1"))).Violations).Code);
        Assert.Equal(WorkflowVersionContext.NotFoundCode, Assert.Single((await host.Tool.AuthorizeAsync("actor-1", "tenant-1",
            WorkflowVersionContext.Target("missing"))).Violations).Code);
        Assert.Equal(0, host.Reader.Reads);
        Assert.Equal(3, host.Reader.Lookups);
        Assert.Equal("agent.workflow.resource_required", Assert.Single((await host.Tool.AuthorizeAsync("actor-1", "tenant-1",
            new(Module, WorkflowAgentSnapshot.ResourceType, "definition-1", "r1"))).Violations).Code);
    }

    [Fact]
    public async Task Retained_context_and_results_are_read_again_and_withheld_once_the_owner_withholds_them()
    {
        var owner = new Owner("definition-1");
        using var host = new Host(owner: owner);
        var admitted = (await host.CollectAsync(Reference("definition-1", "v2"))).Attachments
            .Select(item => item with { ContributorId = WorkflowVersionContext.ContextSourceId }).ToArray();
        var prepared = await host.PrepareAsync(admitted);
        var result = (await host.Tool.ExecuteAsync(Prepared(prepared.Value!))).Value! with
        {
            Access = new(Module, "1", prepared.Value!.Target) { DisclosureSourceId = WorkflowVersionContext.ContextSourceId, InvocationId = "invocation-1" }
        };

        var current = await host.Source.AdmitDisclosureAsync(new(host.Session.Id, admitted, [result], false));
        Assert.Empty(current.Diagnostics);
        Assert.Equal(admitted.Select(item => JsonSerializer.Serialize(item.Content)), current.Attachments.Select(item => JsonSerializer.Serialize(item.Content)));

        owner.Readable.Clear();
        var withheld = await host.Source.AdmitDisclosureAsync(new(host.Session.Id, admitted, [], false));
        Assert.Equal(WorkflowVersionContext.NotFoundCode, Assert.Single(withheld.Diagnostics).Code);
        var withheldResult = await host.Source.AdmitDisclosureAsync(new(host.Session.Id, [], [result], false));
        Assert.Equal(WorkflowVersionContext.NotFoundCode, Assert.Single(withheldResult.Diagnostics).Code);

        var forged = await host.Source.AdmitDisclosureAsync(new(host.Session.Id, [Reference("definition-1", "v2")], [], false));
        Assert.Equal("agent.workflow.disclosure_invalid", Assert.Single(forged.Diagnostics).Code);
    }

    [Fact]
    public void The_feature_registers_the_internal_sealed_version_contributions_and_no_fallback_reader()
    {
        var services = new ServiceCollection();
        new FoundationWorkflowsAgentFeature().ConfigureServices(services);
        Assert.Single(services, item => item.ServiceType == typeof(IAgentContextProvider) && item.ImplementationType == typeof(WorkflowVersionContextProvider));
        Assert.Single(services, item => item.ServiceType == typeof(IAgentTool) && item.ImplementationType == typeof(WorkflowReadVersionTool));
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IWorkflowVersionReader));
        foreach (var type in new[] { typeof(WorkflowVersionContextProvider), typeof(WorkflowReadVersionTool) })
            Assert.True(type.IsSealed && !type.IsPublic, type.Name);
        Assert.Equal(["workflows/design:read"], WorkflowReadVersionTool.Contract.RequiredPermissions);
        Assert.Equal(AgentToolMutability.ReadOnly, WorkflowReadVersionTool.Contract.Mutability);
    }

    // ---- Fixtures --------------------------------------------------------------------------------------------------------

    internal static AgentContextAttachment Reference(string definitionId, string versionId, object? content = null) =>
        new($"workflows.version:{versionId}", "workflow-version", versionId, "Version", WorkflowVersionContext.VersionKind, AgentContextSensitivity.Internal, "selection",
            "Workflow version", JsonSerializer.SerializeToElement(content ?? new { definitionId, versionId }), new Dictionary<string, string>())
        { ModuleId = Module, SchemaVersion = "1", Target = new(Module, "workflow-version", versionId, versionId), Origin = AgentContextOrigin.ClientSnapshot };

    private static AgentPreparedToolInvocation Prepared(AgentToolPreparation preparation) =>
        new("invocation-1", "session-1", "turn-1", "call-1", WorkflowReadVersionTool.ToolName, "1", Module, "actor-1", "tenant-1", preparation.Target,
            preparation.Arguments, "digest", preparation.Risk, preparation.Preview, AgentExecutionLocation.Server, false, null, DateTimeOffset.UtcNow.AddMinutes(5));

    internal sealed class Host : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;

        public Host(bool hostDenied = false, Owner? owner = null, Action<IServiceCollection>? configure = null)
        {
            root = AgentTestServices.Create(services =>
            {
                new FoundationWorkflowsAgentFeature().ConfigureServices(services);
                services.AddSingleton<IWorkflowDefinitionAuthorizer>(owner ?? new Owner("definition-1", "definition-2"));
                services.AddSingleton<IWorkflowVersionReader>(Reader);
                services.AddScoped<IActivityStructureService>(_ => new DefaultActivityStructureService([new FlowHandler()]));
                if (hostDenied) services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer, DenyDesignRead>());
                configure?.Invoke(services);
            });
            scope = root.CreateScope();
            Session = scope.ServiceProvider.GetRequiredService<IAgentSessionService>().CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test-provider",
                "explain", null, AgentPolicy.Default, new Dictionary<string, string>())).GetAwaiter().GetResult();
        }

        public FixtureReader Reader { get; } = new();
        public AgentSession Session { get; }
        public IAgentContextProvider Source => scope.ServiceProvider.GetServices<IAgentContextProvider>().OfType<WorkflowVersionContextProvider>().Single();
        public WorkflowReadVersionTool Tool => scope.ServiceProvider.GetServices<IAgentTool>().OfType<WorkflowReadVersionTool>().Single();

        public async Task<AgentContextContribution> CollectAsync(params AgentContextAttachment[] callers) =>
            await Source.CollectAsync(new(Session.Id, "workflow-version", new Dictionary<string, string>()) { CallerAttachments = callers });

        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(IReadOnlyCollection<AgentContextAttachment> context, IReadOnlyDictionary<string, object?>? arguments = null) =>
            Tool.PrepareAsync(new("call-1", WorkflowReadVersionTool.ToolName, Session.Id, Session.ActorId, arguments ?? new Dictionary<string, object?>(), context)
                { TenantId = Session.TenantId });

        public void Dispose()
        {
            scope.Dispose();
            root.Dispose();
        }
    }

    internal sealed class Owner(params string[] readable) : IWorkflowDefinitionAuthorizer
    {
        private readonly bool throws;
        public Owner(bool throws) : this() => this.throws = throws;
        public HashSet<string> Readable { get; } = [.. readable];
        /// <summary>Each read decision asked, in order, with the reader's read and lookup counts at that moment.</summary>
        public List<(string DefinitionId, int Reads, int Lookups)> Asked { get; } = [];
        public FixtureReader? Reader { get; set; }
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            Asked.Add((definitionId, Reader?.Reads ?? -1, Reader?.Lookups ?? -1));
            return throws ? throw new WorkflowDefinitionAuthorizationException("definition-1", "read", new InvalidOperationException("store down"))
                : ValueTask.FromResult(Readable.Contains(definitionId));
        }
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class DenyDesignRead : IAgentToolAuthorizer
    {
        public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId, AgentResourceTarget? target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(tool.RequiredPermissions.Contains("workflows/design:read")
                ? new AgentPolicyDecision(false, [new("agent.tool.permission_denied", "Denied.")]) : new AgentPolicyDecision(true, []));
    }

    /// <summary>
    /// Three versions of definition-1 and one of definition-2. v1: flow > { a, b } with a -> b. v2: flow > { a (its text
    /// input changed), nested > { inner } } with a -> nested. v3: flow > { a }.
    /// </summary>
    internal sealed class FixtureReader : IWorkflowVersionReader
    {
        public int Reads { get; private set; }
        /// <summary>Lists 60 versions of definition-1 (oldest first, v2 the newest), more than the context lists.</summary>
        public bool ManyVersions { get; set; }

        private static readonly Dictionary<string, WorkflowVersionDetails> Versions = new()
        {
            ["v1"] = Version("v1", "definition-1", "1.0.0", Flow("flow", [Leaf("a", "value-1"), Leaf("b", "value-b")], ("a", "b"))),
            ["v2"] = Version("v2", "definition-1", "2.0.0", Flow("flow", [Leaf("a", "value-2"), Flow("nested", [Leaf("inner", "value-inner")])], ("a", "nested"))),
            ["v3"] = Version("v3", "definition-1", "3.0.0", Flow("flow", [Leaf("a", "value-3")])),
            ["other-v1"] = Version("other-v1", "definition-2", "1.0.0", Flow("flow", [])),
            // definition-3: structural changes that leave every activity's version and inputs alone.
            ["s1"] = Version("s1", "definition-3", "1.0.0", Flow("flow", [Leaf("a", "same"), Leaf("b", "same")], "a", "Acme.Flow")),
            ["s2"] = Version("s2", "definition-3", "2.0.0", Flow("flow", [Leaf("b", "same"), Leaf("a", "same") with
            {
                ExecutionPlacement = ActivityExecutionPlacement.Background,
                Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Finish)
            }], "b", "Acme.Flow")),
            // definition-5: an ordered container reordered, and per-element flowchart metadata.
            ["q1"] = Version("q1", "definition-5", "1.0.0", Container("sequence", [Leaf("a", "same"), Leaf("b", "same")], new { })),
            ["q2"] = Version("q2", "definition-5", "2.0.0", Container("sequence", [Leaf("b", "same"), Leaf("a", "same")], new { })),
            ["m1"] = Version("m1", "definition-5", "3.0.0", Container("flow", [Leaf("a", "same"), Leaf("b", "same")], new
            {
                connections = new[] { new { id = "ab", source = new { nodeId = "a", port = "Done" }, target = new { nodeId = "b" } } },
                connectionMetadata = new Dictionary<string, object> { ["ab"] = new { label = "first" } },
                nodeMetadata = new Dictionary<string, object> { ["b"] = new { policyKind = "WaitAny" } }
            })),
            ["m2"] = Version("m2", "definition-5", "4.0.0", Container("flow", [Leaf("a", "same"), Leaf("b", "same"), Leaf("c", "same")], new
            {
                connections = new[]
                {
                    new { id = "ab", source = new { nodeId = "a", port = "Done" }, target = new { nodeId = "b" } },
                    new { id = "bc", source = new { nodeId = "b", port = "Done" }, target = new { nodeId = "c" } }
                },
                connectionMetadata = new Dictionary<string, object> { ["ab"] = new { label = "renamed" }, ["bc"] = new { label = "second" } },
                nodeMetadata = new Dictionary<string, object> { ["b"] = new { policyKind = "WaitAll" }, ["c"] = new { policyKind = "WaitAny" } }
            })),
            // An id-less connection's default flag changes (metadata keyed as the flowchart keys it), and two connections share
            // endpoints under different ids, one of which goes away; an unclaimed node metadata entry changes too.
            ["i1"] = Version("i1", "definition-5", "5.0.0", Container("flow", [Leaf("a", "same"), Leaf("b", "same")], new
            {
                connections = new object[]
                {
                    new { source = new { nodeId = "a", port = "Done" }, target = new { nodeId = "b" } },
                    new { id = "twin-1", source = new { nodeId = "b", port = "Done" }, target = new { nodeId = "a" } },
                    new { id = "twin-2", source = new { nodeId = "b", port = "Done" }, target = new { nodeId = "a" } }
                },
                connectionMetadata = new Dictionary<string, object> { ["a:Done->b"] = new { isDefault = false } },
                nodeMetadata = new Dictionary<string, object> { ["not-a-child"] = new { policyKind = "WaitAny" } }
            })),
            ["i2"] = Version("i2", "definition-5", "6.0.0", Container("flow", [Leaf("a", "same"), Leaf("b", "same")], new
            {
                connections = new object[]
                {
                    new { source = new { nodeId = "a", port = "Done" }, target = new { nodeId = "b" } },
                    new { id = "twin-1", source = new { nodeId = "b", port = "Done" }, target = new { nodeId = "a" } }
                },
                connectionMetadata = new Dictionary<string, object> { ["a:Done->b"] = new { isDefault = true } },
                nodeMetadata = new Dictionary<string, object> { ["not-a-child"] = new { policyKind = "WaitAll" } }
            })),
            // The same connections written the ways the runtime accepts: an omitted, a blank and a padded port are Done, a padded
            // id is trimmed and a blank one is none, and property names in any case. Metadata keyed by the normalized key attaches.
            ["p1"] = Version("p1", "definition-5", "7.0.0", Container("flow", [Leaf("a", "same"), Leaf("b", "same")], new
            {
                connections = new object[]
                {
                    new { source = new { nodeId = "a", port = "Done" }, target = new { nodeId = "b", port = "Done" } },
                    new { id = "twin", source = new { nodeId = "b", port = "Done" }, target = new { nodeId = "a" } }
                },
                connectionMetadata = new Dictionary<string, object> { ["a:Done->b"] = new { isDefault = false } }
            })),
            ["p2"] = Version("p2", "definition-5", "8.0.0", Container("flow", [Leaf("a", "same"), Leaf("b", "same")], new Dictionary<string, object>
            {
                ["Connections"] = new object[]
                {
                    new Dictionary<string, object> { ["Id"] = "  ", ["Source"] = new Dictionary<string, object> { ["NodeId"] = "a" }, ["Target"] = new Dictionary<string, object> { ["nodeId"] = "b", ["Port"] = "   " } },
                    new Dictionary<string, object> { ["id"] = " twin ", ["source"] = new Dictionary<string, object> { ["nodeId"] = "b", ["port"] = " Done " }, ["target"] = new Dictionary<string, object> { ["nodeId"] = "a" } }
                },
                ["ConnectionMetadata"] = new Dictionary<string, object> { ["a:Done->b"] = new { isDefault = true } }
            })),
            // A container no installed handler reads.
            ["u1"] = Version("u1", "definition-3", "3.0.0", Flow("flow", [Flow("opaque", [Leaf("hidden", "x")], null, "Acme.Unknown")], null, "Acme.Flow")),
            // More activities than the walk reads.
            ["t1"] = Version("t1", "definition-4", "1.0.0", Flow("flow",
                Enumerable.Range(0, WorkflowVersionStructure.WalkLimit + 1).Select(index => Leaf($"n{index}", "x")).ToArray(), null, "Acme.Flow"))
        };

        public int Lookups { get; private set; }

        public ValueTask<string?> FindDefinitionIdAsync(string versionId, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return ValueTask.FromResult(Versions.GetValueOrDefault(versionId)?.DefinitionId);
        }

        public ValueTask<WorkflowVersionDetails?> ReadAsync(string versionId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(Versions.GetValueOrDefault(versionId));
        }

        public ValueTask<IReadOnlyList<WorkflowDefinitionVersionSummary>> ListAsync(string definitionId, CancellationToken cancellationToken = default) =>
            definitionId == "definition-1" && ManyVersions
                ? ValueTask.FromResult<IReadOnlyList<WorkflowDefinitionVersionSummary>>(Enumerable.Range(1, 60)
                    .Select(index => new WorkflowDefinitionVersionSummary(index == 60 ? "v2" : $"old-{index}", $"{index}.0.0", DateTimeOffset.UnixEpoch.AddDays(index))).ToArray())
                : ValueTask.FromResult<IReadOnlyList<WorkflowDefinitionVersionSummary>>(Versions.Values.Where(item => item.DefinitionId == definitionId)
                .Select(item => new WorkflowDefinitionVersionSummary(item.VersionId, item.Version, new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero))).ToArray());

        private static WorkflowVersionDetails Version(string id, string definitionId, string version, ActivityNode root) =>
            new(id, definitionId, version, definitionId == "definition-1" ? "Onboarding" : "Other", new([], root, [], [], null));
    }

    internal static ActivityNode Leaf(string id, string text) =>
        new(id, "version-write-line", [new ArgumentState("text", new ArgumentValue(JsonSerializer.SerializeToElement(text), "Literal"), null, null, null, null)], []);

    internal static ActivityNode Flow(string id, ActivityNode[] children, params (string Source, string Target)[] connections) =>
        Flow(id, children, null, "Acme.Flow", connections);

    /// <summary>A container whose payload embeds its children as activity nodes, plus the given payload fields.</summary>
    internal static ActivityNode Container(string id, ActivityNode[] children, object fields)
    {
        var payload = JsonSerializer.SerializeToNode(fields, JsonOptions)!.AsObject();
        payload["activities"] = JsonSerializer.SerializeToNode(children.Select(child => new { nodeId = child.NodeId, activityVersionId = child.ActivityVersionId }).ToArray());
        var node = new ActivityNode(id, "version-flow", [], [], new ActivityNodeStructure("Acme.Flow", "1", JsonSerializer.SerializeToElement(payload)));
        return FlowHandler.Register(node, children);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A container whose payload embeds its children as activity nodes (as real structures do), its connections and its start node.</summary>
    internal static ActivityNode Flow(string id, ActivityNode[] children, string? start, string kind, params (string Source, string Target)[] connections)
    {
        var node = new ActivityNode(id, "version-flow", [], [], new ActivityNodeStructure(kind, "1", JsonSerializer.SerializeToElement(new
        {
            activities = children.Select(child => new { nodeId = child.NodeId, activityVersionId = child.ActivityVersionId }).ToArray(),
            connections = connections.Select(item => new { source = new { nodeId = item.Source, port = "Done" }, target = new { nodeId = item.Target } }).ToArray(),
            startNodeId = start
        })));
        return FlowHandler.Register(node, children);
    }

    /// <summary>A test-owned structure handler: the children live beside the node, and the payload holds its connections.</summary>
    internal sealed class FlowHandler : IActivityStructureHandler
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ActivityNode, ActivityNode[]> Children = new();
        public string Kind => "Acme.Flow";
        public string SchemaVersion => "1";

        public static ActivityNode Register(ActivityNode node, ActivityNode[] children)
        {
            Children.AddOrUpdate(node, children);
            return node;
        }

        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            [new ActivityChildProjection("Activities", Children.TryGetValue(activity, out var children) ? children : [])];

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => throw new NotSupportedException();
        public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
    }
}
