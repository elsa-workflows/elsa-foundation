using Elsa.Activities.Design.Core.Contracts;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityDesignContractTests
{
    [Fact]
    public void ActivityCatalogCore_DoesNotExposePortModel()
    {
        var assembly = typeof(IActivityDefinitionVersion).Assembly;

        Assert.Null(assembly.GetType("Elsa.Activities.Design.Core.Models.ActivityPortDefinition"));
        Assert.Null(assembly.GetType("Elsa.Activities.Design.Core.Models.ActivityPortType"));
        Assert.Null(typeof(IActivityDefinitionVersion).GetProperty("Ports"));
        Assert.NotNull(typeof(IActivityDefinitionVersion).GetProperty("DesignFacets"));
    }
}
