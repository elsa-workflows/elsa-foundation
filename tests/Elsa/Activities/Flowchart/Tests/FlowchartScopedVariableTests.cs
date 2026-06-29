using System.Text.Json;
using Elsa.Activities.Flowchart.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Flowchart container-scoped variables (#208): a Flowchart owns container-scoped variable
/// declarations and uses the same generic scope semantics as a Sequence (ADR 0027).
/// </summary>
public sealed class FlowchartScopedVariableTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static IActivityStructureService StructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    private static VariableDefinition Counter() =>
        new("var-counter", "Counter", new Primitives.Models.TypeReference("String"), null, null);

    private static ActivityNode FlowchartNode(string nodeId, IReadOnlyCollection<VariableDefinition> variables, params ActivityNode[] children) =>
        new(
            nodeId,
            "activity-flowchart",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(
                    new FlowchartAuthoredStructure(children, connections: [], startNodeId: null, variables: variables),
                    SerializerOptions)));

    [Fact]
    public void Flowchart_is_a_container_capable_of_owning_scoped_variables()
    {
        var service = StructureService();
        var flowchart = FlowchartNode("flow", [Counter()]);

        Assert.True(service.SupportsScopedVariables(flowchart));
        var declared = Assert.Single(service.ProjectScopedVariables(flowchart));
        Assert.Equal("var-counter", declared.ReferenceKey);
    }

    [Fact]
    public void Flowchart_materializes_declared_variables_into_executable_structure()
    {
        var service = StructureService();
        var flowchart = FlowchartNode("flow", [Counter()]);

        var executable = service.CompileExecutableStructure(flowchart);

        Assert.NotNull(executable);
        var structure = executable!.Payload.Deserialize<FlowchartStructure>(SerializerOptions);
        Assert.NotNull(structure);
        var materialized = Assert.Single(structure!.Variables);
        Assert.Equal("var-counter", materialized.ReferenceKey);
    }

    [Fact]
    public void Flowchart_exposes_container_variables_to_descendants_via_the_shared_resolver()
    {
        var service = StructureService();
        var child = new ActivityNode("child", "av-child", [], []);
        var flowchart = FlowchartNode("flow", [Counter()], child);
        var resolver = new ScopedVariableResolver(service);

        var visibility = resolver.Resolve([], flowchart, maxDepth: 100);

        // The descendant sees the Flowchart's container-scoped variable, identical to Sequence semantics.
        Assert.True(visibility.IsReferenceVisible("child", new VariableReference("var-counter", "flow")));
        Assert.False(visibility.IsReferenceVisible("child", new VariableReference("var-counter", "other-scope")));
    }
}
