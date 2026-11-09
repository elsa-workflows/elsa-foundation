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
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Elsa.Workflows.Design.Api.Endpoints;
using MinimalEndpoints;

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
        // Coded failures (the authorization-unavailable 503) render ahead of translation with an additive errorCode.
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, WorkflowDesignFaultRenderer>(WorkflowsDesignApi.OwnerId);

        services.AddEventHandlersFrom(assembly);
        services.AddCommandHandlersFrom(assembly);
        services.AddRequestHandlersFrom(assembly);
        // These services back the authoring API, which registers them itself rather than relying on the validation feature.
        services.AddScopedVariableAuthoring();
        // Null-object so MinimalEndpoints GetRequiredService does not fail on a design-only host.
        // Publishing replaces this with the slot/journal lookup.
        services.TryAddScoped<IWorkflowDefinitionPublicationStateLookup, NullWorkflowDefinitionPublicationStateLookup>();
        services.TryAddScoped<Endpoints.Definitions.IWorkflowDefinitionDetailsReader, Endpoints.Definitions.WorkflowDefinitionDetailsReader>();
        services.TryAddScoped<IWorkflowDefinitionAuthorizer, HttpWorkflowDefinitionAuthorizer>();
        services.TryAddScoped<Endpoints.Versions.IWorkflowVersionDetailsReader, Endpoints.Versions.WorkflowVersionDetailsReader>();
        services.TryAddScoped<IExpressionAuthoringAuthorizationPolicy, DefaultExpressionAuthoringAuthorizationPolicy>();
        ExpressionToolingStartupTask.Validate(services);
        services.TryAddScoped<IExpressionToolingOperations, ExpressionToolingOperations>();
        AddExpressionToolingValidator(services);
        services.TryAddScoped<IExpressionAuthoringContextSource>(serviceProvider =>
            new PersistedExpressionAuthoringContextSource(
                serviceProvider.GetServices<IWorkflowDefinitionDraftStore>(),
                serviceProvider.GetRequiredService<IActivityStructureService>(),
                serviceProvider.GetRequiredService<Elsa.Workflows.Design.Core.Services.ScopedVariablePicker>(),
                serviceProvider.GetService<IActivityDefinitionVersionStore>(),
                serviceProvider.GetServices<IExpressionAuthoringContextFilter>(),
                serviceProvider.GetServices<IExpressionAuthoringSymbolFilter>(),
                serviceProvider,
                serviceProvider.GetServices<IActivityDraftAuthoringReader>(),
                serviceProvider.GetServices<IActivityDraftExpressionGraphProjection>()));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateActivityDraftExpressionGraphProjectionsStartupTask>());
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
        // The library's organization commands, which the library endpoints call and other domains reach through
        // Workflows Design Core (spec 1193 D4). One Core implementation per shell: a duplicate fails startup.
        // Rejected at configuration as well as at startup: the startup task runs only where the Tasks feature is composed.
        WorkflowLibraryCommandsStartupTask.Validate(services);
        services.TryAddScoped<WorkflowLibraryCommands>();
        services.TryAddScoped<IWorkflowLibraryCommands>(sp => sp.GetRequiredService<WorkflowLibraryCommands>());
        AddLibraryCommandsValidator(services);
        // Exact version reads for other domains through Workflows Design Core (spec 1193 slice F, D4), over the same details
        // composition the version endpoint uses. One implementation per shell, rejected at configuration and at startup.
        WorkflowVersionReaderStartupTask.Validate(services);
        services.TryAddScoped<IWorkflowVersionReader, WorkflowVersionReader>();
        AddVersionReaderValidator(services);
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

    // One validator per configuring collection: a copied child shell rebinds it to its own collection.
    private static void AddExpressionToolingValidator(IServiceCollection services)
    {
        if (services.LastOrDefault(item => item.ServiceType == typeof(ExpressionToolingStartupTask))
                ?.ImplementationInstance is not ExpressionToolingStartupTask validator || !validator.Owns(services))
            validator = new ExpressionToolingStartupTask(services);
        services.RemoveAll<ExpressionToolingStartupTask>();
        services.AddSingleton(validator);
        foreach (var previous in services.Where(item => item.ServiceType == typeof(IStartupTask)
                     && (item.ImplementationInstance is ExpressionToolingStartupTask
                         || item.ImplementationType == typeof(ExpressionToolingStartupTask))).ToArray())
            services.Remove(previous);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask>(validator));
    }

    // One validator per configuring collection: a copied child shell rebinds it to its own collection.
    private static void AddLibraryCommandsValidator(IServiceCollection services)
    {
        if (services.LastOrDefault(item => item.ServiceType == typeof(WorkflowLibraryCommandsStartupTask))
                ?.ImplementationInstance is not WorkflowLibraryCommandsStartupTask validator || !validator.Owns(services))
            validator = new WorkflowLibraryCommandsStartupTask(services);
        services.RemoveAll<WorkflowLibraryCommandsStartupTask>();
        services.AddSingleton(validator);
        foreach (var previous in services.Where(item => item.ServiceType == typeof(IStartupTask)
                     && (item.ImplementationInstance is WorkflowLibraryCommandsStartupTask
                         || item.ImplementationType == typeof(WorkflowLibraryCommandsStartupTask))).ToArray())
            services.Remove(previous);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask>(validator));
    }

    // One validator per configuring collection: a copied child shell rebinds it to its own collection.
    private static void AddVersionReaderValidator(IServiceCollection services)
    {
        if (services.LastOrDefault(item => item.ServiceType == typeof(WorkflowVersionReaderStartupTask))
                ?.ImplementationInstance is not WorkflowVersionReaderStartupTask validator || !validator.Owns(services))
            validator = new WorkflowVersionReaderStartupTask(services);
        services.RemoveAll<WorkflowVersionReaderStartupTask>();
        services.AddSingleton(validator);
        foreach (var previous in services.Where(item => item.ServiceType == typeof(IStartupTask)
                     && (item.ImplementationInstance is WorkflowVersionReaderStartupTask
                         || item.ImplementationType == typeof(WorkflowVersionReaderStartupTask))).ToArray())
            services.Remove(previous);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask>(validator));
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        WorkflowsDesignApi.MapWorkflowsDesignApi(endpoints);
}
