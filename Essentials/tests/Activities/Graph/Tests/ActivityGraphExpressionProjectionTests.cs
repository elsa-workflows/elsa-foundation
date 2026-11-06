using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Graph.Design;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

public sealed class ActivityGraphExpressionProjectionTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public void Registered_graph_strategy_projects_only_public_inputs_graph_variables_and_authored_body(string schema)
    {
        var state = State("elsa.activity-graph", schema);
        using var services = Services();
        var projection = Assert.Single(services.GetServices<IActivityDraftExpressionGraphProjection>());

        Assert.Equal("elsa.activity-graph", projection.ProviderKey);
        var graph = projection.TryProject(state);

        Assert.NotNull(graph);
        Assert.Equal("body-root", graph.RootActivity!.NodeId);
        Assert.Equal("placed-graph", graph.RootActivity.Structure!.Payload.GetProperty("activities")[0].GetProperty("nodeId").GetString());
        Assert.Equal("A", Assert.Single(graph.Inputs).Name);
        Assert.Equal("graph-count", Assert.Single(graph.Variables).ReferenceKey);
        Assert.Null(Assert.Single(graph.Variables).Default);
        Assert.Empty(graph.Outputs);
        // The placed reusable node stays closed: no provider lookup or hidden graph is introduced.
        Assert.Equal(JsonValueKind.Null, graph.RootActivity.Structure.Payload.GetProperty("activities")[0].GetProperty("structure").ValueKind);
    }

    [Theory]
    [InlineData("another-provider", "1")]
    [InlineData("elsa.activity-graph", "3")]
    public void Other_providers_and_unsupported_schemas_have_no_projection(string provider, string schema)
    {
        using var services = Services();
        Assert.Null(Assert.Single(services.GetServices<IActivityDraftExpressionGraphProjection>()).TryProject(State(provider, schema)));
    }

    [Fact]
    public void Malformed_graph_source_has_no_projection()
    {
        using var services = Services();
        var state = State("elsa.activity-graph", "1");
        state = state with { Provider = state.Provider with { Payload = JsonSerializer.SerializeToElement(new { rootActivity = "bad" }) } };
        Assert.Null(Assert.Single(services.GetServices<IActivityDraftExpressionGraphProjection>()).TryProject(state));
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        new GraphActivitiesDesignFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static ActivityDefinitionDraftState State(string provider, string schema) => new(
        new("1", [new("a", "A", new TypeReference("Int32"), true, false, null, "default")], [], []),
        new(provider, schema, JsonSerializer.SerializeToElement(new
        {
            rootActivity = new { nodeId = "body-root", activityVersionId = "sequence", inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                structure = new { kind = "elsa.sequence.structure", schemaVersion = "1", payload = new { activities = new[] {
                    new { nodeId = "placed-graph", activityVersionId = "closed-public-version", inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), structure = (object?)null }
                } } } },
            variables = new[] { new { referenceKey = "graph-count", name = "Count", type = new { alias = "Int32", collectionKind = "Single" }, storageDriverKey = "default", initialValue = new { syntax = "Literal", value = 42 } } },
            outputMappings = Array.Empty<object>(), outcomeMappings = Array.Empty<object>()
        })), new Dictionary<string, string>());
}
