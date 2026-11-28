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
using Elsa.Workflows.Runtime.KernelHost;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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

    public void ConfigureServices(IServiceCollection services) => WorkflowsRuntimeKernelApiFeature.ConfigureRetainedServices(services);

    public void PostConfigureServices(IServiceCollection services)
    {
        if (HasService<LegacyRuntimeApiSelected>(services))
            throw new InvalidOperationException($"The shell must select either {FeatureName} or WorkflowsRuntimeApi, which share Workflow Runtime routes.");

        WorkflowsRuntimeKernelApiFeature.ConfigureKernelServices(services, FeatureName);
        var includeExecute = HasService<IWorkflowExecutionStartService>(services);
        var links = RuntimeApiCapabilities.StaticDeclaration.Links.Where(link => link.Rel is
            "workflow-executables" or "workflow-executable" or "workflow-executable-provenance" or
            "workflow-activation-slots" or "workflow-activation-slot" or "workflow-instances" or "workflow-instances-page" or
            "workflow-instance" or "activity-execution" || includeExecute && link.Rel == "workflow-execute").ToList();
        links.Add(new("activity-executions", "runtime/workflows/instances/{workflowExecutionId}/activity-executions", templated: true));
        if (HasService<SelectedRunDeletionAvailable>(services))
            links.Add(new("workflow-instance-delete", "runtime/workflows/instances/{workflowExecutionId}", templated: true));
        services.AddApiCapability(new ApiCapabilityDeclaration(RuntimeApiCapabilities.CapabilityId, 1, links, FeatureName));
        services.AddApiCapabilitySource<ExecutableInputSourcesCapability>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        var available = endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>();
        WorkflowsRuntimeKernelApiFeature.MapRetainedEndpoints(endpoints,
            includeExecute: available.IsService(typeof(IWorkflowExecutionStartService)),
            includeDelete: available.IsService(typeof(SelectedRunDeletionAvailable)),
            includeInputSources: HasPublishingRead(endpoints.ServiceProvider.GetService<IPermissionCatalog>()));
    }

    private static bool HasPublishingRead(IPermissionCatalog? catalog) =>
        catalog?.Find(WorkflowRuntimePermissions.WorkflowPublishingRead) is not null;

    private static bool HasService<T>(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ServiceType == typeof(T) && !descriptor.IsKeyedService);

    private sealed class ExecutableInputSourcesCapability(IPermissionCatalog? permissions = null) : IApiCapabilitySource
    {
        public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(HasPublishingRead(permissions)
                ? [new(RuntimeApiCapabilities.CapabilityId, 1,
                    [RuntimeApiCapabilities.StaticDeclaration.Links.Single(link => link.Rel == "workflow-executable-input-sources")],
                    FeatureName)]
                : []);
    }
}
