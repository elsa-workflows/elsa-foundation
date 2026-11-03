using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows;
using Elsa.Agent.Workflows.Services;
using Elsa.Agent.Workflows.Tools;
using Elsa.Expressions.Core.Models;
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
        Assert.Equal(new AgentResourceTarget(Module, "workflow-version", "v2", "v2"), admitted.Target);
        var content = (JsonElement)admitted.Content!;
        Assert.Equal(("definition-1", "Onboarding", "v2", "2.0.0", 4), (content.GetProperty("definitionId").GetString(), content.GetProperty("definitionName").GetString(),
            content.GetProperty("versionId").GetString(), content.GetProperty("version").GetString(), content.GetProperty("activityCount").GetInt32()));
        Assert.Equal(["v1", "v2", "v3"], content.GetProperty("versions").EnumerateArray().Select(item => item.GetProperty("versionId").GetString()));
        Assert.False(content.GetProperty("versionsTruncated").GetBoolean());
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
        Assert.Equal(new AgentResourceTarget(Module, "workflow-version", "v2", "v2"), prepared.Value.Target);
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
        Assert.Equal(("flow", "a", "Done", "nested", (string?)null), (connection.GetProperty("container").GetString(), connection.GetProperty("source").GetString(),
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
        Assert.True((await host.Tool.AuthorizeAsync("actor-1", "tenant-1", WorkflowVersionContext.Target("v2"))).Allowed);
        Assert.Equal(WorkflowVersionContext.NotFoundCode, Assert.Single((await host.Tool.AuthorizeAsync("actor-1", "tenant-1", WorkflowVersionContext.Target("other-v1"))).Violations).Code);
        Assert.Equal(WorkflowVersionContext.NotFoundCode, Assert.Single((await host.Tool.AuthorizeAsync("actor-1", "tenant-1", WorkflowVersionContext.Target("missing"))).Violations).Code);
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
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) =>
            throws ? throw new WorkflowDefinitionAuthorizationException("definition-1", "read", new InvalidOperationException("store down")) : ValueTask.FromResult(Readable.Contains(definitionId));
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

        private static readonly Dictionary<string, WorkflowVersionDetails> Versions = new()
        {
            ["v1"] = Version("v1", "definition-1", "1.0.0", Flow("flow", [Leaf("a", "value-1"), Leaf("b", "value-b")], ("a", "b"))),
            ["v2"] = Version("v2", "definition-1", "2.0.0", Flow("flow", [Leaf("a", "value-2"), Flow("nested", [Leaf("inner", "value-inner")])], ("a", "nested"))),
            ["v3"] = Version("v3", "definition-1", "3.0.0", Flow("flow", [Leaf("a", "value-3")])),
            ["other-v1"] = Version("other-v1", "definition-2", "1.0.0", Flow("flow", []))
        };

        public ValueTask<WorkflowVersionDetails?> ReadAsync(string versionId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(Versions.GetValueOrDefault(versionId));
        }

        public ValueTask<IReadOnlyList<WorkflowDefinitionVersionSummary>> ListAsync(string definitionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<WorkflowDefinitionVersionSummary>>(Versions.Values.Where(item => item.DefinitionId == definitionId)
                .Select(item => new WorkflowDefinitionVersionSummary(item.VersionId, item.Version, new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero))).ToArray());

        private static WorkflowVersionDetails Version(string id, string definitionId, string version, ActivityNode root) =>
            new(id, definitionId, version, definitionId == "definition-1" ? "Onboarding" : "Other", new([], root, [], [], null));
    }

    internal static ActivityNode Leaf(string id, string text) =>
        new(id, "version-write-line", [new ArgumentState("text", new ArgumentValue(JsonSerializer.SerializeToElement(text), "Literal"), null, null, null, null)], []);

    internal static ActivityNode Flow(string id, ActivityNode[] children, params (string Source, string Target)[] connections)
    {
        var node = new ActivityNode(id, "version-flow", [], [], new ActivityNodeStructure("Acme.Flow", "1", JsonSerializer.SerializeToElement(new
        {
            children = children.Select(child => child.NodeId).ToArray(),
            connections = connections.Select(item => new { source = new { nodeId = item.Source, port = "Done" }, target = new { nodeId = item.Target } }).ToArray()
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
