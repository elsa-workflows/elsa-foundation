using CShells.Features;
using Elsa.Mediator.Core.Extensions;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Provisioning.Core.Events;
using Elsa3.Workflows.Design.Import.Contracts;
using Elsa3.Workflows.Design.Import.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Workflows.Design.Import;

[ShellFeature(
    name: "Elsa3ImportJsonWorkflows"
)]
public class Elsa3ImportWorkflowsFeature : IShellFeature
{
    public IEnumerable<string> WorkflowCollectionSourceTypes { get; set; } = [];

    public void ConfigureServices(IServiceCollection services)
    {
        foreach (var source in WorkflowCollectionSourceTypes)
        {
            var type = Type.GetType(source)
                ?? throw new FeatureConfigurationException($"JSON source type '{source}' could not be loaded");

            services.AddScoped(typeof(IWorkflowCollectionJsonSource), type);
        }

        services.AddDomainEventHandler<OnWorkflowVersionsProvisioning, ImportWorkflows>();

    }
}
