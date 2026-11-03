using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Agent.Workflows;
using Elsa.Agent.Workflows.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

/// <summary>
/// Spec 1193 story 4 (FR-003, FR-004, FR-031): the workflow context's catalog and permissions are the owning services'
/// current answers, read again for every turn, and tool availability follows them.
/// </summary>
public sealed class WorkflowTruthSeamTests
{
    [Fact]
    public async Task Collection_discloses_the_owning_catalog_and_the_actors_manage_permission_on_the_exact_target()
    {
        using var host = Host.Create();
        var contribution = await host.CollectAsync();

        Assert.Empty(contribution.Diagnostics);
        var catalog = Single(contribution, WorkflowActivityCatalog.ContentType);
        Assert.Equal(AgentContextOrigin.AuthoritativeRead, catalog.Origin);
        Assert.Null(catalog.Target);
        Assert.Empty(catalog.AgentScopes);
        var content = Content(catalog);
        Assert.Equal("available", content.GetProperty("status").GetString());
        Assert.False(content.GetProperty("truncated").GetBoolean());
        var entries = content.GetProperty("entries").EnumerateArray().ToArray();
        // The owner's entries, order, availability and versions; an excluded activity stays listed as unavailable.
        Assert.Equal(host.Catalog.Entries.Select(entry => entry.ActivityVersionId), entries.Select(entry => entry.GetProperty("activityVersionId").GetString()));
        Assert.Equal(host.Catalog.Entries.Select(entry => entry.Available), entries.Select(entry => entry.GetProperty("available").GetBoolean()));
        Assert.Equal(host.Catalog.Entries.Select(entry => entry.Version), entries.Select(entry => entry.GetProperty("version").GetString()));
        Assert.True(entries[0].GetProperty("inputs").EnumerateArray().Single(input => input.GetProperty("name").GetString() == "ApiKey")
            .GetProperty("isCredential").GetBoolean());

        var permissions = Single(contribution, DefaultWorkflowAgentContextProvider.PermissionsContentType);
        Assert.Equal(WorkflowAgentTests.Snapshot().Target, permissions.Target);
        Assert.Equal(AgentContextOrigin.AuthoritativeRead, permissions.Origin);
        Assert.Empty(permissions.AgentScopes);
        var granted = Content(permissions);
        Assert.Equal("allowed", granted.GetProperty("read").GetString());
        Assert.Equal("allowed", granted.GetProperty("manage").GetString());
        Assert.Equal(["workflow_read_graph", "workflow_set_designer_position"], Strings(granted, "tools"));
        Assert.Equal(["workflow.explain", "workflow.propose-change"], Strings(granted, "capabilities"));
    }

    [Theory]
    [InlineData(OwnerManage.Allowed)]
    [InlineData(OwnerManage.Denied)]
    [InlineData(OwnerManage.Unavailable)]
    public async Task Tool_availability_follows_the_reported_permissions(OwnerManage manage)
    {
        using var host = Host.Create();
        host.Owner.Manage = manage;
        var contribution = await host.CollectAsync();

        var reported = Content(Single(contribution, DefaultWorkflowAgentContextProvider.PermissionsContentType));
        Assert.Equal(manage switch { OwnerManage.Allowed => "allowed", OwnerManage.Denied => "denied", _ => "unavailable" },
            reported.GetProperty("manage").GetString());
        // The engine's own advertisement decision (host authority plus the tool's owner authority) for the same target.
        var advertised = new List<string>();
        var target = WorkflowAgentTests.Snapshot().Target;
        foreach (var tool in (await host.Services.GetRequiredService<IAgentToolRegistry>().ListAsync())
                     .Where(tool => tool.Descriptor.ModuleId == WorkflowAgentSnapshot.ModuleId))
            if ((await AgentToolAccess.AuthorizeAsync(tool, host.Services.GetRequiredService<IAgentToolAuthorizer>(), "actor-1", "tenant-1", target, CancellationToken.None)).Allowed)
                advertised.Add(tool.Descriptor.Name);
        Assert.Equal(Strings(reported, "tools").Order(), advertised.Order());
        // A read-only actor still gets explanation: read stays allowed and the context is admitted.
        Assert.Equal("allowed", reported.GetProperty("read").GetString());
        Assert.Contains(contribution.Attachments, item => item.ContentType == "workflow.definition");
    }

