using Elsa.Activities.Sequence;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Sequence.Tests;

public sealed class ActivitiesSequenceFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersSequenceStructureHandler()
    {
        var services = new ServiceCollection();

        new ActivitiesSequenceFeature().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var handler = Assert.Single(provider.GetServices<IActivityStructureHandler>());
        Assert.Equal(global::Elsa.Activities.Sequence.Activities.Sequence.StructureKind, handler.Kind);
        Assert.Equal(global::Elsa.Activities.Sequence.Activities.Sequence.StructureSchemaVersion, handler.SchemaVersion);
        Assert.Equal(typeof(Models.SequenceAuthoredStructure), handler.AuthoredPayloadType);
    }
}
