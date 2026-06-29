using Elsa.Activities.ForEach;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.ForEach.Tests;

public sealed class ActivitiesForEachFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersForEachStructureHandler()
    {
        var services = new ServiceCollection();

        new ActivitiesForEachFeature().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var handler = Assert.Single(provider.GetServices<IActivityStructureHandler>());
        Assert.Equal(global::Elsa.Activities.ForEach.Activities.ForEach.StructureKind, handler.Kind);
        Assert.Equal(global::Elsa.Activities.ForEach.Activities.ForEach.StructureSchemaVersion, handler.SchemaVersion);
    }
}
