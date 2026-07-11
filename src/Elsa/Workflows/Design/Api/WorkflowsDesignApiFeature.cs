using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Events.Core.Extensions;
using Elsa.Workflows.Design.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Api.Services;

namespace Elsa.Workflows.Design.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "WorkflowsDesignApi",
    DisplayName = "Workflows Design API",
    Description = "Contains endpoints to manage data in the Workflows Design Domain"
)]
public class WorkflowsDesignApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var assembly = GetType().Assembly;

        services.AddEventHandlersFrom(assembly);
        services.AddCommandHandlersFrom(assembly);
        services.AddRequestHandlersFrom(assembly);
        // These services back the authoring API and must not depend on the optional validation feature.
        services.AddScopedVariableAuthoring();
        services.TryAddScoped<IActivityInputOptionsProviderResolver, ActivityInputOptionsProviderResolver>();
        services.AddScoped<IStartupTask, ValidateActivityInputOptionsProvidersStartupTask>();
    }
}
