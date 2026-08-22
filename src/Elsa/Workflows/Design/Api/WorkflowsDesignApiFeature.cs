using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Events.Core.Extensions;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Api.AspNetCore;
using Elsa.Mediator.Core.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Capabilities;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Extensions;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Elsa.Workflows.Design.Api.Endpoints;

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
public class WorkflowsDesignApiFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        var assembly = GetType().Assembly;

        // Endpoint failure translation and error-shape writing, used by the mapped mediator endpoints.
        services.TryAddSingleton<IEndpointProblemWriter, WorkflowDesignProblemWriter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEndpointExceptionTranslator, WorkflowDesignExceptionTranslator>());

        services.AddEventHandlersFrom(assembly);
        services.AddCommandHandlersFrom(assembly);
        services.AddRequestHandlersFrom(assembly);
        // These services back the authoring API and must not depend on the optional validation feature.
        services.AddScopedVariableAuthoring();
        services.TryAddScoped<Endpoints.Definitions.IWorkflowDefinitionDetailsReader, Endpoints.Definitions.WorkflowDefinitionDetailsReader>();
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
        services.AddPermissionContributor<WorkflowDesignPermissionContributor>();
        services.ConfigureHttpJsonOptions(options =>
        {
            if (!options.SerializerOptions.TypeInfoResolverChain.Any(resolver => resolver is WorkflowsDesignJsonTypeInfoResolver))
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, new WorkflowsDesignJsonTypeInfoResolver());
        });
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        WorkflowsDesignApi.MapWorkflowsDesignApi(endpoints);
}
