using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Api.Capabilities.Services;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Diagnostics;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

public sealed class ActivityDataSelectionContextTests
{
    [Fact]
    public async Task An_untouched_unsaved_graph_is_analyzed_without_changing_the_saved_provider_state()
    {
        using var fixture = new Fixture();
        var before = fixture.Store.Draft.State.Provider.Payload.GetRawText();
        var request = fixture.Request("unsaved-node") with { CurrentSnapshot = Element(State("unsaved-node")) };
        var result = await fixture.Service.ResolveAsync(request, default);
        Assert.Equal("empty", result.State);
        Assert.Equal("5", result.BaseRevision);
        Assert.Equal("unsaved-node", result.Analysis!.Target!.Target.NodeId);
        Assert.NotNull(result.ContextRevision);
        Assert.NotNull(result.SnapshotHash);
        Assert.Equal(before, fixture.Store.Draft.State.Provider.Payload.GetRawText());
        Assert.Equal(5, fixture.Store.Draft.Revision);
    }

    [Fact]
    public async Task Real_shared_analysis_rejects_a_missing_producer_with_a_located_finding()
    {
        using var fixture = new Fixture();
        var request = fixture.Request() with { Query = new() { Source = new ArgumentValue(Element(new
        { producerNodeId = "missing", projectionKey = "$result", producerScopeId = "workflow" }), "ActivityResult") } };
        var result = await fixture.Service.ResolveAsync(request, default);
        Assert.Equal("invalid", result.State);
        Assert.Contains(result.Analysis!.Findings, x => x.Path == "saved-node/inputs/value");
        Assert.True(fixture.Lookup.Reads > 0);
    }

