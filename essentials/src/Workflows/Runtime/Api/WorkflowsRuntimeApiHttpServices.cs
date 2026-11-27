using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Inspection.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api;

/// <summary>The Runtime HTTP endpoint conventions and request authority shared by workflow execution backends.</summary>
internal static class WorkflowsRuntimeApiHttpServices
{
    internal static void AddWorkflowRuntimeApiHttpServices(this IServiceCollection services)
    {
        services.AddElsaEndpoints();
        services.AddHttpContextAccessor();
        services.AddDynamicEndpointApiExplorerRefresh();

        // Keyed failure services preserve each module's response shape on a shared host.
        services.TryAddKeyedSingleton<IEndpointProblemWriter, WorkflowsRuntimeProblemWriter>(WorkflowsRuntimeApi.OwnerId);
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, WorkflowsRuntimeFaultRenderer>(WorkflowsRuntimeApi.OwnerId);
        services.ConfigureHttpJsonOptions(options =>
        {
            if (!options.SerializerOptions.TypeInfoResolverChain.Any(resolver => resolver is WorkflowsRuntimeJsonTypeInfoResolver))
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, new WorkflowsRuntimeJsonTypeInfoResolver());
        });

        services.TryAddScoped<HttpContextActivityExecutionInspectionAuthorizationContext>();
        services.TryAddScoped<IActivityInspectionContextAsync>(sp => sp.GetRequiredService<HttpContextActivityExecutionInspectionAuthorizationContext>());
        services.EnsureReplacementContract<IActivityInspectionContextAsync, HttpContextActivityExecutionInspectionAuthorizationContext>();
        services.TryAddScoped<HttpContextWorkflowRuntimeOperatorAccess>();
        services.TryAddScoped<IWorkflowRuntimeOperatorAccess>(sp => sp.GetRequiredService<HttpContextWorkflowRuntimeOperatorAccess>());
        services.AddPermissionContributor<WorkflowRuntimePermissionContributor>();
    }
}
