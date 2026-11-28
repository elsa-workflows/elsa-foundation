using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class ActivitiesFlowchartFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersFlowchartStructureHandler()
    {
        var services = new ServiceCollection();

        new ActivitiesFlowchartFeature().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var handler = Assert.Single(provider.GetServices<IActivityStructureHandler>());
        Assert.Equal(global::Elsa.Activities.Flowchart.Activities.Flowchart.StructureKind, handler.Kind);
        Assert.Equal(global::Elsa.Activities.Flowchart.Activities.Flowchart.StructureSchemaVersion, handler.SchemaVersion);
        Assert.Equal(typeof(FlowchartAuthoredStructure), handler.AuthoredPayloadType);
    }

    [Fact]
    public void Structure_handler_projects_only_connected_child_outcome_reference_keys()
    {
        var services = new ServiceCollection();
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        var handler = Assert.Single(services.BuildServiceProvider().GetServices<IActivityStructureHandler>());
        var structure = new FlowchartAuthoredStructure(
            connections:
            [
                new(new("approve", "approved"), new("finish")),
                new(new("approve", "approved"), new("audit")),
                new(new("reject", "rejected"), new("finish"))
            ]);
        var node = new ActivityNode(
            "flowchart",
            "flowchart-version",
            [],
            [],
            new(
                global::Elsa.Activities.Flowchart.Activities.Flowchart.StructureKind,
                global::Elsa.Activities.Flowchart.Activities.Flowchart.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var usage = handler.ProjectChildContractMemberUsage(node);

        Assert.Collection(
            usage,
            approve =>
            {
                Assert.Equal("approve", approve.NodeId);
                Assert.Equal([new("Outcome", "approved", "Connected")], approve.MemberUsage);
            },
            reject =>
            {
                Assert.Equal("reject", reject.NodeId);
                Assert.Equal([new("Outcome", "rejected", "Connected")], reject.MemberUsage);
            });
    }
}
