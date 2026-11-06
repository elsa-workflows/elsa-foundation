using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Elsa.Workflows.Design.Validations;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Tests;

internal static class ActivityDataSelectionTestSupport
{
    internal static ServiceProvider Provider(StubActivityCatalog catalog, IActivityStructureService? structures = null,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services);
        new Elsa.Activities.Flowchart.ActivitiesFlowchartFeature().ConfigureServices(services);
        new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        services.AddSingleton<IActivityDefinitionLookup>(catalog);
        if (structures is not null) services.AddSingleton(structures);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
