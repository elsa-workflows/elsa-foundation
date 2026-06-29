using Elsa.Activities.Parallel;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;

namespace Elsa.Activities.Parallel.Tests;

public sealed class ActivitiesParallelFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersParallelStructureHandler()
    {
        var services = new ServiceCollection();

        new ActivitiesParallelFeature().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var handler = Assert.Single(provider.GetServices<IActivityStructureHandler>());
        Assert.Equal(ParallelActivity.StructureKind, handler.Kind);
        Assert.Equal(ParallelActivity.StructureSchemaVersion, handler.SchemaVersion);
    }
}
