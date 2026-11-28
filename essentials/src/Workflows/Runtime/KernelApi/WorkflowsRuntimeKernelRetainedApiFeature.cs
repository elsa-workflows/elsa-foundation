using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.KernelHost;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workflows.Runtime.KernelApi;

/// <summary>Selects the Workflow Runtime HTTP API served by the built-in workflow kernel.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Workflows Runtime Retained Kernel API",
    Description = "Serves executable inspection and kernel workflow runs, history and operations through the Workflow Runtime API.",
    DependsOn = new object[] { WorkflowsRuntimeKernelFeature.FeatureName, "ApiCapabilities" })]
public sealed class WorkflowsRuntimeKernelRetainedApiFeature : IWebShellFeature, IPostConfigureShellServices
{
    public const string FeatureName = "WorkflowsRuntimeKernelRetainedApi";

    // Keep historic endpoint ownership and capability contributor identities stable.
    internal const string RouteOwner = "WorkflowsRuntimeKernelApi";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddRetainedRuntimeHttpServices();
        RuntimeHttpInspectionServices.AddExecutableInspectionServices(services);
        RuntimeHttpInspectionServices.AddRunInspectionServices(services, provider => new WorkflowRunInspection(
            provider.GetRequiredService<IWorkflowInstanceDetailsService>(),
            provider.GetRequiredService<IActivityExecutionInspectionService>(),
            timeProvider: provider.GetRequiredService<TimeProvider>(),
            executables: provider.GetRequiredService<IWorkflowExecutableInspection>()));
        services.AddDataProtection();
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(LegacyRuntimeApiSelected) && !descriptor.IsKeyedService))
            throw new InvalidOperationException($"The shell must select either {FeatureName} or WorkflowsRuntimeApi, which share Workflow Runtime routes.");

        ConfigureKernelServices(services);
        services.AddScoped<IWorkflowRetainedRunSource>(serviceProvider => serviceProvider.GetRequiredService<KernelRunReader>());
        var links = RuntimeApiCapabilities.StaticDeclaration.Links.Where(link => link.Rel is
            "workflow-executables" or "workflow-executable" or "workflow-executable-provenance" or
            "workflow-activation-slots" or "workflow-activation-slot" or "workflow-instances" or "workflow-instances-page" or
            "workflow-instance" or "activity-execution").ToList();
        links.Add(new("activity-executions", "runtime/workflows/instances/{workflowExecutionId}/activity-executions", templated: true));
        services.AddApiCapability(new ApiCapabilityDeclaration(RuntimeApiCapabilities.CapabilityId, 1, links, FeatureName));
        services.AddApiCapabilitySource<OptionalRuntimeRoutesCapability>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        var (execute, delete, inputSources) = GetOptionalRoutes(endpoints.ServiceProvider);
        RuntimeHttpApi.MapRetainedEndpoints(endpoints, execute, delete, inputSources);
        KernelHistoryStream.Map(endpoints);
        KernelParkRecovery.Map(endpoints);
        KernelParkInspection.Map(endpoints);
    }

    private static void ConfigureKernelServices(IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(KernelRuns) && !descriptor.IsKeyedService))
            throw new InvalidOperationException(
                $"The {FeatureName} feature needs the host's {nameof(KernelRuns)} registration.");
        services.AddScoped<KernelRunReader>();
        services.TryAddScoped<IRuntimePreparedStandaloneRunReader, KernelPreparedStandaloneRunReader>();
        services.AddScoped<KernelActivityTimeline>();
        services.AddScoped<KernelHistoryCursor>();
        services.AddScoped<KernelPlaybackCursor>();
        services.AddApiCapabilitySource<KernelHistoryCapabilitySource>();
        services.AddApiCapabilitySource<KernelParkInspectionCapabilitySource>();
        ReplaceService(typeof(IWorkflowInstanceListService), typeof(KernelWorkflowInstanceListService));
        ReplaceService(typeof(IWorkflowInstanceDetailsService), typeof(KernelWorkflowInstanceDetailsService));
        ReplaceService(typeof(IActivityExecutionBrowseSource), typeof(KernelActivityExecutionBrowseSource));
        ReplaceService(typeof(IActivityExecutionInspectionService), typeof(KernelActivityExecutionInspectionService));
        ReplaceService(typeof(IRuntimeHistoryPageReader), typeof(KernelRuntimeHistoryPageReader));
        ReplaceService(typeof(IRuntimeHistoryOccurrencePageReader), typeof(KernelPlaybackPageReader));
        if (services.Any(descriptor => descriptor.ServiceType == typeof(KernelOperatorIngress) && !descriptor.IsKeyedService))
        {
            ReplaceService(typeof(IWorkflowInstanceDeletionService), typeof(KernelWorkflowInstanceDeletionService));
            services.TryAddSingleton<SelectedRunDeletionAvailable>();
            services.TryAddScoped<KernelParkRecovery>();
            services.AddApiCapabilitySource<KernelParkRecoveryCapabilitySource>();
        }
        ReplaceService(typeof(IWorkflowExecutableRetentionReader),
            typeof(KernelWorkflowExecutableRetentionReader));

        void ReplaceService(Type service, Type kernelImplementation)
        {
            foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == service && !descriptor.IsKeyedService).ToArray())
            {
                if (!RuntimeHttpInspectionServices.OwnsStoredReader(descriptor))
                    throw new InvalidOperationException(
                        $"The {FeatureName} feature replaces the shell's {service.Name} with the kernel's, and this shell registers another for it. " +
                        $"One implementation serves per shell: remove that registration, or leave the {FeatureName} feature out of the shell.");
                services.Remove(descriptor);
            }
            services.AddScoped(service, kernelImplementation);
        }
    }

    private static (bool Execute, bool Delete, bool InputSources) GetOptionalRoutes(IServiceProvider services)
    {
        var available = services.GetRequiredService<IServiceProviderIsService>();
        return (available.IsService(typeof(IWorkflowExecutionStartService)),
            available.IsService(typeof(SelectedRunDeletionAvailable)),
            services.GetService<IPermissionCatalog>()?.Find(WorkflowRuntimePermissions.WorkflowPublishingRead) is not null);
    }

    private sealed class OptionalRuntimeRoutesCapability(IServiceProvider services) : IApiCapabilitySource
    {
        public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            var (execute, delete, inputSources) = GetOptionalRoutes(services);
            var links = RuntimeApiCapabilities.StaticDeclaration.Links.Where(link =>
                (execute && link.Rel == "workflow-execute") ||
                (inputSources && link.Rel == "workflow-executable-input-sources")).ToList();
            if (delete)
                links.Add(new("workflow-instance-delete", "runtime/workflows/instances/{workflowExecutionId}", templated: true));
            return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(links.Count == 0
                ? []
                : [new(RuntimeApiCapabilities.CapabilityId, 1, links, FeatureName)]);
        }
    }
}
