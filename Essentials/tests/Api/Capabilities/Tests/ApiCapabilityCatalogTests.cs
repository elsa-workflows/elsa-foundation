using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Exceptions;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Api.Capabilities.Services;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Api.Capabilities.Tests;

public sealed class ApiCapabilityCatalogTests
{
    [Fact]
    public async Task Aggregates_static_and_dynamic_declarations_in_deterministic_order()
    {
        ApiCapabilityDeclaration[] declarations =
        [
            Declaration("elsa.api.runtime", "RuntimeApi", new ApiCapabilityLink("workflow-executables", "runtime/workflows/executables")),
            Declaration("elsa.api.activity-design", "ActivitiesDesignApi", new ApiCapabilityLink("activity-catalog", "design/activities/catalog"))
        ];
        IApiCapabilitySource[] sources =
        [
            new StubSource(Declaration(
                "elsa.api.workflow-design",
                "ScopedVariableAuthoring",
                new ApiCapabilityLink("scoped-variable-analysis", "design/workflows/scoped-variables/analyze")))
        ];

        var document = await new ApiCapabilityCatalog(declarations, sources).GetAsync();

        Assert.Equal(
            ["elsa.api.activity-design", "elsa.api.runtime", "elsa.api.workflow-design"],
            document.Capabilities.Select(x => x.Id));
        Assert.All(document.Capabilities.SelectMany(x => x.Links), link => Assert.False(link.Href.StartsWith('/')));
    }

    [Fact]
    public async Task Equivalent_duplicates_merge_but_conflicting_links_raise_diagnostics()
    {
        var first = Declaration("elsa.api.runtime", "RuntimeApi", new ApiCapabilityLink("workflow-executables", "runtime/workflows/executables"));
        var equivalent = first with { SourceFeatureId = "RuntimeApiAlias" };
        var merged = await new ApiCapabilityCatalog([first, equivalent], []).GetAsync();
        Assert.Single(merged.Capabilities);

        var conflicting = Declaration("elsa.api.runtime", "BadRuntimeApi", new ApiCapabilityLink("workflow-executables", "wrong/path"));
        var exception = await Assert.ThrowsAsync<ApiCapabilityConflictException>(
            () => new ApiCapabilityCatalog([first, conflicting], []).GetAsync());
        Assert.Contains("RuntimeApi", exception.Message);
        Assert.Contains("BadRuntimeApi", exception.Message);
    }

    [Fact]
    public void Conflicting_static_declarations_fail_during_shell_service_configuration()
    {
        var services = new ServiceCollection();
        services.AddApiCapability(Declaration(
            "elsa.api.runtime",
            "RuntimeApi",
            new ApiCapabilityLink("workflow-executables", "runtime/workflows/executables")));

        var exception = Assert.Throws<ApiCapabilityConflictException>(() => services.AddApiCapability(Declaration(
            "elsa.api.runtime",
            "BadRuntimeApi",
            new ApiCapabilityLink("workflow-executables", "wrong/path"))));

        Assert.Contains("RuntimeApi", exception.Message);
        Assert.Contains("BadRuntimeApi", exception.Message);
    }

    [Fact]
    public async Task A_dynamic_source_is_evaluated_for_each_document()
    {
        var source = new ToggleSource();
        var catalog = new ApiCapabilityCatalog([], [source]);

        Assert.Empty((await catalog.GetAsync()).Capabilities);
        source.Enabled = true;
        Assert.Single((await catalog.GetAsync()).Capabilities);
    }

    [Fact]
    public async Task Dynamic_sources_augment_static_capabilities_without_weakening_static_duplicate_rules()
    {
        var declaration = Declaration(
            "elsa.api.workflow-design",
            "WorkflowsDesignApi",
            new ApiCapabilityLink("workflow-definitions", "design/workflows/definitions"));
        var source = new StubSource(Declaration(
            "elsa.api.workflow-design",
            "WorkflowsDesignApi.Operational",
            new ApiCapabilityLink("scoped-variable-analysis", "design/workflows/scoped-variables/analyze")));

        var capability = Assert.Single((await new ApiCapabilityCatalog([declaration], [source]).GetAsync()).Capabilities);

        Assert.Equal(["scoped-variable-analysis", "workflow-definitions"], capability.Links.Select(link => link.Rel));
    }