    [Fact]
    public async Task A_missing_catalog_owner_is_reported_unavailable_by_name_without_blocking_explanation()
    {
        using var host = Host.Create(installCatalog: false);
        var collected = await host.CollectThroughEngineAsync();

        Assert.True(collected.Succeeded, collected.Error?.Message);
        var diagnostic = Assert.Single(collected.Value!.Diagnostics, item => item.Code == WorkflowActivityCatalog.UnavailableDiagnostic);
        Assert.False(diagnostic.Required);
        Assert.Contains(nameof(IActivityAuthoringCatalog), diagnostic.Message);
        var content = Content(Single(collected.Value, WorkflowActivityCatalog.ContentType));
        Assert.Equal("unavailable", content.GetProperty("status").GetString());
        Assert.Empty(content.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task A_failing_catalog_owner_is_unavailable_and_discloses_no_cause()
    {
        using var host = Host.Create();
        host.Catalog.Fail = true;
        var contribution = await host.CollectAsync();

        var diagnostic = Assert.Single(contribution.Diagnostics);
        Assert.Equal(WorkflowActivityCatalog.UnavailableDiagnostic, diagnostic.Code);
        Assert.False(diagnostic.Required);
        Assert.DoesNotContain(FixtureActivityCatalog.FailureDetail, JsonSerializer.Serialize(contribution));
        Assert.Equal("unavailable", Content(Single(contribution, WorkflowActivityCatalog.ContentType)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_actor_without_catalog_read_sees_a_denied_catalog_with_no_entries()
    {
        using var host = Host.Create();
        host.Catalog.Allowed = false;
        var contribution = await host.CollectAsync();

        Assert.Empty(contribution.Diagnostics);
        var content = Content(Single(contribution, WorkflowActivityCatalog.ContentType));
        Assert.Equal("denied", content.GetProperty("status").GetString());
        Assert.Empty(content.GetProperty("entries").EnumerateArray());
    }

    [Theory]
    [InlineData("workflow.catalog")]
    [InlineData("workflow.permissions")]
    public async Task A_caller_cannot_supply_a_server_read(string kind)
    {
        using var host = Host.Create();
        var forged = WorkflowAgentTests.Snapshot() with
        {
            Id = "forged", ContentType = kind,
            Content = JsonSerializer.SerializeToElement(new { manage = "allowed", tools = new[] { "workflow_set_designer_position" } })
        };
        var contribution = await host.CollectAsync(forged);

        Assert.Equal("agent.workflow.context_unsupported", Assert.Single(contribution.Diagnostics).Code);
        Assert.DoesNotContain(contribution.Attachments, item => item.Id == "forged");
    }

    [Fact]
    public async Task Each_turn_reads_the_catalog_and_permissions_again_instead_of_trusting_the_retained_ones()
    {
        using var host = Host.Create();
        var accepted = await host.CollectAsync();
        var unchanged = await host.AdmitAsync(accepted.Attachments);
        // An unchanged catalog and permission set is the identical projection, so it adds no new disclosure snapshot.
        Assert.Equal(Json(Single(accepted, WorkflowActivityCatalog.ContentType)), Json(Single(unchanged, WorkflowActivityCatalog.ContentType)));
        Assert.Equal(Json(Single(accepted, DefaultWorkflowAgentContextProvider.PermissionsContentType)),
            Json(Single(unchanged, DefaultWorkflowAgentContextProvider.PermissionsContentType)));
        var reads = host.Catalog.ListCalls;

        host.Catalog.Entries = [.. host.Catalog.Entries.Take(1)];
        host.Owner.Manage = OwnerManage.Denied;
        var changed = await host.AdmitAsync(accepted.Attachments);

        Assert.Equal(reads + 1, host.Catalog.ListCalls);
        var catalog = Single(changed, WorkflowActivityCatalog.ContentType);
        Assert.Equal(WorkflowActivityCatalog.AttachmentId, catalog.Id);
        Assert.Single(Content(catalog).GetProperty("entries").EnumerateArray());
        var permissions = Single(changed, DefaultWorkflowAgentContextProvider.PermissionsContentType);
        Assert.Equal(Single(accepted, DefaultWorkflowAgentContextProvider.PermissionsContentType).Id, permissions.Id);
        Assert.Equal("denied", Content(permissions).GetProperty("manage").GetString());
        Assert.Equal(["workflow_read_graph"], Strings(Content(permissions), "tools"));
    }

    [Theory]
    [InlineData("workflow.catalog")]
    [InlineData("workflow.permissions")]
    public async Task A_retained_server_read_that_this_module_did_not_produce_is_refused(string kind)
    {
        using var host = Host.Create();
        var accepted = await host.CollectAsync();
        var forged = accepted.Attachments.Select(item => item.ContentType == kind ? item with { Origin = AgentContextOrigin.ClientSnapshot } : item).ToArray();

        var admission = await host.AdmitAsync(forged);

        Assert.Empty(admission.Attachments);
        Assert.Equal("agent.workflow.disclosure_invalid", Assert.Single(admission.Diagnostics).Code);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "agent.workflow.permission_denied")]
    [InlineData(null, ActivityAuthoringCatalogUnavailableException.UnavailableCode)]
    public async Task A_historical_catalog_stays_disclosable_only_while_the_actor_may_read_the_catalog(bool? canRead, string? refusal)
    {
        using var host = Host.Create();
        var accepted = await host.CollectAsync();
        var reads = host.Catalog.ListCalls;
        host.Catalog.Allowed = canRead == true;
        host.Catalog.Fail = canRead is null;

        var admission = await host.AdmitAsync(accepted.Attachments, historical: true);

        // History is authorized by the gate alone; it never re-reads entries.
        Assert.Equal(reads, host.Catalog.ListCalls);
        if (refusal is null)
        {
            Assert.Empty(admission.Diagnostics);
            Assert.Equal(Json(Single(accepted, WorkflowActivityCatalog.ContentType)), Json(Single(admission, WorkflowActivityCatalog.ContentType)));
            return;
        }
        Assert.Empty(admission.Attachments);
        var diagnostic = Assert.Single(admission.Diagnostics);
        Assert.Equal(refusal, diagnostic.Code);
        Assert.True(diagnostic.Required);
        Assert.Equal(canRead is null, diagnostic.OwnerUnavailable);
    }

    [Fact]
    public async Task A_large_catalog_is_bounded_and_says_it_was_truncated()
    {
        using var host = Host.Create();
        host.Catalog.Entries = Enumerable.Range(0, 2000).Select(index => FixtureActivityCatalog.Entry($"version-{index:0000}", true)).ToArray();

        var collected = await host.CollectThroughEngineAsync();

        Assert.True(collected.Succeeded, collected.Error?.Message);
        var catalog = Single(collected.Value!, WorkflowActivityCatalog.ContentType);
        var content = Content(catalog);
        Assert.True(content.GetProperty("truncated").GetBoolean());
        Assert.Equal(2000, content.GetProperty("totalCount").GetInt32());
        var included = content.GetProperty("includedCount").GetInt32();
        Assert.InRange(included, 1, 1999);
        Assert.Equal(included, content.GetProperty("entries").GetArrayLength());
        Assert.Equal("version-0000", content.GetProperty("entries")[0].GetProperty("activityVersionId").GetString());
        Assert.True(JsonSerializer.Serialize(catalog.Content).Length <= 32768);
        Assert.Contains("did not fit", catalog.Summary);
    }

    private static AgentContextAttachment Single(AgentContextContribution contribution, string kind)
        => Assert.Single(contribution.Attachments, item => item.ContentType == kind);
    private static JsonElement Content(AgentContextAttachment attachment) => Assert.IsType<JsonElement>(attachment.Content);
    private static string Json(AgentContextAttachment attachment) => JsonSerializer.Serialize(attachment with { ContributorId = null });
    private static string[] Strings(JsonElement content, string property)
        => content.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();

    public enum OwnerManage { Allowed, Denied, Unavailable }

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        internal FixtureActivityCatalog Catalog { get; } = new();
        internal Owner Owner { get; } = new();
        internal IServiceProvider Services => scope.ServiceProvider;
        private AgentSession? session;

        private Host(bool installCatalog)
        {
            root = AgentTestServices.Create(services =>
            {
                new FoundationWorkflowsAgentFeature().ConfigureServices(services);
                services.AddSingleton<IWorkflowDefinitionAuthorizer>(Owner);
                if (installCatalog) services.AddSingleton<IActivityAuthoringCatalog>(Catalog);
            });
            scope = root.CreateScope();
        }

        internal static Host Create(bool installCatalog = true) => new(installCatalog);

        private DefaultWorkflowAgentContextProvider Source => Services.GetServices<IAgentContextProvider>().OfType<DefaultWorkflowAgentContextProvider>().Single();

        private async Task<AgentSession> SessionAsync() => session ??= await Services.GetRequiredService<IAgentSessionService>().CreateAsync(new("tenant-1",
            "actor-1", "conversation-1", "test-provider", "build", null, AgentPolicy.Default, new Dictionary<string, string>()));

        internal async Task<AgentContextContribution> CollectAsync(AgentContextAttachment? caller = null)
            => await Source.CollectAsync(WorkflowAgentTests.Request(caller ?? WorkflowAgentTests.Snapshot()) with { SessionId = (await SessionAsync()).Id });

        internal async Task<AgentResult<AgentContextContribution>> CollectThroughEngineAsync()
            => await Services.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
                WorkflowAgentTests.Request(WorkflowAgentTests.Snapshot()) with { SessionId = (await SessionAsync()).Id });

        internal async Task<AgentContextContribution> AdmitAsync(IReadOnlyCollection<AgentContextAttachment> context, bool historical = false)
            => await Source.AdmitDisclosureAsync(new((await SessionAsync()).Id, context, [], false) { IsHistorical = historical });

        public void Dispose()
        {
            scope.Dispose();
            root.Dispose();
        }
    }

    private sealed class Owner : IWorkflowDefinitionAuthorizer
    {
        internal OwnerManage Manage { get; set; } = OwnerManage.Allowed;
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(definitionId == "wf-1");
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) => Manage switch
        {
            OwnerManage.Unavailable => throw new WorkflowDefinitionAuthorizationException(definitionId, "manage", new InvalidOperationException("owner store offline")),
            _ => ValueTask.FromResult(definitionId == "wf-1" && Manage == OwnerManage.Allowed)
        };
    }
}

/// <summary>An owner-shaped activity authoring catalog: answers, gate and failures are the fixture's, not invented by the module.</summary>
internal sealed class FixtureActivityCatalog : IActivityAuthoringCatalog
{
    internal const string FailureDetail = "catalog-store-connection-detail-4471";
    internal bool Allowed { get; set; } = true;
    internal bool Fail { get; set; }
    internal int ListCalls { get; private set; }
    internal IReadOnlyList<ActivityAuthoringCatalogEntry> Entries { get; set; } =
    [
        Entry("version-write-line", true, new ActivityAuthoringCatalogInput("Text", "Text", "String", "Text", true, false, false),
            new ActivityAuthoringCatalogInput("ApiKey", "ApiKey", "String", "API key", false, true, true)),
        Entry("version-excluded", false)
    ];

    public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default)
        => Fail ? throw new ActivityAuthoringCatalogUnavailableException(new InvalidOperationException(FailureDetail)) : ValueTask.FromResult(Allowed);

    public ValueTask<ActivityAuthoringCatalogResult> ListAsync(CancellationToken cancellationToken = default)
    {
        ListCalls++;
        if (Fail) throw new ActivityAuthoringCatalogUnavailableException(new InvalidOperationException(FailureDetail));
        return ValueTask.FromResult(Allowed ? new ActivityAuthoringCatalogResult(true, Entries) : ActivityAuthoringCatalogResult.Denied);
    }

    internal static ActivityAuthoringCatalogEntry Entry(string versionId, bool available, params ActivityAuthoringCatalogInput[] inputs)
        => new(versionId, "Acme." + versionId, available ? "1.0.0" : "2.0.0", "Display " + versionId, "Acme", "An activity description.",
            available, available ? null : "Excluded by the effective activity availability policy.", inputs);
}
