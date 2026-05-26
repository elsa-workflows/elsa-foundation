using CShells.Features;
using Elsa.Primitives.Exceptions;
using Elsa3.Activities.Design.Import.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Activities.Design.Import;

[ShellFeature(
    name: "Elsa3ImportJsonActivities"
)]
public class Elsa3ImportActivitiesFeature : IShellFeature
{
    /// <summary>
    /// Workflow definition collection sources; from which the activities are extracted
    /// </summary>
    public IEnumerable<string> WorkflowCollectionSourceTypes { get; set; } = [];

    public void ConfigureServices(IServiceCollection services)
    {
        foreach(var source in WorkflowCollectionSourceTypes)
        {
            var type = Type.GetType(source)
                ?? throw new FeatureConfigurationException($"JSON source type '{source}' could not be loaded");

            services.AddScoped(typeof(IActivityCollectionJsonSource), type);
        }

        services.AddDomainEventHandlersFrom(GetType().Assembly);
    }
}
