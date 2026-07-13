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
