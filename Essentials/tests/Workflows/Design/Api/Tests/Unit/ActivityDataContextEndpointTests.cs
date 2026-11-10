using System.Collections.Immutable;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Services;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Tests.Support;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class ActivityDataContextEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configured_host_metadata_filters_refuse_neutral_discovery_before_private_reads(bool symbolFilter)
    {
        var calls = 0;
        var filter = new RestrictedMetadata();
        using var host = new Host(services =>
        {
            if (symbolFilter) services.AddSingleton<IExpressionAuthoringSymbolFilter>(filter);
            else services.AddSingleton<IExpressionAuthoringContextFilter>(filter);
            services.AddSingleton<IActivitySerializedShapeSource>(new CallbackShape(() => calls++));
        });
        var before = host.Drafts.Draft.State;
        var result = await host.Invoke(host.Request());
        Assert.Equal("unavailable", result.Body.State);
        Assert.Equal("activity-data.metadata-policy-unavailable", result.Body.Code);
        Assert.Null(result.Body.Analysis);
        Assert.Equal(0, host.Drafts.Reads);
        Assert.Equal(0, calls);
        Assert.Equal(0, filter.Calls);
        Assert.Same(before, host.Drafts.Draft.State);
    }

    [Fact]
    public async Task Untouched_input_uses_the_real_analyzer_without_language_modules_and_the_mapped_route_is_no_store()
    {
        using var host = new Host();
        var result = await host.Invoke(host.Request());
        Assert.Equal("supported", result.Body.State);
        Assert.Equal("no-store", result.CacheControl);
        using var wire = JsonDocument.Parse(result.Json);
        Assert.Equal("unknown", wire.RootElement.GetProperty("analysis").GetProperty("sources")[0].GetProperty("shape").GetProperty("state").GetString());
        Assert.Equal("definite", wire.RootElement.GetProperty("analysis").GetProperty("sources")[0].GetProperty("availability").GetString());
        var resultSource = wire.RootElement.GetProperty("analysis").GetProperty("sources").EnumerateArray()
            .Single(source => source.GetProperty("id").GetString() == "activity:producer:$result");
        Assert.Equal(JsonValueKind.Null, resultSource.GetProperty("binding").ValueKind);
        Assert.Equal("RuntimeScopeBindingUnavailable", resultSource.GetProperty("reasonCode").GetString());
        Assert.Equal("producer", resultSource.GetProperty("activityResultSource").GetProperty("producerNodeId").GetString());
        Assert.Equal("$result", resultSource.GetProperty("activityResultSource").GetProperty("projectionKey").GetString());
        Assert.Equal("Customer ID", result.Body.Analysis!.Target!.DisplayName);
        Assert.Equal(new TypeReference("String"), result.Body.Analysis.Target.Type);
        Assert.Contains(result.Body.Analysis.Sources, source => source.Id == "activity:producer:$result" && source.Shape.State == ActivitySerializedShapeState.Unknown);
        Assert.Empty(host.Services.GetServices<IExpressionToolingProvider>());
        var capability = await new ApiCapabilityCatalog([], host.Services.GetServices<IApiCapabilitySource>()).GetAsync();
        var data = Assert.Single(capability.Capabilities, item => item.Id == "elsa.api.activity-data-selection");
        Assert.Equal("1", data.ContractVersion);
        Assert.Contains(data.Links, link => link.Rel == "workflow-data-context" && link.Href == "design/workflows/activity-data/context");
        Assert.DoesNotContain(data.Links, link => link.Rel.Contains("execution", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("catalog")]
    public async Task Denied_authority_discloses_no_metadata_and_precedes_any_draft_read(string denied)
    {
        using var host = new Host();
        if (denied == "definition") host.Owner.Allowed = false; else host.Catalog.Allowed = false;
        var result = await host.Invoke(host.Request());
        Assert.Equal("denied", result.Body.State);
        Assert.Null(result.Body.Analysis);
        Assert.Equal(0, host.Drafts.Reads);
    }

    [Theory]
    [InlineData("missing-catalog")]
    [InlineData("duplicate-catalog")]
    [InlineData("missing-analyzer")]
    [InlineData("duplicate-owner")]
    public async Task Missing_or_ambiguous_authority_fails_closed(string kind)
    {
        using var host = new Host(services => {
            if (kind == "missing-catalog") services.RemoveAll<IActivityAuthoringCatalog>();
            if (kind == "duplicate-catalog") services.AddSingleton<IActivityAuthoringCatalog>(new CatalogAuthority());
            if (kind == "missing-analyzer") services.RemoveAll<IActivityDataSelectionAnalyzer>();
            if (kind == "duplicate-owner") services.AddSingleton<IWorkflowDefinitionAuthorizer>(new DefinitionAuthority());
        });
        var result = await host.Invoke(host.Request());
        Assert.Equal("unavailable", result.Body.State);
        Assert.Null(result.Body.Analysis);
        Assert.Equal(0, host.Drafts.Reads);
    }

    [Theory]
    [InlineData("other-workflow", "tenant")]
    [InlineData("workflow", "other-tenant")]
    public async Task The_draft_must_belong_to_the_authorized_definition_and_current_tenant(string owner, string tenant)
    {
        using var host = new Host();
        host.Drafts.Draft.WorkflowDefinitionId = owner;
        host.Drafts.Draft.TenantId = tenant;
        var result = await host.Invoke(host.Request());
        Assert.Equal("denied", result.Body.State);
        Assert.Null(result.Body.Analysis);
    }

    [Fact]
    public async Task A_stale_acknowledged_baseline_does_not_fall_back_to_the_persisted_candidate()
    {
        using var host = new Host();
        var result = await host.Invoke(host.Request() with { ExpectedBaseRevision = "old" });
        Assert.Equal("stale", result.Body.State);
        Assert.Equal("activity-data.base-changed", result.Body.Code);
        Assert.Null(result.Body.Analysis);
    }

    [Fact]
    public async Task A_current_unsaved_producer_is_visible_and_the_snapshot_is_never_saved()
    {
        using var host = new Host();
        var original = host.Drafts.Draft.State;
        var snapshot = View(Sequence(Node("new-producer"), Node("consumer")));
        var result = await host.Invoke(host.Request() with { Snapshot = snapshot, Query = new() { Source = Result("new-producer") } });
        Assert.Equal("supported", result.Body.State);
        Assert.Equal("activity:new-producer:$result", Assert.Single(result.Body.Analysis!.Sources).Id);
        Assert.Same(original, host.Drafts.Draft.State);
        Assert.Equal(DateTimeOffset.UnixEpoch, host.Drafts.Draft.LastModifiedAt);
        Assert.Null(host.Drafts.Draft.Source);
    }

    [Fact]
    public async Task Deleting_a_producer_in_the_unsaved_snapshot_invalidates_even_an_optional_reference()
    {
        using var host = new Host();
        var request = host.Request() with { Snapshot = View(Sequence(Node("consumer"))), Query = new() { Source = Result("producer", optional: true) } };
        var result = await host.Invoke(request);
        Assert.Equal("invalid", result.Body.State);
        Assert.Null(result.Body.Analysis);
        var persisted = await host.Invoke(request with { Snapshot = null });
        Assert.Equal("supported", persisted.Body.State);
        Assert.Equal("activity:producer:$result", Assert.Single(persisted.Body.Analysis!.Sources).Id);
    }

    [Fact]
    public async Task A_client_snapshot_hash_is_only_a_claim_and_the_server_hash_is_repeatable()
    {
        using var host = new Host();
        var request = host.Request() with { Snapshot = View(Sequence(Node("new-producer"), Node("consumer"))) };
        var bad = await host.Invoke(request with { ExpectedSnapshotHash = "client-invented" });
        Assert.Equal("incompatible", bad.Body.State);
        Assert.Null(bad.Body.Analysis);
        var first = await host.Invoke(request);
        var acknowledged = await host.Invoke(request with { ExpectedSnapshotHash = first.Body.SnapshotHash });
        Assert.Equal("supported", acknowledged.Body.State);
        Assert.Equal(first.Body.SnapshotHash, acknowledged.Body.SnapshotHash);
        Assert.Equal(first.Body.ContextRevision, acknowledged.Body.ContextRevision);
    }

    [Fact]
    public void Draft_views_expose_a_nonpersisted_revision_that_changes_even_if_the_timestamp_does_not()
    {
        var draft = Draft();
        var first = WorkflowDraftView.From(draft, []).AuthoringRevision;
        draft.State = State(Sequence(Node("different"), Node("consumer")));
        var second = WorkflowDraftView.From(draft, []).AuthoringRevision;
        Assert.NotEmpty(first);
        Assert.NotEqual(first, second);
        Assert.Equal(DateTimeOffset.UnixEpoch, draft.LastModifiedAt);
        Assert.Null(draft.Source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Context_identity_ignores_rotating_tokens_but_changes_with_actual_grants(bool changeGrant)
    {
        using var host = new Host();
        var first = await host.Invoke(host.Request(), Principal("1", "read"));
        var next = await host.Invoke(host.Request() with { PreviousContextRevision = first.Body.ContextRevision }, Principal("2", changeGrant ? "manage" : "read"));
        Assert.Equal(changeGrant ? "stale" : "supported", next.Body.State);
        if (changeGrant) Assert.Null(next.Body.Analysis); else Assert.Equal(first.Body.ContextRevision, next.Body.ContextRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_authority_or_save_changes_prevent_metadata_disclosure(bool saveChanges)
    {
        Host? host = null;
        var shape = new CallbackShape(() => {
            if (saveChanges) host!.Drafts.Draft.LastModifiedAt = DateTimeOffset.UnixEpoch.AddSeconds(1);
            else host!.Catalog.Allowed = false;
        });
        using (host = new Host(services => services.AddSingleton<IActivitySerializedShapeSource>(shape)))
        {
            var result = await host.Invoke(host.Request());
            Assert.Equal(saveChanges ? "stale" : "denied", result.Body.State);
            Assert.Null(result.Body.Analysis);
        }
    }

    [Fact]
    public async Task Provider_faults_return_only_a_safe_code()
    {
        using var host = new Host(services => services.AddSingleton<IActivitySerializedShapeSource>(new CallbackShape(() => throw new InvalidOperationException("private /source/path customer-value"))));
        var result = await host.Invoke(host.Request());
        Assert.Equal("unavailable", result.Body.State);
        Assert.Null(result.Body.Analysis);
        Assert.DoesNotContain("private", result.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("customer-value", result.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_query_bounds_are_refused_before_reading_a_draft()
    {
        using var host = new Host();
        var result = await host.Invoke(host.Request() with { Query = new() { Take = 101 } });
        Assert.Equal("incompatible", result.Body.State);
        Assert.Null(result.Body.Analysis);
        Assert.Equal(0, host.Drafts.Reads);
    }

    [Fact]
    public async Task An_unsaved_hidden_version_is_denied_before_any_source_shape_query()
    {
        var calls = 0;
        using var host = new Host(services => services.AddSingleton<IActivitySerializedShapeSource>(new CallbackShape(() => calls++)));
        var hidden = Node("hidden") with { ActivityVersionId = "private-version" };
        var result = await host.Invoke(host.Request() with { Snapshot = View(Sequence(hidden, Node("consumer"))), Query = new() { Source = Result("hidden") } });
        Assert.Equal("denied", result.Body.State);
        Assert.Null(result.Body.Analysis);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task A_visible_but_unavailable_catalog_entry_remains_readable()
    {
        using var host = new Host();
        host.Catalog.Entries = host.Catalog.Entries.Select(entry => entry with { Available = false }).ToArray();
        Assert.Equal("supported", (await host.Invoke(host.Request())).Body.State);
    }

    [Fact]
    public async Task A_late_catalog_subset_change_refuses_metadata_even_when_general_read_stays_allowed()
    {
        Host? host = null;
        using (host = new Host(services => services.AddSingleton<IActivitySerializedShapeSource>(new CallbackShape(() =>
            host!.Catalog.Entries = host.Catalog.Entries.Where(entry => entry.ActivityVersionId != "test").ToArray()))))
        {
            var result = await host.Invoke(host.Request());
            Assert.True(host.Catalog.Allowed);
            Assert.Equal("denied", result.Body.State);
            Assert.Null(result.Body.Analysis);
        }
    }

    [Fact]
    public async Task Analysis_for_a_different_canonical_revision_is_never_disclosed()
    {
        using var host = new Host(services => {
            var owner = services.Single(descriptor => descriptor.ServiceType == typeof(IActivityDataSelectionAnalyzer));
            services.RemoveAll<IActivityDataSelectionAnalyzer>();
            services.AddScoped<IActivityDataSelectionAnalyzer>(provider => new WrongRevisionAnalyzer(
                (IActivityDataSelectionAnalyzer)ActivatorUtilities.CreateInstance(provider, owner.ImplementationType!)));
        });
        var result = await host.Invoke(host.Request());
        Assert.Equal("unavailable", result.Body.State);
        Assert.Equal("activity-data.invalid-analysis", result.Body.Code);
        Assert.Null(result.Body.Analysis);
    }

    [Fact]
    public async Task Literal_member_metadata_is_unknown_and_its_real_metadata_revision_invalidates_the_context()
    {
        var shape = new CallbackShape(() => { });
        using var host = new Host(services => services.AddSingleton<IActivitySerializedShapeSource>(shape));
        var request = host.Request() with { Query = new() { Source = Result("producer"), MemberPath = ["a.b", ""] } };
        var first = await host.Invoke(request);
        Assert.Equal("supported", first.Body.State);
        Assert.Equal(new[] { "a.b", "" }, shape.LastPath);
        var source = Assert.Single(first.Body.Analysis!.Sources);
        Assert.Equal(ActivitySerializedShapeState.Unknown, source.Shape.State);
        Assert.Null(source.Shape.Type);
        shape.Revision = "updated-metadata";
        var second = await host.Invoke(request with { PreviousContextRevision = first.Body.ContextRevision });
        Assert.Equal("stale", second.Body.State);
        Assert.Null(second.Body.Analysis);
    }

    private static ActivityNode Node(string id) => new(id, "test", [], []);
    private static readonly JsonSerializerOptions Web = CreateWeb();
    private static JsonSerializerOptions CreateWeb()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter<ActivitySerializedShapeState>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ActivitySerializedValueKind>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ActivitySerializedPresence>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ActivityChildResultAvailability>(JsonNamingPolicy.CamelCase));
        return options;
    }
    private static ActivityNode Sequence(params ActivityNode[] children) => new("root", "sequence", [], [], new("elsa.sequence.structure", "1.0.0",
        JsonSerializer.SerializeToElement(new { activities = children, variables = Array.Empty<VariableDefinition>() }, Web)));
    private static WorkflowDefinitionState State(ActivityNode root) => new([], root, [], [], null);
    private static WorkflowDefinitionStateView View(ActivityNode root) => new([], root, [], [], null);
    private static WorkflowDefinitionDraft Draft() => new() { Id = "draft", WorkflowDefinitionId = "workflow", TenantId = "tenant", State = State(Sequence(Node("producer"), Node("consumer"))), LastModifiedAt = DateTimeOffset.UnixEpoch };
    private static ArgumentValue Result(string producer, bool optional = false) => new(JsonSerializer.SerializeToElement(new {
        producerNodeId = producer, projectionKey = "$result", producerScopeId = "root", isOptional = optional
    }), "ActivityResult");
    private static ClaimsPrincipal Principal(string token = "1", string grant = "read") => new(new ClaimsIdentity([
        new("sub", "author"), new(IdentityClaimTypes.Normalized, "v1"), new(IdentityClaimTypes.TenantId, "tenant"),
        new("permission", grant), new("exp", token), new("jti", token)
    ], "test"));

    private sealed class RestrictedMetadata : IExpressionAuthoringContextFilter, IExpressionAuthoringSymbolFilter
    {
        internal int Calls { get; private set; }
        public ValueTask<ExpressionAuthoringContext?> FilterAsync(ExpressionAuthoringContext context, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Neutral acquisition must not fabricate an expression context."); }
        public ValueTask<ExpressionSymbol?> FilterAsync(ExpressionSymbol symbol, ExpressionAuthoringContext context, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Neutral acquisition must not fabricate an expression symbol."); }
    }

    private sealed class Host : IDisposable
    {
        internal ServiceProvider Services { get; }
        internal DraftStore Drafts { get; } = new(Draft());
        internal DefinitionAuthority Owner { get; } = new();
        internal CatalogAuthority Catalog { get; } = new();
        private readonly RouteEndpoint endpoint;
        internal Host(Action<IServiceCollection>? configure = null)
        {
            var services = new ServiceCollection();
            services.AddLogging().AddRouting().AddElsaEndpoints();
            services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
            new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services);
            new Elsa.Events.EventsFeature().ConfigureServices(services);
            new WorkflowDesignValidationsFeature().ConfigureServices(services);
            new WorkflowsDesignApiFeature().ConfigureServices(services);
            services.RemoveAll<IWorkflowDefinitionAuthorizer>();
            services.AddSingleton<IWorkflowDefinitionAuthorizer>(Owner);
            services.AddSingleton<IActivityAuthoringCatalog>(Catalog);
            services.AddSingleton<IWorkflowDefinitionDraftStore>(Drafts);
            services.AddSingleton<IActivityDefinitionLookup>(new StubActivityCatalog().Add("sequence").Add("private-version").Add("test", [
                new("CustomerId", "CustomerId", new("String"), null, "Customer ID", null, true)
            ]));
            configure?.Invoke(services);
            Services = services.BuildServiceProvider();
            endpoint = WorkflowDesignEndpointTestSupport.Find(WorkflowDesignEndpointTestSupport.MapEndpoints(), "design/workflows/activity-data/context", "POST");
        }
        internal ActivityDataContextRequest Request() => new("1", "workflow", "draft", WorkflowDraftView.From(Drafts.Draft, []).AuthoringRevision,
            "working-document", "consumer", "CustomerId");
        internal async Task<Response> Invoke(ActivityDataContextRequest request, ClaimsPrincipal? principal = null)
        {
            using var scope = Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = principal ?? Principal() };
            context.Request.Method = "POST";
            context.Request.Path = "/design/workflows/activity-data/context";
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Web)));
            context.Response.Body = new MemoryStream();
            await endpoint.RequestDelegate!(context);
            context.Response.Body.Position = 0;
            var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
            return new(JsonSerializer.Deserialize<ActivityDataContextResponse>(json, Web)!, context.Response.Headers.CacheControl.ToString(), json);
        }
        public void Dispose() => Services.Dispose();
    }
    private sealed record Response(ActivityDataContextResponse Body, string CacheControl, string Json);
    private sealed class DefinitionAuthority : IWorkflowDefinitionAuthorizer
    {
        internal bool Allowed { get; set; } = true;
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Allowed && definitionId == "workflow");
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }
    private sealed class CatalogAuthority : IActivityAuthoringCatalog
    {
        internal bool Allowed { get; set; } = true;
        internal IReadOnlyList<ActivityAuthoringCatalogEntry> Entries { get; set; } = new[] { "test", "sequence" }.Select(id =>
            new ActivityAuthoringCatalogEntry(id, id, "1.0.0", id, null, null, true, null, [])).ToArray();
        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Allowed);
        public ValueTask<ActivityAuthoringCatalogResult> ListAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityAuthoringCatalogResult(Allowed, Allowed ? Entries : []));
    }
    private sealed class WrongRevisionAnalyzer(IActivityDataSelectionAnalyzer owner) : IActivityDataSelectionAnalyzer
    {
        public async ValueTask<Elsa.Workflows.Design.Validations.Core.Models.ActivityDataSelectionAnalysis> AnalyzeAsync(
            Elsa.Workflows.Design.Validations.Core.Models.ActivityDataSelectionDocument document,
            Elsa.Workflows.Design.Validations.Core.Models.ActivityDataSelectionTarget? target = null,
            Elsa.Workflows.Design.Validations.Core.Models.ActivityDataSelectionQuery? query = null,
            CancellationToken cancellationToken = default) => (await owner.AnalyzeAsync(document, target, query, cancellationToken)) with { ContractRevision = "different-candidate" };
    }
    private sealed class DraftStore(WorkflowDefinitionDraft draft) : IWorkflowDefinitionDraftStore
    {
        internal WorkflowDefinitionDraft Draft { get; } = draft;
        internal int Reads { get; private set; }
        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) { Reads++; return Task.FromResult<WorkflowDefinitionDraft?>(draftId == Draft.Id ? Draft : null); }
        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class CallbackShape(Action callback) : IActivitySerializedShapeSource
    {
        internal string Revision { get; set; } = "metadata";
        internal ImmutableArray<string> LastPath { get; private set; } = [];
        public ValueTask<ActivitySerializedShape?> DescribeResultAsync(IActivityDefinitionVersion admittedVersion, string projectionKey, ImmutableArray<string> memberPath,
            ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default)
        {
            LastPath = memberPath;
            callback();
            return ValueTask.FromResult<ActivitySerializedShape?>(new(ActivitySerializedShapeState.Unknown, ActivitySerializedValueKind.Unknown, null, "source", "profile", Revision));
        }
        public ValueTask<ActivitySerializedShape?> DescribeDeclaredTypeAsync(TypeReference declaredType, string ownerContractIdentity, ImmutableArray<string> memberPath,
            ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) => ValueTask.FromResult<ActivitySerializedShape?>(null);
    }
}
