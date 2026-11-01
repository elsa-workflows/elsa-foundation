using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Events.Core.Extensions;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Api.AspNetCore;
using Elsa.Mediator.Core.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Capabilities;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Extensions;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Elsa.Workflows.Design.Api.Endpoints;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "WorkflowsDesignApi",
    DisplayName = "Workflows Design API",
    Description = "Contains endpoints to manage data in the Workflows Design Domain",
    // Every endpoint that writes workflow state admits it through the credential-literal rule,
    // which WorkflowDesignValidations registers; a host without it fails at composition instead of skipping the rule.
    DependsOn = new object[] { "ApiCapabilities", "WorkflowDesignValidations" }
)]
public class WorkflowsDesignApiFeature : IWebShellFeature
{
    /// <summary>Controls folders, tags, saved views and favorites without removing their stored data.</summary>
    [ManifestSetting(DisplayName = "Workflow organization", Description = "Enables folders, tags, saved views and favorites. Turning this off preserves saved organization and ordinary workflow browsing, authoring and execution.", Category = "Workflows", DefaultValue = "true")]
    public bool WorkflowOrganizationEnabled { get; set; } = true;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new WorkflowOrganizationOptions(WorkflowOrganizationEnabled));
        services.AddElsaEndpoints();
        var assembly = GetType().Assembly;

        // Endpoint failure translation and error-shape writing, keyed by the owner so a host
        // composing several modules keeps each module's own error shapes: an unkeyed writer would
        // make the first module's wire format win, and an unkeyed translator could claim another
        // module's generic exceptions.
        services.TryAddKeyedSingleton<IEndpointProblemWriter, WorkflowDesignProblemWriter>(WorkflowsDesignApi.OwnerId);
        services.TryAddKeyedSingleton<IEndpointExceptionTranslator, WorkflowDesignExceptionTranslator>(WorkflowsDesignApi.OwnerId);

        services.AddEventHandlersFrom(assembly);
        services.AddCommandHandlersFrom(assembly);
        services.AddRequestHandlersFrom(assembly);
        // These services back the authoring API, which registers them itself rather than relying on the validation feature.
        services.AddScopedVariableAuthoring();
        // Null-object so NativeEndpoints GetRequiredService does not fail on a design-only host.
        // Publishing replaces this with the slot/journal lookup.
        services.TryAddScoped<IWorkflowDefinitionPublicationStateLookup, NullWorkflowDefinitionPublicationStateLookup>();
        services.TryAddScoped<Endpoints.Definitions.IWorkflowDefinitionDetailsReader, Endpoints.Definitions.WorkflowDefinitionDetailsReader>();
        services.TryAddScoped<IWorkflowDefinitionAuthorizer, HttpWorkflowDefinitionAuthorizer>();
        services.TryAddScoped<Endpoints.Versions.IWorkflowVersionDetailsReader, Endpoints.Versions.WorkflowVersionDetailsReader>();
        services.TryAddScoped<IExpressionAuthoringAuthorizationPolicy, DefaultExpressionAuthoringAuthorizationPolicy>();
        services.TryAddScoped<IExpressionAuthoringContextSource>(serviceProvider =>
            ActivatorUtilities.CreateInstance<PersistedExpressionAuthoringContextSource>(
                serviceProvider,
                serviceProvider.GetServices<IWorkflowDefinitionDraftStore>()));
        services.TryAddScoped<IActivityInputOptionsProviderResolver, ActivityInputOptionsProviderResolver>();
        services.TryAddScoped<ActivityInputOptionsAuthoringService>();
        services.AddScoped<IStartupTask, ValidateActivityInputOptionsProvidersStartupTask>();
        services.TryAddSingleton<IServiceCollection>(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, LibraryVisibilityStartupTask>());
        if (services.LastOrDefault(item => item.ServiceType == typeof(WorkflowDefinitionAuthorizationStartupTask))
                ?.ImplementationInstance is not WorkflowDefinitionAuthorizationStartupTask authorityValidator || !authorityValidator.Owns(services))
            authorityValidator = new WorkflowDefinitionAuthorizationStartupTask(services);
        // Shells copy parent descriptors, including application-wide IServiceCollection metadata.
        // This validator owns only this collection; it captures no scoped authority or request service.
        services.RemoveAll<WorkflowDefinitionAuthorizationStartupTask>();
        services.AddSingleton(authorityValidator);
        foreach (var previous in services.Where(item => item.ServiceType == typeof(IStartupTask)
                     && (item.ImplementationInstance is WorkflowDefinitionAuthorizationStartupTask
                         || item.ImplementationType == typeof(WorkflowDefinitionAuthorizationStartupTask))).ToArray())
            services.Remove(previous);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask>(authorityValidator));
        services.AddHttpContextAccessor();
        services.AddApiCapability(WorkflowDesignApiCapabilities.StaticDeclaration);
        services.AddApiCapabilitySource<WorkflowDesignOperationalCapabilitySource>();
        services.AddApiCapabilitySource<WorkflowTemplateCapabilitySource>();
        services.AddApiCapabilitySource<WorkflowLibraryCapabilitySource>();
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
