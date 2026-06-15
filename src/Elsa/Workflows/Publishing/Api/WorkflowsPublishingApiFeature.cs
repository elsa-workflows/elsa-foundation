using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Api;

/// <summary>
/// The publishing surface — today a single bridge over the activity-construction seam, tomorrow the
/// seed of the compile-and-publish domain. Its endpoints read a persisted activity definition (the
/// Design seam) and invoke <c>IActivityFactory</c> (the Runtime seam) to materialise a live
/// <c>IActivity</c>. The feature depends only on the two seams' <c>.Core</c> contracts; it is neither
/// Design nor Runtime, which is why it may bridge them without breaking §E2.2.
/// </summary>
[ShellFeature(
    name: "WorkflowsPublishingApi",
    Description = "Bridge endpoints that construct a live activity from a persisted catalog row (the construction seam)."
)]
public class WorkflowsPublishingApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var assembly = GetType().Assembly;

        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddScoped<IActivityStructureService, DefaultActivityStructureService>();
        services.AddRequestHandlersFrom(assembly);
    }
}
