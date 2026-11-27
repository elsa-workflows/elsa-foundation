using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Elsa.Events.Core.Extensions;

/// <summary>
/// Event-handler registration — the single source of truth for how an <see cref="IEventHandler{TEvent}"/>
/// is wired into the container.
/// </summary>
/// <remarks>
/// The dispatch contract (see <c>Elsa.Events/EXTENSION_POINTS.md</c>): the pipeline resolves handlers ONLY
/// through the closed generic <see cref="IEventHandler{TEvent}"/>. Every registration therefore records the
/// handler under both the closed generic (the dispatch path) and the non-generic <see cref="IEventHandler"/>
/// marker (kept for scan/registration-count checks). Registration is idempotent per (event, handler) pair, so
/// a handler reached by an explicit call and an assembly scan still dispatches exactly once — the pipeline's
/// de-dup is defence-in-depth, not the guarantee. Registering only the bare marker will NOT dispatch.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="THandler"/> as a subscriber to <typeparamref name="TEvent"/> under both
    /// the closed generic dispatch interface and the non-generic marker. Idempotent per (event, handler) pair.
    /// </summary>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IEvent
        where THandler : class, IEventHandler<TEvent>
        => services.TryAddEventHandler<TEvent, THandler>();

    /// <summary>
    /// Idempotently registers <typeparamref name="THandler"/> for <typeparamref name="TEvent"/> under both the
    /// closed generic dispatch interface and the non-generic marker. Prefer this for aggregating handlers that
    /// several features may each try to register (e.g. the EF Core entity saving/loading aggregators), where a
    /// plain additive registration would land the same handler more than once.
    /// </summary>
    public static IServiceCollection TryAddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHandler<TEvent>, THandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHandler, THandler>());
        return services;
    }

    /// <summary>
    /// Scans <paramref name="assembly"/> for <see cref="IEventHandler{TEvent}"/> implementations and registers
    /// each under both its closed generic dispatch interface and the non-generic marker, idempotently.
    /// </summary>
    public static IServiceCollection AddEventHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.DefinedTypes)
        {
            var closedGenerics = type
                .GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>));

            foreach (var closedGeneric in closedGenerics)
            {
                services.TryAddEnumerable(ServiceDescriptor.Scoped(closedGeneric, type));
                services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IEventHandler), type));
            }
        }

        return services;
    }
}
