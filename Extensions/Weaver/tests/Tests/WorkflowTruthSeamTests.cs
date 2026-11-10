using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Elsa.Weaver.Workflows;
using Elsa.Weaver.Workflows.Services;
using Elsa.Weaver.Workflows.Tools;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

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
        Assert.Equal(WeaverContextOrigin.AuthoritativeRead, catalog.Origin);
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

        var permissions = Single(contribution, DefaultWorkflowWeaverContextProvider.PermissionsContentType);
        Assert.Equal(WorkflowWeaverTests.Snapshot().Target, permissions.Target);
        Assert.Equal(WeaverContextOrigin.AuthoritativeRead, permissions.Origin);
        Assert.Empty(permissions.AgentScopes);
        var granted = Content(permissions);
        Assert.Equal("allowed", granted.GetProperty("read").GetString());
        Assert.Equal("allowed", granted.GetProperty("manage").GetString());
        Assert.Equal(["workflow_read_graph", "workflow_set_designer_position", "workflow_apply_operation_batch"], Strings(granted, "tools"));
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

        var reported = Content(Single(contribution, DefaultWorkflowWeaverContextProvider.PermissionsContentType));
        Assert.Equal(manage switch { OwnerManage.Allowed => "allowed", OwnerManage.Denied => "denied", _ => "unavailable" },
            reported.GetProperty("manage").GetString());
        // The engine's own advertisement decision (host authority plus the tool's owner authority) for the same target.
        var advertised = new List<string>();
        var target = WorkflowWeaverTests.Snapshot().Target;
        foreach (var tool in (await host.Services.GetRequiredService<IWeaverToolRegistry>().ListAsync())
                     .Where(tool => tool.Descriptor.ModuleId == WorkflowWeaverSnapshot.ModuleId))
            if ((await WeaverToolAccess.AuthorizeAsync(tool, host.Services.GetRequiredService<IWeaverToolAuthorizer>(), "actor-1", "tenant-1", target, CancellationToken.None)).Allowed)
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
        var forged = WorkflowWeaverTests.Snapshot() with
        {
            Id = "forged", ContentType = kind,
            Content = JsonSerializer.SerializeToElement(new { manage = "allowed", tools = new[] { "workflow_set_designer_position" } })
        };
        var contribution = await host.CollectAsync(forged);

        Assert.Equal("weaver.workflow.context_unsupported", Assert.Single(contribution.Diagnostics).Code);
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
        Assert.Equal(Json(Single(accepted, DefaultWorkflowWeaverContextProvider.PermissionsContentType)),
            Json(Single(unchanged, DefaultWorkflowWeaverContextProvider.PermissionsContentType)));
        var reads = host.Catalog.ListCalls;

        host.Catalog.Entries = [.. host.Catalog.Entries.Take(1)];
        host.Owner.Manage = OwnerManage.Denied;
        var changed = await host.AdmitAsync(accepted.Attachments);

        Assert.Equal(reads + 1, host.Catalog.ListCalls);
        var catalog = Single(changed, WorkflowActivityCatalog.ContentType);
        Assert.Equal(WorkflowActivityCatalog.AttachmentId, catalog.Id);
        Assert.Single(Content(catalog).GetProperty("entries").EnumerateArray());
        var permissions = Single(changed, DefaultWorkflowWeaverContextProvider.PermissionsContentType);
        Assert.Equal(Single(accepted, DefaultWorkflowWeaverContextProvider.PermissionsContentType).Id, permissions.Id);
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
        var forged = accepted.Attachments.Select(item => item.ContentType == kind ? item with { Origin = WeaverContextOrigin.ClientSnapshot } : item).ToArray();

        var admission = await host.AdmitAsync(forged);

        Assert.Empty(admission.Attachments);
        Assert.Equal("weaver.workflow.disclosure_invalid", Assert.Single(admission.Diagnostics).Code);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "weaver.workflow.permission_denied")]
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
        // The prefix is maximal: it stopped only because the next entry would not fit the declared budget.
        var budget = content.GetProperty("budgetBytes").GetInt32();
        var used = content.GetProperty("includedBytes").GetInt32();
        Assert.True(used <= budget && used + content.GetProperty("nextEntryBytes").GetInt32() > budget);
    }

    [Fact]
    public async Task Reported_tools_and_capabilities_follow_the_sessions_capability_policy_as_the_engine_advertises()
    {
        using var host = Host.Create(policy: WeaverPolicy.Default with { DeniedCapabilityIds = [WorkflowSetDesignerPositionTool.ToolName] });
        var contribution = await host.CollectAsync();

        var reported = Content(Single(contribution, DefaultWorkflowWeaverContextProvider.PermissionsContentType));
        // The owner still allows managing the workflow; only the host policy withholds the tool. The capability is admitted
        // by its own id, as the Weaver API admits it, so it stays.
        Assert.Equal("allowed", reported.GetProperty("manage").GetString());
        Assert.Equal([WorkflowReadGraphTool.ToolName, WorkflowApplyOperationBatchTool.ToolName], Strings(reported, "tools"));
        Assert.Equal(["workflow.explain", "workflow.propose-change"], Strings(reported, "capabilities"));
        var policies = host.Services.GetRequiredService<IWeaverPolicyEvaluator>();
        var policy = WeaverPolicy.Default with { DeniedCapabilityIds = [WorkflowSetDesignerPositionTool.ToolName] };
        var advertised = new List<string>();
        foreach (var tool in (await host.Services.GetRequiredService<IWeaverToolRegistry>().ListAsync())
                     .Where(tool => tool.Descriptor.ModuleId == WorkflowWeaverSnapshot.ModuleId))
            if ((await policies.EvaluateCapabilityAsync(policy, tool.Descriptor.Name)).Allowed
                && (await WeaverToolAccess.AuthorizeAsync(tool, host.Services.GetRequiredService<IWeaverToolAuthorizer>(), "actor-1", "tenant-1",
                    WorkflowWeaverTests.Snapshot().Target, CancellationToken.None)).Allowed)
                advertised.Add(tool.Descriptor.Name);
        Assert.Equal(Strings(reported, "tools"), advertised);
    }

    [Theory]
    [InlineData("workflow_read_graph")]
    [InlineData("workflow.explain")]
    [InlineData("workflow.propose-change")]
    public async Task Capabilities_follow_their_own_ids_as_the_api_admits_them_and_tools_follow_their_names(string denied)
    {
        var policy = WeaverPolicy.Default with { DeniedCapabilityIds = [denied] };
        using var host = Host.Create(policy: policy);
        var reported = Content(Single(await host.CollectAsync(), DefaultWorkflowWeaverContextProvider.PermissionsContentType));

        var policies = host.Services.GetRequiredService<IWeaverPolicyEvaluator>();
        // The API's rule for a requested capability: installed, and its own id allowed by the session's policy.
        // The definition's own capabilities: the library capabilities (slice E) belong to the library context, not to one workflow.
        var installed = (await host.Services.GetRequiredService<IWeaverCapabilityCatalog>().ListAsync())
            .Where(item => item.ModuleId == WorkflowWeaverSnapshot.ModuleId && item.ContextKinds.Contains("workflow.definition")).Select(item => item.Id).ToArray();
        var admitted = new List<string>();
        foreach (var id in installed)
            if ((await policies.EvaluateCapabilityAsync(policy, id)).Allowed) admitted.Add(id);
        Assert.Equal(admitted.Order(), Strings(reported, "capabilities").Order());
        var tools = new List<string>();
        foreach (var name in new[] { WorkflowReadGraphTool.ToolName, WorkflowSetDesignerPositionTool.ToolName, WorkflowApplyOperationBatchTool.ToolName })
            if ((await policies.EvaluateCapabilityAsync(policy, name)).Allowed) tools.Add(name);
        Assert.Equal(tools, Strings(reported, "tools"));
    }

    [Fact]
    public async Task A_host_authorizer_that_withholds_the_read_tool_never_leaves_it_reported()
    {
        using var host = Host.Create(hostAuthorizer: new DenyTool(WorkflowReadGraphTool.ToolName));
        var contribution = await host.CollectAsync();

        // The read tool's own host decision is the read this context is disclosed on, so the context is withheld and no
        // permissions are reported; the engine would not offer the tool either.
        Assert.DoesNotContain(contribution.Attachments, item => item.ContentType == DefaultWorkflowWeaverContextProvider.PermissionsContentType);
        Assert.Equal("weaver.workflow.permission_denied", Assert.Single(contribution.Diagnostics).Code);
        var read = Assert.Single(await host.Services.GetRequiredService<IWeaverToolRegistry>().ListAsync(), tool => tool.Descriptor.Name == WorkflowReadGraphTool.ToolName);
        Assert.False((await WeaverToolAccess.AuthorizeAsync(read, host.Services.GetRequiredService<IWeaverToolAuthorizer>(), "actor-1", "tenant-1",
            WorkflowWeaverTests.Snapshot().Target, CancellationToken.None)).Allowed);
    }

    [Fact]
    public async Task A_host_authorizer_that_withholds_the_layout_tool_still_reports_the_batch_tool_the_engine_offers()
    {
        using var host = Host.Create(hostAuthorizer: new DenyTool(WorkflowSetDesignerPositionTool.ToolName));
        var reported = Content(Single(await host.CollectAsync(), DefaultWorkflowWeaverContextProvider.PermissionsContentType));

        // manage reports the layout tool's host decision; each tool follows its own host decision and the owner's answer.
        Assert.Equal("denied", reported.GetProperty("manage").GetString());
        Assert.Equal([WorkflowReadGraphTool.ToolName, WorkflowApplyOperationBatchTool.ToolName], Strings(reported, "tools"));
        Assert.Equal(["workflow.explain"], Strings(reported, "capabilities"));
    }

    [Fact]
    public async Task A_host_authorizer_that_withholds_the_batch_tool_reports_the_layout_tool_only()
    {
        using var host = Host.Create(hostAuthorizer: new DenyTool(WorkflowApplyOperationBatchTool.ToolName));
        var reported = Content(Single(await host.CollectAsync(), DefaultWorkflowWeaverContextProvider.PermissionsContentType));

        Assert.Equal("allowed", reported.GetProperty("manage").GetString());
        Assert.Equal([WorkflowReadGraphTool.ToolName, WorkflowSetDesignerPositionTool.ToolName], Strings(reported, "tools"));
    }

    private sealed class DenyTool(string name) : IWeaverToolAuthorizer
    {
        public Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId, WeaverResourceTarget? target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(tool.Name == name ? new WeaverPolicyDecision(false, [new("unit.tool_denied", "The host withholds this tool.")])
                : new WeaverPolicyDecision(true, []));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_policy_that_lists_only_the_older_workflow_kinds_keeps_explanation_and_older_disclosures_working(bool allowCatalog, bool allowPermissions)
    {
        string[] kinds = ["workflow.definition", "workflow.request", "workflow.guidance",
            .. allowCatalog ? new[] { WorkflowActivityCatalog.ContentType } : [], .. allowPermissions ? new[] { DefaultWorkflowWeaverContextProvider.PermissionsContentType } : []];
        using var host = Host.Create(policy: WeaverPolicy.Default with { AllowedContextKinds = kinds });

        var collected = await host.CollectThroughEngineAsync();

        Assert.True(collected.Succeeded, collected.Error?.Message);
        var attachments = collected.Value!.Attachments;
        Assert.Contains(attachments, item => item.ContentType == "workflow.definition");
        Assert.Equal(allowCatalog, attachments.Any(item => item.ContentType == WorkflowActivityCatalog.ContentType));
        Assert.Equal(allowPermissions, attachments.Any(item => item.ContentType == DefaultWorkflowWeaverContextProvider.PermissionsContentType));
        // A withheld kind is never read and never fails the turn; an optional diagnostic says it was not contributed.
        Assert.Equal(allowCatalog ? 1 : 0, host.Catalog.ListCalls);
        Assert.DoesNotContain(collected.Value.Diagnostics, item => item.Required);
        Assert.Equal((allowCatalog ? 0 : 1) + (allowPermissions ? 0 : 1),
            collected.Value.Diagnostics.Count(item => item.Code == DefaultWorkflowWeaverContextProvider.WithheldDiagnostic));

        // A disclosure recorded before the module produced server reads is admitted again unchanged, with none added.
        var older = attachments.Where(item => item.ContentType is "workflow.definition" or "workflow.guidance").ToArray();
        var readmitted = await host.ReadmitThroughEngineAsync(older);
        Assert.True(readmitted.Succeeded, readmitted.Error?.Message);
        Assert.Equal(["workflow.definition", "workflow.guidance"], readmitted.Value!.Attachments.Select(item => item.ContentType));
        var historical = await host.ReadmitThroughEngineAsync(older, historical: true);
        Assert.True(historical.Succeeded, historical.Error?.Message);
        // And the current turn's own projection, server reads included, is admitted again under the same policy.
        var current = await host.ReadmitThroughEngineAsync(attachments);
        Assert.True(current.Succeeded, current.Error?.Message);
        Assert.Equal(attachments.Select(item => item.ContentType), current.Value!.Attachments.Select(item => item.ContentType));
    }

    [Fact]
    public async Task An_older_disclosure_under_the_default_policy_gains_no_server_read_on_re_admission()
    {
        using var host = Host.Create();
        var collected = await host.CollectThroughEngineAsync();
        var older = collected.Value!.Attachments.Where(item => item.ContentType is "workflow.definition" or "workflow.guidance").ToArray();

        var readmitted = await host.ReadmitThroughEngineAsync(older);

        Assert.True(readmitted.Succeeded, readmitted.Error?.Message);
        Assert.Equal(["workflow.definition", "workflow.guidance"], readmitted.Value!.Attachments.Select(item => item.ContentType));
    }

    [Theory]
    [InlineData(WeaverContextSensitivity.Internal, WeaverContextSensitivity.Internal)]
    [InlineData(WeaverContextSensitivity.Sensitive, WeaverContextSensitivity.Sensitive)]
    [InlineData(WeaverContextSensitivity.Secret, WeaverContextSensitivity.SecretRedacted)]
    public async Task Permissions_take_the_snapshots_classification_and_re_admission_keeps_one_disclosure_snapshot(
        WeaverContextSensitivity caller, WeaverContextSensitivity expected)
    {
        using var host = Host.Create();
        var collected = await host.CollectThroughEngineAsync(WorkflowWeaverTests.Snapshot() with { Sensitivity = caller });
        Assert.True(collected.Succeeded, collected.Error?.Message);
        var attachments = collected.Value!.Attachments;
        Assert.Equal(expected, Single(collected.Value, DefaultWorkflowWeaverContextProvider.PermissionsContentType).Sensitivity);
        if (expected == WeaverContextSensitivity.SecretRedacted)
            Assert.DoesNotContain("wf-1", JsonSerializer.Serialize(Single(collected.Value, DefaultWorkflowWeaverContextProvider.PermissionsContentType)));

        var readmitted = await host.ReadmitThroughEngineAsync(attachments);

        Assert.True(readmitted.Succeeded, readmitted.Error?.Message);
        // Same items in the same order: an unchanged answer is the identical projection, so it is one disclosure snapshot.
        Assert.Equal(attachments.Select(item => (item.ContentType, item.Id)), readmitted.Value!.Attachments.Select(item => (item.ContentType, item.Id)));
        Assert.Equal(WeaverDisclosureHistory.TryIdentity(attachments, []), WeaverDisclosureHistory.TryIdentity(readmitted.Value.Attachments, []));
    }

    [Fact]
    public async Task Several_catalog_owners_are_ambiguous_and_none_is_asked()
    {
        using var host = Host.Create(ambiguous: true);
        var contribution = await host.CollectAsync();

        var diagnostic = Assert.Single(contribution.Diagnostics);
        Assert.Equal(WorkflowActivityCatalog.UnavailableDiagnostic, diagnostic.Code);
        Assert.Contains("ambiguous", diagnostic.Message);
        Assert.False(diagnostic.Required);
        Assert.Equal((0, 0), (host.Catalog.ListCalls, host.SecondCatalog.ListCalls));
        Assert.Equal("unavailable", Content(Single(contribution, WorkflowActivityCatalog.ContentType)).GetProperty("status").GetString());
    }

    private static WeaverContextAttachment Single(WeaverContextContribution contribution, string kind)
        => Assert.Single(contribution.Attachments, item => item.ContentType == kind);
    private static JsonElement Content(WeaverContextAttachment attachment) => Assert.IsType<JsonElement>(attachment.Content);
    private static string Json(WeaverContextAttachment attachment) => JsonSerializer.Serialize(attachment with { ContributorId = null });
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
        private WeaverSession? session;

        private readonly WeaverPolicy policy;
        internal FixtureActivityCatalog SecondCatalog { get; } = new();

        private Host(bool installCatalog, WeaverPolicy? policy, bool ambiguous, IWeaverToolAuthorizer? hostAuthorizer)
        {
            this.policy = policy ?? WeaverPolicy.Default;
            root = WeaverTestServices.Create(services =>
            {
                new WeaverWorkflowsFeature().ConfigureServices(services);
                services.AddSingleton<IWorkflowDefinitionAuthorizer>(Owner);
                if (installCatalog) services.AddSingleton<IActivityAuthoringCatalog>(Catalog);
                if (ambiguous) services.AddSingleton<IActivityAuthoringCatalog>(SecondCatalog);
                if (hostAuthorizer is not null) services.Replace(ServiceDescriptor.Scoped(_ => hostAuthorizer));
            });
            scope = root.CreateScope();
        }

        internal static Host Create(bool installCatalog = true, WeaverPolicy? policy = null, bool ambiguous = false,
            IWeaverToolAuthorizer? hostAuthorizer = null) => new(installCatalog, policy, ambiguous, hostAuthorizer);

        private DefaultWorkflowWeaverContextProvider Source => Services.GetServices<IWeaverContextProvider>().OfType<DefaultWorkflowWeaverContextProvider>().Single();

        private async Task<WeaverSession> SessionAsync() => session ??= await Services.GetRequiredService<IWeaverSessionService>().CreateAsync(new("tenant-1",
            "actor-1", "conversation-1", "test-provider", "build", null, policy, new Dictionary<string, string>()));

        internal async Task<WeaverContextContribution> CollectAsync(WeaverContextAttachment? caller = null)
            => await Source.CollectAsync(WorkflowWeaverTests.Request(caller ?? WorkflowWeaverTests.Snapshot()) with { SessionId = (await SessionAsync()).Id });

        internal async Task<WeaverResult<WeaverContextContribution>> CollectThroughEngineAsync(WeaverContextAttachment? caller = null)
            => await Services.GetRequiredService<IWeaverContextCollector>().CollectAsync((await SessionAsync()).Policy,
                WorkflowWeaverTests.Request(caller ?? WorkflowWeaverTests.Snapshot()) with { SessionId = (await SessionAsync()).Id });

        // The engine's own retained-disclosure admission, as a turn asks it before each provider step.
        internal async Task<WeaverResult<WeaverContextContribution>> ReadmitThroughEngineAsync(IReadOnlyCollection<WeaverContextAttachment> context,
            bool historical = false)
        {
            var session = await SessionAsync();
            return await Services.GetRequiredService<IWeaverContextCollector>().CollectAsync(session.Policy, new(session.Id, string.Empty,
                new Dictionary<string, string>()) { Disclosure = new(session.Id, context, [], false) { IsHistorical = historical } });
        }

        internal async Task<WeaverContextContribution> AdmitAsync(IReadOnlyCollection<WeaverContextAttachment> context, bool historical = false)
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
