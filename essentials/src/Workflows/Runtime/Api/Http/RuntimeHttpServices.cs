using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Core.Authorization;
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

internal sealed class LegacyRuntimeApiSelected;

/// <summary>The Runtime HTTP conventions and request authority shared by workflow execution backends.</summary>
internal static class RuntimeHttpServices
{
    internal static void AddRetainedRuntimeHttpServices(this IServiceCollection services) =>
        services.AddRuntimeHttpServices<RuntimeHttpJsonTypeInfoResolver, RetainedWorkflowRuntimePermissionContributor>();

    internal static void AddRuntimeHttpServices<TJsonResolver, TPermissionContributor>(this IServiceCollection services)
        where TJsonResolver : class, IJsonTypeInfoResolver, new()
        where TPermissionContributor : class, IPermissionContributor
    {
        services.AddElsaEndpoints();
        services.AddHttpContextAccessor();
        services.AddDynamicEndpointApiExplorerRefresh();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<RuntimeHistoryOccurrenceAuthorization>();

        services.TryAddKeyedSingleton<IEndpointProblemWriter, WorkflowsRuntimeProblemWriter>(RuntimeHttpApi.OwnerId);
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, RuntimeHttpFaultRenderer>(RuntimeHttpApi.OwnerId);
        services.ConfigureHttpJsonOptions(options =>
        {
            if (!options.SerializerOptions.TypeInfoResolverChain.Any(resolver => resolver is TJsonResolver))
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, new TJsonResolver());
        });

        services.TryAddScoped<HttpContextActivityExecutionInspectionAuthorizationContext>();
        services.TryAddScoped<IActivityInspectionContextAsync>(sp => sp.GetRequiredService<HttpContextActivityExecutionInspectionAuthorizationContext>());
        services.EnsureReplacementContract<IActivityInspectionContextAsync, HttpContextActivityExecutionInspectionAuthorizationContext>();
        services.TryAddScoped<HttpContextWorkflowRuntimeOperatorAccess>();
        services.TryAddScoped<IWorkflowRuntimeOperatorAccess>(sp => sp.GetRequiredService<HttpContextWorkflowRuntimeOperatorAccess>());
        services.AddPermissionContributor<TPermissionContributor>();
    }
}

internal sealed class RuntimeHttpJsonTypeInfoResolver : IJsonTypeInfoResolver
{
    private readonly RuntimeHttpJsonContext _context = new();

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
        ((IJsonTypeInfoResolver)_context).GetTypeInfo(type, options);
}