    [Fact]
    public async Task Real_source_bindings_serialize_through_the_owner_context_without_runtime_reflection_fallback()
    {
        using var fixture = new Fixture();
        var state = fixture.Store.Draft.State;
        fixture.Store.Draft.State = state with { Contract = state.Contract with
        { Inputs = [new("declared", "Declared", new("String"), false, true, null, "elsa.json")] } };
        var result = await fixture.Service.ResolveAsync(fixture.Request(), default);
        Assert.Equal("supported", result.State);
        Assert.IsType<VariableReference>(Assert.Single(result.Analysis!.Sources).Binding.Value);
        var json = JsonSerializer.Serialize(result, ActivitiesDesignWire.Context.GetTypeInfo(typeof(ActivityDataSelectionContextResponse))!);
        Assert.Contains("\"referenceKey\":\"declared\"", json);
        Assert.DoesNotContain("producerNodeId", json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Denied_catalog_or_provider_payload_discloses_neither_state_nor_target(bool providerDenied)
    {
        using var fixture = new Fixture();
        fixture.Catalog.Allowed = providerDenied;
        fixture.Actor.Payload = !providerDenied;
        var result = await fixture.Service.ResolveAsync(fixture.Request(), default);
        Assert.Equal("denied", result.State);
        Assert.Null(result.Analysis);
        Assert.Null(result.BaseRevision);
        Assert.Null(result.SnapshotHash);
        Assert.Equal(0, fixture.Lookup.Reads);
        if (!providerDenied) Assert.Equal(0, fixture.Store.DraftReads);
    }

    [Fact]
    public async Task An_actor_catalog_subset_blocks_private_version_metadata_before_lookup()
    {
        using var fixture = new Fixture();
        fixture.Catalog.IncludeVersion = false;
        var result = await fixture.Service.ResolveAsync(fixture.Request(), default);
        Assert.Equal("activity-data.catalog-denied", result.Code);
        Assert.Null(result.Analysis);
        Assert.Equal(0, fixture.Lookup.Reads);
    }

    [Fact]
    public async Task A_foreign_tenant_draft_does_not_disclose_revision_or_target_metadata()
    {
        using var fixture = new Fixture();
        fixture.Store.Draft = new() { Id = "draft", DefinitionId = "definition", TenantId = "foreign", Revision = 5, State = State("saved-node") };
        var result = await fixture.Service.ResolveAsync(fixture.Request(), default);
        Assert.Equal("activity-data.resource-unavailable", result.Code);
        Assert.Null(result.BaseRevision);
        Assert.Null(result.Analysis);
        Assert.Equal(0, fixture.Lookup.Reads);
    }

    [Fact]
    public async Task Unaddable_but_readable_catalog_versions_remain_explainable()
    {
        using var fixture = new Fixture();
        fixture.Catalog.Available = false;
        Assert.Equal("empty", (await fixture.Service.ResolveAsync(fixture.Request(), default)).State);
    }

    [Fact]
    public async Task Stale_base_and_forged_snapshot_hashes_never_reach_metadata_analysis()
    {
        using var fixture = new Fixture();
        Assert.Equal("stale", (await fixture.Service.ResolveAsync(fixture.Request() with { ExpectedBaseRevision = 4 }, default)).State);
        Assert.Equal("stale", (await fixture.Service.ResolveAsync(fixture.Request() with { SnapshotHash = "forged" }, default)).State);
        Assert.Equal(0, fixture.Lookup.Reads);
    }

    [Fact]
    public async Task Unsaved_provider_schema_drift_is_not_a_provider_migration()
    {
        using var fixture = new Fixture();
        var state = State("unsaved-node");
        state = state with { Provider = state.Provider with { SchemaVersion = "2" } };
        var result = await fixture.Service.ResolveAsync(fixture.Request("unsaved-node") with { CurrentSnapshot = Element(state) }, default);
        Assert.Equal("incompatible", result.State);
        Assert.Equal(0, fixture.Lookup.Reads);
    }

    [Fact]
    public async Task Source_owned_read_only_content_can_be_inspected_but_not_replaced_by_a_proposed_state()
    {
        using var fixture = new Fixture();
        fixture.Store.Owner = new ActivityDefinitionAuthoringState
        {
            Id = "definition", DefinitionId = "definition", TenantId = "tenant",
            ContentAuthority = new(ActivityContentAuthorityKind.ProviderSource, "provider", "source")
        };
        Assert.Equal("empty", (await fixture.Service.ResolveAsync(fixture.Request(), default)).State);
        var result = await fixture.Service.ResolveAsync(fixture.Request("changed") with { CurrentSnapshot = Element(State("changed")) }, default);
        Assert.Equal("activity-data.read-only", result.Code);
        Assert.Null(result.Analysis);
    }

    [Fact]
    public async Task Publication_during_analysis_discards_a_changed_snapshot_even_without_a_revision_increment()
    {
        using var fixture = new Fixture(store => store.Draft.Status = ActivityDefinitionDraftStatus.Published);
        var savedState = fixture.Store.Draft.State;
        var result = await fixture.Service.ResolveAsync(fixture.Request("changed") with { CurrentSnapshot = Element(State("changed")) }, default);
        Assert.Equal("stale", result.State);
        Assert.Equal("activity-data.base-stale", result.Code);
        Assert.Null(result.Analysis);
        Assert.Null(result.ContextRevision);
        Assert.Equal(5, fixture.Store.Draft.Revision);
        Assert.Equal(ActivityDefinitionDraftStatus.Published, fixture.Store.Draft.Status);
        Assert.Same(savedState, fixture.Store.Draft.State);
        Assert.True(fixture.Lookup.Reads > 0);
    }

    [Fact]
    public async Task An_unchanged_published_draft_remains_browsable_with_a_distinct_context_identity()
    {
        using var fixture = new Fixture();
        var active = await fixture.Service.ResolveAsync(fixture.Request(), default);
        fixture.Store.Draft.Status = ActivityDefinitionDraftStatus.Published;
        var published = await fixture.Service.ResolveAsync(fixture.Request(), default);
        Assert.Equal("empty", active.State);
        Assert.Equal("empty", published.State);
        Assert.NotNull(published.Analysis);
        Assert.NotNull(published.ContextRevision);
        Assert.NotEqual(active.ContextRevision, published.ContextRevision);
        Assert.Equal(active.SnapshotHash, published.SnapshotHash);
        var changed = await fixture.Service.ResolveAsync(fixture.Request("changed") with { CurrentSnapshot = Element(State("changed")) }, default);
        Assert.Equal("activity-data.read-only", changed.Code);
        Assert.Null(changed.Analysis);
    }

    [Fact]
    public async Task Malformed_owned_snapshots_and_client_authority_fields_are_rejected()
    {
        using var fixture = new Fixture();
        var malformed = State("saved-node") with { Provider = new(GraphActivityProvider.Key, "1", JsonDocument.Parse("{}").RootElement.Clone()) };
        var result = await fixture.Service.ResolveAsync(fixture.Request() with { CurrentSnapshot = Element(malformed) }, default);
        Assert.Equal("invalid", result.State);
        Assert.Contains(result.Analysis!.Findings, x => x.Path == "/rootActivity");
        var inventedNode = System.Text.Json.Nodes.JsonNode.Parse(Element(State("saved-node")).GetRawText())!.AsObject();
        inventedNode["permissions"] = new System.Text.Json.Nodes.JsonArray("all");
        var invented = Element(inventedNode);
        Assert.Equal("activity-data.snapshot-invalid", (await fixture.Service.ResolveAsync(fixture.Request() with { CurrentSnapshot = invented }, default)).Code);
    }

    [Fact]
    public async Task A_catalog_change_during_metadata_work_discards_the_response()
    {
        using var fixture = new Fixture();
        fixture.Catalog.ChangeAfterFirstRead = true;
        var result = await fixture.Service.ResolveAsync(fixture.Request(), default);
        Assert.Equal("activity-data.catalog-stale", result.Code);
        Assert.Null(result.Analysis);
    }

    [Fact]
    public async Task Context_identity_changes_with_the_actor_authorization_profile()
    {
        using var fixture = new Fixture();
        var first = await fixture.Service.ResolveAsync(fixture.Request(), default);
        fixture.Actor.Profile = "revoked-and-restored-profile";
        var second = await fixture.Service.ResolveAsync(fixture.Request(), default);
        Assert.NotEqual(first.ContextRevision, second.ContextRevision);
    }

    [Fact]
    public void Wire_rejects_missing_or_string_base_revision_and_unrecognized_client_authority()
    {
        var info = ActivitiesDesignWire.Context.GetTypeInfo(typeof(ActivityDataSelectionContextRequest))!;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"contractVersion":"1","documentRevision":"1","target":{"nodeId":"node","inputKey":"value"}}""", info));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"contractVersion":"1","expectedBaseRevision":"5","documentRevision":"1","target":{"nodeId":"node","inputKey":"value"}}""", info));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"contractVersion":"1","expectedBaseRevision":5,"documentRevision":"1","target":{"nodeId":"node","inputKey":"value"},"expectedType":"private-type"}""", info));
    }

    [Fact]
    public async Task Scoped_draft_wrapper_keeps_provider_diagnostics_and_adds_real_located_read_failures()
    {
        using var fixture = new Fixture();
        var state = State("saved-node");
        state = state with { Provider = state.Provider with { Payload = Element(new
        {
            rootActivity = new { nodeId = "saved-node", activityVersionId = "native-version", inputs = new[] { new { referenceKey = "value", value = new
            { expressionType = "ActivityResult", value = new { producerNodeId = "missing", projectionKey = "$result", producerScopeId = "workflow" } } } }, outputs = Array.Empty<object>() },
            variables = Array.Empty<object>(), outputMappings = Array.Empty<object>()
        }) } };
        var original = new CountingValidator();
        var wrapper = new ActivityDataSelectionDraftValidator(original, [fixture.Projector], [fixture.Analyzer]);
        var result = await wrapper.ValidateAsync(new("definition", "draft", 5, state));
        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, x => x.Code == "provider.warning");
        var diagnostic = Assert.Single(result.Diagnostics, x => x.Code == "activity.data-selection.invalid");
        Assert.Equal(GraphActivityProvider.Key, diagnostic.Location!.ProviderKey);
        Assert.Equal("value", diagnostic.Location.ReferenceKey);
        Assert.Equal(new ActivityNodeOrigin("AuthoredNode", "saved-node"), Assert.Single(diagnostic.Location.NodeOrigin!));
        Assert.Null(diagnostic.Location.JsonPointer);
        Assert.Equal("saved-node/inputs/value", diagnostic.Metadata["validationPath"]);
        Assert.Equal(1, original.Calls);
    }

    [Fact]
    public async Task Wrapper_preserves_borrowed_host_singleton_lifetime_without_capturing_a_request_scope()
    {
        var original = new CountingValidator();
        var services = new ServiceCollection();
        services.AddSingleton<IActivityDraftValidator>(original);
        services.AddScoped<IActivityDataSelectionAnalyzer, UnusedAnalyzer>();
        ActivityDataSelectionDraftValidationRegistration.Register(services);
        ActivityDataSelectionDraftValidationRegistration.Register(services);
        using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
        {
            using var first = provider.CreateScope();
            using var second = provider.CreateScope();
            var firstWrapper = first.ServiceProvider.GetRequiredService<IActivityDraftValidator>();
            var secondWrapper = second.ServiceProvider.GetRequiredService<IActivityDraftValidator>();
            Assert.NotSame(firstWrapper, secondWrapper);
            await firstWrapper.ValidateAsync(new("definition", "draft", 5, State("saved-node")));
            await secondWrapper.ValidateAsync(new("definition", "draft", 5, State("saved-node")));
        }
        Assert.Equal(2, original.Calls);
        Assert.Equal(0, original.Disposals);
    }

    [Fact]
    public async Task Shared_draft_authority_cannot_be_omitted_after_a_valid_default_verdict()
    {
        var wrapper = new ActivityDataSelectionDraftValidator(new CountingValidator(), [], []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => wrapper.ValidateAsync(new("definition", "draft", 5, State("saved-node"))).AsTask());
    }

    [Fact]
    public async Task Independent_capability_composes_with_the_other_owner_without_optional_language_services()
    {
        var services = new ServiceCollection();
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var sources = provider.GetServices<IApiCapabilitySource>().ToArray();
        var catalog = new ApiCapabilityCatalog(provider.GetServices<ApiCapabilityDeclaration>(), sources);
        var activity = Assert.Single((await catalog.GetAsync()).Capabilities, x => x.Id == "elsa.api.activity-data-selection");
        Assert.Equal("1", activity.ContractVersion);
        Assert.Equal("activity-data-context", Assert.Single(activity.Links).Rel);
        var combined = new ApiCapabilityCatalog([], sources.Append(new OtherOwnerCapability()));
        Assert.Equal(2, Assert.Single((await combined.GetAsync()).Capabilities).Links.Count);
    }

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static ActivityDefinitionDraftState State(string node) => new(new("1", [], [], [new("done", "Done", true)]),
        new(GraphActivityProvider.Key, "1", Element(new { rootActivity = new { nodeId = node, activityVersionId = "native-version", inputs = Array.Empty<object>(), outputs = Array.Empty<object>() },
            variables = Array.Empty<object>(), outputMappings = Array.Empty<object>() })), new Dictionary<string, string>());

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        internal Store Store { get; } = new();
        internal Actor Actor { get; } = new();
        internal Catalog Catalog { get; } = new();
        internal Lookup Lookup { get; } = new();
        internal ActivityDataSelectionContextService Service { get; }
        internal IActivityDataSelectionAnalyzer Analyzer { get; }
        internal IActivityDataSelectionDocumentProjector Projector { get; }
        internal Fixture(Action<Store>? afterAnalysis = null)
        {
            var services = new ServiceCollection();
            services.AddOptions();
            new WorkflowDesignValidationsFeature().ConfigureServices(services);
            services.AddSingleton<IActivityDefinitionLookup>(Lookup);
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = _provider.CreateScope();
            var structures = _scope.ServiceProvider.GetRequiredService<IActivityStructureService>();
            var projector = new GraphActivityProvider(structures, new ActivityContractAuthoringValidator(new Capabilities()));
            Projector = projector;
            Analyzer = _scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>();
            var contextAnalyzer = afterAnalysis is null ? Analyzer : new CompletingAnalyzer(Analyzer, () => afterAnalysis(Store));
            Service = new(Store, Store, Actor, [Catalog], [projector], [contextAnalyzer], structures);
        }
        internal ActivityDataSelectionContextRequest Request(string node = "saved-node") => new("draft", "1", 5, "edit-7", new(node, "value"));
        public void Dispose() { _scope.Dispose(); _provider.Dispose(); }
    }

    private sealed class Actor : IActivityAuthoringContextAsync
    {
        internal bool Payload { get; set; } = true;
        internal string Profile { get; set; } = "profile-1";
        public string? TenantId => "tenant";
        public string ActorId => "actor";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Profile);
        public ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(Payload);
        public ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class Store : IActivityDefinitionDraftStore, IActivityDefinitionAuthoringStore
    {
        internal ActivityDefinitionDraft Draft { get; set; } = new() { Id = "draft", DefinitionId = "definition", TenantId = "tenant", Revision = 5, State = State("saved-node") };
        internal ActivityDefinitionAuthoringState Owner { get; set; } = new() { Id = "definition", DefinitionId = "definition", TenantId = "tenant", ContentAuthority = new(ActivityContentAuthorityKind.Design, "design") };
        internal int DraftReads { get; private set; }
        Task<ActivityDefinitionDraft?> IActivityDefinitionDraftStore.FindAsync(string draftId, CancellationToken cancellationToken) { DraftReads++; return Task.FromResult<ActivityDefinitionDraft?>(Draft); }
        Task<IReadOnlyList<ActivityDefinitionDraft>> IActivityDefinitionDraftStore.ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<ActivityDefinitionAuthoringState?> IActivityDefinitionAuthoringStore.FindAsync(string definitionId, CancellationToken cancellationToken) => Task.FromResult<ActivityDefinitionAuthoringState?>(Owner);
        Task<IReadOnlyList<ActivityDefinitionAuthoringState>> IActivityDefinitionAuthoringStore.ListAsync(IEnumerable<string> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Catalog : IActivityAuthoringCatalog
    {
        internal bool Allowed { get; set; } = true;
        internal bool IncludeVersion { get; set; } = true;
        internal bool Available { get; set; } = true;
        internal bool ChangeAfterFirstRead { get; set; }
        private int _reads;
        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Allowed);
        public ValueTask<ActivityAuthoringCatalogResult> ListAsync(CancellationToken cancellationToken = default)
        {
            _reads++;
            return ValueTask.FromResult(new ActivityAuthoringCatalogResult(Allowed, IncludeVersion ?
                [new("native-version", "Native", "1.0.0", ChangeAfterFirstRead && _reads > 1 ? "Changed" : "Native", "Test", null, Available, null, [])] : []));
        }
    }

    private sealed class Capabilities : IActivityContractCapabilityCatalog { public IReadOnlyCollection<ActivityContractTypeCapability> Types => []; }

    private sealed class CountingValidator : IActivityDraftValidator, IDisposable
    {
        internal int Calls { get; private set; }
        internal int Disposals { get; private set; }
        public ValueTask<ActivityDraftValidation> ValidateAsync(ActivityDraftValidationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(new ActivityDraftValidation(request.DraftId, request.Revision, true, DateTimeOffset.UnixEpoch,
                [new("provider.warning", ActivityDiagnosticSeverity.Warning, "Existing provider guidance.", new(request.SubjectKind, request.DraftId))]));
        }
        public void Dispose() => Disposals++;
    }

    private sealed class UnusedAnalyzer : IActivityDataSelectionAnalyzer
    {
        public ValueTask<ActivityDataSelectionAnalysis> AnalyzeAsync(ActivityDataSelectionDocument document, ActivityDataSelectionTarget? target = null,
            ActivityDataSelectionQuery? query = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No provider graph was claimed.");
    }

    private sealed class CompletingAnalyzer(IActivityDataSelectionAnalyzer inner, Action completed) : IActivityDataSelectionAnalyzer
    {
        public async ValueTask<ActivityDataSelectionAnalysis> AnalyzeAsync(ActivityDataSelectionDocument document, ActivityDataSelectionTarget? target = null,
            ActivityDataSelectionQuery? query = null, CancellationToken cancellationToken = default)
        {
            var analysis = await inner.AnalyzeAsync(document, target, query, cancellationToken);
            completed();
            return analysis;
        }
    }

    private sealed class OtherOwnerCapability : IApiCapabilitySource
    {
        public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>([new("elsa.api.activity-data-selection", 1,
                [new("workflow-data-context", "design/workflows/activity-data/context")], "WorkflowOwner")]);
    }

    private sealed class Lookup : IActivityDefinitionLookup
    {
        internal int Reads { get; private set; }
        private readonly IActivityDefinitionVersion _version = new ActivityDefinitionVersionModel("native-version", "1.0.0", "native-definition", "native", "1", "native", "1",
            Element(new { }), "Test", "test", new ActivityDefinitionModel("native-definition", "Native", "Test", "Native", null),
            [new InputDefinition("value", "Value", new("String"), null, "Value", null, true)], [], [], ActivityExecutionType.Action, "hash");
        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) { Reads++; return Task.FromResult(versionId == _version.Id ? _version : null); }
        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null, string? displayName = null, string? description = null,
            bool? tenantAgnostic = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
