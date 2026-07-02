using System.Reflection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Core.Middleware;

/// <summary>
/// Registration helpers for contributing middleware to the runtime execution pipelines. Each call is atomic: it both
/// registers the middleware type in DI (so the pipeline can resolve it by type) and records a placement contribution
/// (so the pipeline builder knows where to slot it). Placement comes from the explicit arguments, falling back to the
/// type's <see cref="RuntimeMiddlewareAttribute"/>; the same mechanism serves framework built-ins and third-party modules.
/// </summary>
public static class RuntimeMiddlewareServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TMiddleware"/> and places it in the workflow execution pipeline. Slot/order/name
    /// default to the type's <see cref="RuntimeMiddlewareAttribute"/> and can be overridden per registration.
    /// </summary>
    public static IServiceCollection AddWorkflowRuntimeMiddleware<TMiddleware>(
        this IServiceCollection services,
        string? slot = null,
        int? order = null,
        string? name = null)
        where TMiddleware : class, IWorkflowRuntimeMiddleware
    {
        ArgumentNullException.ThrowIfNull(services);

        var (resolvedSlot, resolvedOrder, resolvedName) = ResolvePlacement(typeof(TMiddleware), slot, order, name);
        services.TryAddSingleton<TMiddleware>();
        services.AddSingleton(new WorkflowRuntimeMiddlewareContribution(typeof(TMiddleware), resolvedSlot, resolvedOrder, resolvedName));
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TMiddleware"/> and places it in the activity execution pipeline. Slot/order/name
    /// default to the type's <see cref="RuntimeMiddlewareAttribute"/> and can be overridden per registration.
    /// </summary>
    public static IServiceCollection AddActivityRuntimeMiddleware<TMiddleware>(
        this IServiceCollection services,
        string? slot = null,
        int? order = null,
        string? name = null)
        where TMiddleware : class, IActivityRuntimeMiddleware
    {
        ArgumentNullException.ThrowIfNull(services);

        var (resolvedSlot, resolvedOrder, resolvedName) = ResolvePlacement(typeof(TMiddleware), slot, order, name);
        services.TryAddSingleton<TMiddleware>();
        services.AddSingleton(new ActivityRuntimeMiddlewareContribution(typeof(TMiddleware), resolvedSlot, resolvedOrder, resolvedName));
        return services;
    }

    private static (string Slot, int Order, string? Name) ResolvePlacement(Type middlewareType, string? slot, int? order, string? name)
    {
        var attribute = middlewareType.GetCustomAttribute<RuntimeMiddlewareAttribute>();

        var resolvedSlot = slot ?? attribute?.Slot
            ?? throw new InvalidOperationException(
                $"Runtime middleware '{middlewareType.Name}' has no slot: pass a slot to the Add*RuntimeMiddleware call or annotate the type with [RuntimeMiddleware(slot)].");

        return (resolvedSlot, order ?? attribute?.Order ?? 0, name ?? attribute?.Name);
    }
}
