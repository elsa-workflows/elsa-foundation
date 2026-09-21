using Elsa.Activities.Bpmn.Interchange.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

public sealed class ActivitiesBpmnInterchangeFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersImporterAndExporter()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IBpmnDocumentImporter>());
        Assert.NotNull(provider.GetRequiredService<IBpmnDocumentExporter>());
    }
}
