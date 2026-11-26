using CShells;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Readability;

public static class ShellServiceSharingExtensions
{
    /// <summary>
    /// Makes every shell container resolve <typeparamref name="TService"/> to the host's own instance, for a service the host
    /// composes on its own container and another package registered by type or by factory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CShells builds each shell container from copies of the host's registrations, and a registration made by type or by factory
    /// is instantiated again there. For a service that holds the host's own state that second instance is wrong: Nuplane's
    /// reconcile trigger queue is read only by the dispatcher the host runs, so a reconcile enqueued on a shell's copy waits for
    /// ever (#2159). A registration made by instance is shared already, which is how the finalization observations and
    /// <see cref="NuplanePackageGenerations"/> are composed; this is for the registrations that cannot be, because the host does
    /// not own them.
    /// </para>
    /// <para>
    /// The registration that would have been used becomes a factory that builds the service as before in the host's own
    /// container and, in a shell container, returns what the host's container resolves for the same type. It finds the host's
    /// container through the CShells lifecycle subscriber registered beside it, which CShells builds from the host's container
    /// alone, before it builds any shell (as <see cref="NuplanePackageGenerations.BindTo"/> relies on, through the same host capture).
    /// </para>
    /// <para>
    /// A container disposes what its factories return, so a shell that is drained would dispose the host's instance. The service
    /// therefore must not be disposable: a registration by type that is, is refused here, and any instance that is, however it was
    /// registered, is refused where it is created or handed to a shell, which is the first resolution during the host's boot.
    /// The service must be registered already, as a singleton, and is left alone if it is shared already. Only singletons can be
    /// shared: a scoped or transient service has no one instance to share.
    /// </para>
    /// </remarks>
    public static IServiceCollection ShareWithShells<TService>(this IServiceCollection services)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        var index = services.ToList().FindLastIndex(descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService);
        if (index < 0)
            throw new InvalidOperationException($"{typeof(TService).Name} is not registered, so there is nothing to share with the shells. Register it first.");

        var registered = services[index];
        if (registered.ImplementationFactory?.Target is SharedRegistration)
            return services;
        if (registered.Lifetime != ServiceLifetime.Singleton)
            throw new InvalidOperationException($"{typeof(TService).Name} is registered as {registered.Lifetime}. Only a singleton can be the host's one instance.");
        if (registered.ImplementationType is { } type && (typeof(IDisposable).IsAssignableFrom(type) || typeof(IAsyncDisposable).IsAssignableFrom(type)))
            throw new InvalidOperationException($"{type.Name} is disposable, and a shell container would dispose the host's instance when it is drained.");

        services[index] = ServiceDescriptor.Singleton(typeof(TService), new SharedRegistration(HostContainerOn(services), registered).Resolve);
        return services;
    }

    /// <summary>The one <see cref="HostContainer"/> this collection's shared services find the host through, registered on first use.</summary>
    private static HostContainer HostContainerOn(IServiceCollection services)
    {
        if (services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(HostContainer) && !descriptor.IsKeyedService)?.ImplementationInstance is HostContainer existing)
            return existing;

        var host = new HostContainer();
        services.AddSingleton(host);
        services.AddSingleton<IShellLifecycleSubscriber>(root => host.BindTo(root));
        return host;
    }

    private sealed class SharedRegistration(HostContainer host, ServiceDescriptor registered)
    {
        public object Resolve(IServiceProvider services)
        {
            // Only a shell container holds its own ShellSettings; the host's container never does.
            if (services.GetService<ShellSettings>() is null)
                return NotDisposable(Create(services));

            var root = host.Root ?? throw new InvalidOperationException($"A shell resolved {registered.ServiceType.Name} before CShells bound the host's container, so there is no host instance to return.");
            return NotDisposable(root.GetRequiredService(registered.ServiceType));
        }

        private object Create(IServiceProvider services) =>
            registered.ImplementationInstance
            ?? registered.ImplementationFactory?.Invoke(services)
            ?? ActivatorUtilities.CreateInstance(services, registered.ImplementationType!);

        /// <summary>
        /// A container disposes what its factories return, so a disposable host instance handed to a shell would be disposed
        /// when that shell is drained. Checked where the instance is created and again where a shell receives it, so the host's
        /// composition fails at its first resolution instead of a drain disposing a service the host still uses.
        /// </summary>
        private object NotDisposable(object instance) => instance is IDisposable or IAsyncDisposable
            ? throw new InvalidOperationException($"{instance.GetType().Name}, shared with the shells as {registered.ServiceType.Name}, is disposable, and a shell container would dispose the host's instance when it is drained. Share only a service that owns nothing to dispose.")
            : instance;
    }
}
