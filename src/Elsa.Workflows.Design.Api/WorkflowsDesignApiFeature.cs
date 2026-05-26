using CShells.Features;
using Elsa.FastEndpoints;
using Elsa.Mapping.Core.Extensions;
using Elsa.Mediator.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api;

[ShellFeature(
    name: "WorkflowsDesignApi",
    Description = "Contains endpoints to manage data in the Workflows Design Domain"
)]
public class WorkflowsDesignApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var assembly = GetType().Assembly;

        services.AddMappingsFrom(assembly);

        services.AddDomainEventHandlersFrom(assembly);
        services.AddCommandHandlersFrom(assembly);
        services.AddRequestHandlersFrom(assembly);
    }
}
