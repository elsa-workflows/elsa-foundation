using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Inspection.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api;

/// <summary>The Runtime HTTP endpoint conventions and request authority shared by workflow execution backends.</summary>
internal static class WorkflowsRuntimeApiHttpServices
{
    internal static void AddWorkflowRuntimeApiHttpServices(this IServiceCollection services, bool includeLegacyUserTasks)
    {
        if (!includeLegacyUserTasks)
        {
            services.AddRetainedRuntimeHttpServices();
            return;
        }
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, WorkflowsRuntimeFaultRenderer>(WorkflowsRuntimeApi.OwnerId);
        services.AddRuntimeHttpServices<WorkflowsRuntimeJsonTypeInfoResolver, WorkflowRuntimePermissionContributor>();
    }

    // An earlier KernelApi package can still be paired with this package by NuGet.
    internal static void AddExecutableInspectionServices(this IServiceCollection services) =>
        RuntimeHttpInspectionServices.AddExecutableInspectionServices(services);

    internal static void AddRunInspectionServices(this IServiceCollection services,
        Func<IServiceProvider, IWorkflowRunInspection>? create = null) =>
        RuntimeHttpInspectionServices.AddRunInspectionServices(services, create);
}