    /// <summary>Spec 182, FR-007 and SC-008: a dormant feature's capability is listed, marked dormant, never simply absent.</summary>
    [Fact]
    public async Task A_capability_a_source_contributes_while_its_feature_is_dormant_is_listed_as_dormant_with_its_reason()
    {
        const string reason = "It becomes available once every host can read version '2' of schema family 'Orders'.";
        var source = new StubSource(Declaration("elsa.api.orders", "OrdersApi", new ApiCapabilityLink("orders", "orders")) with { DormantReason = reason });

        var capability = Assert.Single((await new ApiCapabilityCatalog([], [source]).GetAsync()).Capabilities);

        Assert.Equal(("elsa.api.orders", ApiCapabilityView.DormantStatus, reason), (capability.Id, capability.Status, capability.Reason));
        Assert.Equal(["orders"], capability.Links.Select(link => link.Rel));
    }

    [Fact]
    public async Task One_dormant_contribution_marks_the_whole_capability_dormant_and_none_leaves_it_available()
    {
        var link = new ApiCapabilityLink("workflow-definitions", "design/workflows/definitions");
        var available = Declaration("elsa.api.workflow-design", "WorkflowsDesignApi", link);
        var dormant = new StubSource(available with { SourceFeatureId = "Dormant", DormantReason = "Held by an operator." });

        var marked = Assert.Single((await new ApiCapabilityCatalog([available], [dormant]).GetAsync()).Capabilities);
        var unmarked = Assert.Single((await new ApiCapabilityCatalog([available], []).GetAsync()).Capabilities);

        Assert.Equal((ApiCapabilityView.DormantStatus, "Held by an operator."), (marked.Status, marked.Reason));
        Assert.Null(unmarked.Status);
        Assert.Null(unmarked.Reason);
    }

    /// <summary>A document with nothing dormant keeps its wire shape: the two members appear only on a dormant capability.</summary>
    [Fact]
    public void An_available_capability_serializes_without_status_or_reason_and_a_dormant_one_with_both()
    {
        var available = new ApiCapabilityView("elsa.api.orders", "1", []);
        var dormant = available with { Status = ApiCapabilityView.DormantStatus, Reason = "Held by an operator." };

        var availableJson = System.Text.Json.JsonSerializer.Serialize(available, System.Text.Json.JsonSerializerOptions.Web);
        var dormantJson = System.Text.Json.JsonSerializer.Serialize(dormant, System.Text.Json.JsonSerializerOptions.Web);

        Assert.DoesNotContain("status", availableJson, StringComparison.Ordinal);
        Assert.DoesNotContain("reason", availableJson, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"dormant\"", dormantJson, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"Held by an operator.\"", dormantJson, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dormant_declaration_states_why() =>
        Assert.Throws<ArgumentException>(() => Declaration("elsa.api.orders", "OrdersApi") with { DormantReason = " " });

    private static ApiCapabilityDeclaration Declaration(
        string id,
        string sourceFeatureId,
        params ApiCapabilityLink[] links) => new(id, 1, links, sourceFeatureId);

    private sealed class StubSource(params ApiCapabilityDeclaration[] declarations) : IApiCapabilitySource
    {
        public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(declarations);
    }

    private sealed class ToggleSource : IApiCapabilitySource
    {
        public bool Enabled { get; set; }

        public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(
                Enabled
                    ? [Declaration("elsa.api.workflow-design", "ConditionalAuthoring", new ApiCapabilityLink("scoped-variable-analysis", "design/workflows/scoped-variables/analyze"))]
                    : []);
    }
}
