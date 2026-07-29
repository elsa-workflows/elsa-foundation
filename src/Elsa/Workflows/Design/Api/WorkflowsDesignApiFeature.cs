using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Events.Core.Extensions;
using Elsa.Workflows.Design.Core.Extensions;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Workflows.Design.Api.Capabilities;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "WorkflowsDesignApi",
    DisplayName = "Workflows Design API",
    Description = "Contains endpoints to manage data in the Workflows Design Domain",
    DependsOn = new object[] { "ApiCapabilities" }
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
        services.TryAddScoped<IExpressionAuthoringAuthorizationPolicy, DefaultExpressionAuthoringAuthorizationPolicy>();
        services.TryAddScoped<IExpressionAuthoringContextSource>(serviceProvider =>
            ActivatorUtilities.CreateInstance<PersistedExpressionAuthoringContextSource>(
                serviceProvider,
                serviceProvider.GetServices<IWorkflowDefinitionDraftStore>()));
        services.TryAddScoped<IActivityInputOptionsProviderResolver, ActivityInputOptionsProviderResolver>();
        services.TryAddScoped<ActivityInputOptionsAuthoringService>();
        services.AddScoped<IStartupTask, ValidateActivityInputOptionsProvidersStartupTask>();
        services.AddApiCapability(WorkflowDesignApiCapabilities.StaticDeclaration);
        services.AddApiCapabilitySource<WorkflowDesignOperationalCapabilitySource>();
    }
}
