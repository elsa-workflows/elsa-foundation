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
    /// <summary>The store backing the lane declared by <typeparamref name="TManifestSource"/>.</summary>
    public IDocumentStore For<TManifestSource>()
        where TManifestSource : IGroundworkStorageManifestSource => For(typeof(TManifestSource));

    /// <summary>
    /// The bounded-query store backing that lane. Resolved by its own contract rather than cast from the
    /// document store: the scoped Groundwork adapter satisfies both, but a host-supplied raw provider store
    /// need not, and silently treating one as the other is how a lane ends up querying nothing.
    /// </summary>
    public IBoundedDocumentStore BoundedFor<TManifestSource>()
        where TManifestSource : IGroundworkStorageManifestSource
    {
        var target = laneTargets.For(typeof(TManifestSource));
        var keyed = serviceProvider.GetKeyedService<IBoundedDocumentStore>(target);
        if (keyed is not null)
            return keyed;

        if (!GroundworkTargetNames.IsDefault(target))
        {
            throw new InvalidOperationException(
                $"Groundwork target '{target}' has no bounded document store, so the lane declared by " +
                $"'{typeof(TManifestSource).Name}' cannot run its admitted queries.");
        }

        return serviceProvider.GetService<IBoundedDocumentStore>()
               ?? For<TManifestSource>() as IBoundedDocumentStore
               ?? throw new InvalidOperationException(
                   "The default Groundwork target has no bounded document store.");
    }

    /// <summary>The store backing the lane whose manifest source is <paramref name="manifestSourceType"/>.</summary>
    public IDocumentStore For(Type manifestSourceType)
    {
        ArgumentNullException.ThrowIfNull(manifestSourceType);
        var target = laneTargets.For(manifestSourceType);
        var keyed = serviceProvider.GetKeyedService<IDocumentStore>(target);
        if (keyed is not null)
            return keyed;

        if (!GroundworkTargetNames.IsDefault(target))
        {
            throw new InvalidOperationException(
                $"Groundwork target '{target}' has no document store, so the lane declared by " +
                $"'{manifestSourceType.Name}' cannot be reached. Declare the target on a provider feature.");
        }

        // The default lane may be served by a host-supplied ambient store, exactly as the registrar allows.
        return serviceProvider.GetRequiredService<IDocumentStore>();
    }
}
