using Elsa.Activities.Flowchart;
using Elsa.Workflows.Design.Core.Contracts;
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
    }
}
