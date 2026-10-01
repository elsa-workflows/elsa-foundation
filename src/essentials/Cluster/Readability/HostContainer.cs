using CShells.Lifecycle;

namespace Elsa.Cluster.Readability;

/// <summary>
/// The host's own container, captured once CShells has said which it is: the one place this assembly learns it, for
/// <see cref="NuplanePackageGenerations"/> and <see cref="ShellServiceSharingExtensions"/>, which both hold a host-level object that
/// every shell container CShells builds from copies of the host's registrations must reach the host's own services through.
/// </summary>
/// <remarks>
/// CShells resolves its lifecycle subscribers from the host container alone, before it builds any shell, and never copies them
/// into one. Registering this as a subscriber, bound by <see cref="BindTo"/>, therefore hands it the host's container; the first
/// binding wins, and any later one, from whatever container, changes nothing. It listens for no lifecycle change itself.
/// </remarks>
internal sealed class HostContainer : IShellLifecycleSubscriber
{
    private IServiceProvider? _root;

    /// <summary>The host's container, or <see langword="null"/> until CShells has resolved this as a subscriber.</summary>
    public IServiceProvider? Root => Volatile.Read(ref _root);

    /// <summary>Binds the host's container, returning whether this call was the binding that took.</summary>
    public bool Bind(IServiceProvider root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Interlocked.CompareExchange(ref _root, root, null) is null;
    }

    /// <summary>Binds the container and returns this as the subscriber CShells registers.</summary>
    public IShellLifecycleSubscriber BindTo(IServiceProvider root)
    {
        Bind(root);
        return this;
    }

    public Task OnStateChangedAsync(IShell shell, ShellLifecycleState previous, ShellLifecycleState current, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
