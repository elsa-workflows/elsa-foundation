using CShells.Features;
using Elsa.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Events.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Api;

[ShellFeature(
    name: "ActivitiesDesignApi",
    Description = "Contains endpoints to manage data in the Activities Design Domain"
)]
public class ActivitiesDesignApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var assembly = GetType().Assembly;

        services.AddEventHandlersFrom(assembly);
        services.AddCommandHandlersFrom(assembly);
        services.AddRequestHandlersFrom(assembly);
    }
}
