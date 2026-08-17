using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// Resolves the document store backing a persistence lane, for the few operations that span lanes and so
/// cannot simply be handed one store at registration.
/// <para>
/// Ordinary adapters never use this: the lane registrar binds them to their target's store. Only an operation
/// writing several lanes needs to address each one, and only then when the lanes are on different targets.
/// </para>
/// </summary>
public sealed class GroundworkLaneStores(IServiceProvider serviceProvider, GroundworkLaneTargets laneTargets)
{
    /// <summary>The store backing the lane identified by <typeparamref name="TLane"/>.</summary>
    public IDocumentStore For<TLane>()
        where TLane : IGroundworkStorageLane => For(typeof(TLane));

    /// <summary>
    /// The bounded-query store backing that lane. Resolved by its own contract rather than cast from the
    /// document store: the scoped Groundwork adapter satisfies both, but a host-supplied raw provider store
    /// need not, and silently treating one as the other is how a lane ends up querying nothing.
    /// </summary>
    public IBoundedDocumentStore BoundedFor<TLane>()
        where TLane : IGroundworkStorageLane
    {
        var target = laneTargets.For(typeof(TLane));
        var keyed = serviceProvider.GetKeyedService<IBoundedDocumentStore>(target);
        if (keyed is not null)
            return keyed;

        if (!GroundworkTargetNames.IsDefault(target))
        {
            throw new InvalidOperationException(
                $"Groundwork target '{target}' has no bounded document store, so the lane identified by " +
                $"'{typeof(TLane).Name}' cannot run its admitted queries.");
        }

        return serviceProvider.GetService<IBoundedDocumentStore>()
               ?? For<TLane>() as IBoundedDocumentStore
               ?? throw new InvalidOperationException(
                   "The default Groundwork target has no bounded document store.");
    }

    /// <summary>The store backing the lane identified by <paramref name="laneType"/>.</summary>
    public IDocumentStore For(Type laneType)
    {
        ArgumentNullException.ThrowIfNull(laneType);
        var target = laneTargets.For(laneType);
        var keyed = serviceProvider.GetKeyedService<IDocumentStore>(target);
        if (keyed is not null)
            return keyed;

        if (!GroundworkTargetNames.IsDefault(target))
        {
            throw new InvalidOperationException(
                $"Groundwork target '{target}' has no document store, so the lane declared by " +
                $"'{laneType.Name}' cannot be reached. Declare the target on a provider feature.");
        }

        // The default lane may be served by a host-supplied ambient store, exactly as the registrar allows.
        return serviceProvider.GetRequiredService<IDocumentStore>();
    }
}
