using System.Security.Claims;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;
using Xunit;
using Process = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>Projection and authorization controls; actual host execution is a separate integration gate.</summary>
public sealed class BpmnAuthoredDirectPublisherTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Publication_uses_the_immutable_version_and_presentation_without_mutating_authoring()
    {
        var fixture = new Fixture();
        var before = JsonSerializer.Serialize(fixture.Version.State, Web);
        var outcome = await fixture.Publish();
        Assert.Equal("source", outcome.SourceReferenceId);
        Assert.Equal(1, fixture.Publications);
        Assert.Equal(["version", "definition", "authorize:bpmn:publish", "authorize:workflows/publishing:manage", "catalog", "visible", "layout", "publish"], fixture.Calls);
        Assert.Equal("operation", fixture.RequestId);
        Assert.Equal(new BpmnModelCaller("author", null), fixture.Caller);
        Assert.Equal("version-1", fixture.Source!.VersionId);
        Assert.Equal("1.0.0", fixture.Source.Version);
        Assert.Equal("Frozen process label", Assert.Single(fixture.Source.ActivityPresentation).DisplayName);
        Assert.Contains(fixture.Source.Layout, record => record.NodeId == "bpmn-diagram:root" &&
            record.AdditionalProperties!.Value.GetProperty("shapes").GetProperty("human").GetProperty("x").GetInt32() == 20);
        Assert.Equal("keep", fixture.Source.Layout[0].AdditionalProperties!.Value.GetProperty("opaque").GetString());
        Assert.Equal(1, fixture.Model!.FormatVersion);
        Assert.Equal(["human:human"], fixture.Model.HumanBindings);
        var process = fixture.Model.Process;
        Assert.Equal("process-definition", process.GetProperty("processId").GetString());
        var human = process.GetProperty("elements").EnumerateArray().Single(value => value.GetProperty("elementId").GetString() == "human");
        Assert.Equal("human:human", human.GetProperty("bindingRef").GetString());
        Assert.Empty(human.GetProperty("properties").EnumerateObject());
        Assert.Equal(before, JsonSerializer.Serialize(fixture.Version.State, Web));
    }

    [Theory]
    [InlineData("{\"formatVersion\":99,\"kind\":\"human\"}")]
    [InlineData("{\"formatVersion\":\"1\",\"kind\":\"human\"}")]
    [InlineData("{\"kind\":\"human\"}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"kind\":\"human\"}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"nativeService\"}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"claimRequired\":true}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"claimRequired\":\"false\"}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"unknownMeaning\":true}")]
    public async Task Unsupported_binding_meaning_is_refused_before_runtime_publication(string metadata)
    {
        var fixture = new Fixture(metadata);
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Theory]
    [InlineData("bpmn:publish")]
    [InlineData("workflows/publishing:manage")]
    public async Task Resource_denial_precedes_source_parsing_catalog_and_layout_reads(string denied)
    {
        var fixture = new Fixture { DeniedPermission = denied };
        fixture.Version.StateSource = "malformed source that must not be parsed for a denied caller";
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => fixture.Publish());
        Assert.DoesNotContain("catalog", fixture.Calls);
        Assert.DoesNotContain("layout", fixture.Calls);
        Assert.Equal(0, fixture.Publications);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("definition")]
    [InlineData("layout")]
    public async Task Foreign_tenant_sources_never_reach_publication(string foreign)
    {
        var fixture = new Fixture();
        if (foreign == "version") fixture.Version.TenantId = "other-tenant";
        if (foreign == "definition") fixture.Definition.TenantId = "other-tenant";
        if (foreign == "layout") fixture.Layout.TenantId = "other-tenant";
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invisible")]
    [InlineData("wrong-type")]
    [InlineData("new-contract")]
    public async Task A_shape_alone_cannot_impersonate_a_visible_compatible_root_activity(string invalid)
    {
        var fixture = new Fixture { MissingActivity = invalid == "missing", Visible = invalid != "invisible" };
        if (invalid == "wrong-type") fixture.Activity = fixture.Activity with
            { Definition = new ActivityDefinitionModel("activity", "Another.Activity", "Test", null, null) };
        if (invalid == "new-contract") fixture.Activity = fixture.Activity with { Version = "2.0.0" };
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Fact]
    public async Task Catalog_validation_uses_the_actual_immutable_source_instead_of_a_conflicting_cached_state()
    {
        var fixture = new Fixture();
        fixture.Version.StateSource = JsonSerializer.Serialize(fixture.Version.State with
            { RootActivity = fixture.Version.State.RootActivity! with { ActivityVersionId = "not-a-bpmn-version" } }, Web);
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Fact]
    public async Task Unknown_authored_fields_are_refused_instead_of_silently_dropped()
    {
        var fixture = new Fixture();
        var json = JsonSerializer.SerializeToNode(fixture.Version.State, Web)!;
        json["rootActivity"]!["structure"]!["payload"]!["elements"]![1]!["script"] = "unrepresented behavior";
        fixture.Version.StateSource = json.ToJsonString();
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    [Fact]
    public async Task Native_child_intent_and_source_owned_content_are_not_reinterpreted()
    {
        var fixture = new Fixture();
        fixture.Definition.IsSourceOwned = true;
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        fixture.Definition.IsSourceOwned = false;
        var json = JsonSerializer.SerializeToNode(fixture.Version.State, Web)!;
        json["rootActivity"]!["structure"]!["payload"]!["elements"]![1]!["childNodeId"] = "native-child";
        fixture.Version.StateSource = json.ToJsonString();
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => fixture.Publish());
        Assert.Equal(0, fixture.Publications);
    }

    private sealed class Fixture : IWorkflowDefinitionVersionStore, IWorkflowDefinitionStore,
        IWorkflowDefinitionVersionLayoutStore, IActivityDefinitionLookup, IPermissionAuthorizationService, IBpmnModelRuntime
    {
        public List<string> Calls { get; } = [];
        public WorkflowDefinitionVersion Version { get; }
        public WorkflowDefinition Definition { get; } = new() { Id = "process-definition", Name = "Mutable definition label" };
        public WorkflowDefinitionVersionLayout Layout { get; } = new()
        {
            Id = "layout", WorkflowDefinitionVersionId = "version-1",
            Records = [new("root", 1, 2, AdditionalProperties: JsonSerializer.SerializeToElement(new { opaque = "keep" }))],
            ActivityPresentation = [new("root", "Frozen process label")]
        };
        public ActivityDefinitionVersionModel Activity { get; set; } = new("activity-version", "1.0.0", "activity", "clr", "1", "clr", "1",
            JsonSerializer.SerializeToElement(new { }), "CLR", "source",
            new ActivityDefinitionModel("activity", typeof(Process).FullName!, "BPMN", "BPMN Process", null), [], [], [], default, "hash");
        public string? DeniedPermission { get; set; }
        public bool MissingActivity { get; set; }
        public bool Visible { get; set; } = true;
        public int Publications { get; private set; }
        public BpmnModelDefinition? Model { get; private set; }
        public BpmnModelPublicationSource? Source { get; private set; }
        public BpmnModelCaller? Caller { get; private set; }
        public string? RequestId { get; private set; }

        public Fixture(string metadata = "{\"formatVersion\":1,\"kind\":\"human\"}")
        {
            var structure = new BpmnAuthoredStructure(elements:
                [new("start", BpmnElementTypes.StartEvent), new("human", BpmnElementTypes.UserTask, name: "Review order",
                    properties: new Dictionary<string, string> { ["elsa.direct.binding"] = metadata }), new("end", BpmnElementTypes.EndEvent)],
                sequenceFlows: [new("to-human", "start", "human"), new("to-end", "human", "end")],
                diagram: JsonSerializer.SerializeToElement(new { shapes = new { human = new { x = 20 } } }));
            Version = new("process-definition", "1.0.0") { Id = "version-1", State = new([], new("root", "activity-version", [], [],
                new(Process.StructureKind, Process.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web))), [], [], null) };
        }

        public Task<BpmnPublishedModel> Publish()
        {
            var validator = new NormalizedPrincipalValidator(Options.Create(new FoundationIdentityOptions
                { NormalizedAuthenticationTypes = new HashSet<string>(["test"]) }));
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "author"), new(IdentityClaimTypes.Normalized, "v1")], "test"));
            return new BpmnAuthoredDirectPublisher(this, this, this, this, validator, this, this).PublishAsync("version-1", "operation", principal, default);
        }

        Task<WorkflowDefinitionVersion?> IWorkflowDefinitionVersionStore.FindByIdAsync(string id, CancellationToken cancellationToken)
        { Calls.Add("version"); return Task.FromResult<WorkflowDefinitionVersion?>(Version); }
        Task<WorkflowDefinition?> IWorkflowDefinitionStore.FindByIdAsync(string id, CancellationToken cancellationToken)
        { Calls.Add("definition"); return Task.FromResult<WorkflowDefinition?>(Definition); }
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string id, CancellationToken cancellationToken)
        { Calls.Add("layout"); return Task.FromResult<WorkflowDefinitionVersionLayout?>(Layout); }
        public Task<IActivityDefinitionVersion?> FindVersion(string id, CancellationToken cancellationToken)
        { Calls.Add("catalog"); return Task.FromResult<IActivityDefinitionVersion?>(!MissingActivity && id == Activity.Id ? Activity : null); }
        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null,
            string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default)
        { Calls.Add("visible"); return Task.FromResult<IEnumerable<IActivityDefinition>>(Visible ? [Activity.Definition] : []); }
        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken)
        { Calls.Add("authorize:" + context.Permission); Assert.Same(Version, context.Resource); return ValueTask.FromResult(context.Permission == DeniedPermission ? PermissionEvaluationResult.Denied() : PermissionEvaluationResult.Success); }
        public ValueTask<BpmnPublishedModel> PublishFromSourceAsync(BpmnModelDefinition definition, BpmnModelPublicationSource source, string requestId, BpmnModelCaller caller, CancellationToken cancellationToken)
        { Calls.Add("publish"); Publications++; Model = definition; Source = source; Caller = caller; RequestId = requestId; return ValueTask.FromResult(new BpmnPublishedModel(new("artifact", "definition", "version", "1.0.0", "hash"), "incarnation", "source", "profile")); }
        Task<WorkflowDefinitionVersion> IWorkflowDefinitionVersionStore.GetAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string id, string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<WorkflowDefinition> IWorkflowDefinitionStore.GetAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IActivityDefinition> GetDefinition(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IActivityDefinitionVersion> GetVersion(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BpmnPublishedModel> PublishAsync(BpmnModelDefinition definition, string requestId, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BpmnModelCommandOutcome> StartAsync(string source, string request, IReadOnlyDictionary<string, JsonElement>? inputs, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RuntimeModelExecutionState?> FindExecutionAsync(string id, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<BpmnHumanWork>> ListWorkAsync(string id, RuntimeStorePageRequest request, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BpmnModelCommandOutcome> CompleteHumanAsync(string id, string request, string invocation, string attempt, string[]? outcomes, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BpmnModelCommandOutcome?> FindCommandAsync(string id, string request, BpmnModelCaller caller, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
