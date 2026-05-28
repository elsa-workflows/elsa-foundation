using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Default in-memory implementation of <see cref="IActivityImplementationResolverRegistry"/>.
/// Single instance per host. Duplicate-<c>Kind</c> registration throws.
/// </summary>
public sealed class ActivityImplementationResolverRegistry : IActivityImplementationResolverRegistry
{
    private readonly Dictionary<string, IActivityImplementationResolver> _resolvers = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public void RegisterAll(IEnumerable<IActivityImplementationResolver> resolvers)
    {
        lock (_gate)
        {
            foreach (var resolver in resolvers)
            {
                if (_resolvers.TryGetValue(resolver.Kind, out var existing) && existing.GetType() != resolver.GetType())
                    throw new InvalidOperationException(
                        $"Activity implementation resolver kind '{resolver.Kind}' is already registered with '{existing.GetType().FullName}'; refusing to overwrite with '{resolver.GetType().FullName}'.");

                _resolvers[resolver.Kind] = resolver;
            }
        }
    }

    public Type Resolve(IImplementationDescriptor descriptor)
    {
        IActivityImplementationResolver? resolver;
        lock (_gate)
        {
            if (!_resolvers.TryGetValue(descriptor.Kind, out resolver))
                throw new ActivityResolutionException(
                    $"No activity implementation resolver registered for kind '{descriptor.Kind}'.");
        }

        // Dispatch into the kind-typed Resolve via reflection — the resolver is registered
        // behind the non-generic marker but its concrete generic interface exposes Resolve(TDescriptor).
        var resolverType = resolver.GetType();
        var iface = resolverType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IActivityImplementationResolver<>))
            ?? throw new ActivityResolutionException(
                $"Resolver '{resolverType.FullName}' for kind '{descriptor.Kind}' does not implement IActivityImplementationResolver<TDescriptor>.");

        var method = iface.GetMethod(nameof(IActivityImplementationResolver<IImplementationDescriptor>.Resolve))
            ?? throw new ActivityResolutionException(
                $"Resolver '{resolverType.FullName}' for kind '{descriptor.Kind}' has no Resolve method.");

        try
        {
            return (Type)method.Invoke(resolver, [descriptor])!;
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw new ActivityResolutionException(
                $"Resolver '{resolverType.FullName}' for kind '{descriptor.Kind}' threw while resolving: {tie.InnerException.Message}");
        }
    }
}
