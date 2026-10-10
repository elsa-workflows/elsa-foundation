using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>
/// Whether a newer generation of the shell a distributed runtime runs in is active: what tells a shell reload from a stop
/// (spec 184, FR-021 and FR-022).
/// </summary>
/// <remarks>
/// <para>
/// The leases a runtime holds are its host id's, not its shell generation's. A reload promotes the new generation to
/// active before it drains the old one (<see cref="IShellRegistry.ReloadAsync"/>), so while the old generation's tasks
/// stop, the new generation already runs and its placement pump renews those leases: the old generation must leave them
/// alone. When the host stops, or the shell is removed, no newer generation is active and nothing would renew them, so
/// the stopping pump hands them off.
/// </para>
/// <para>
/// A stop is not judged by the host's <c>IHostApplicationLifetime</c>. A shell container is built from copies of the
/// host's registrations, and the host registers its lifetime by type, so the lifetime a shell resolves is a copy of its
/// own whose <c>ApplicationStopping</c> never fires. Reaching the host's instance would take a relay every host has to
/// compose, and a host that forgot it would never hand anything off, without a sign. A runtime composed outside CShells
/// has no generations and no reload, so each of its stops is a real one.
/// </para>
/// </remarks>
public sealed class ShellSuccession(IShellRegistry? registry, Func<IShell?> shell)
{
    /// <summary>The succession of the shell whose container <paramref name="services"/> is, or of none outside CShells.</summary>
    /// <remarks>The shell is read when asked, not now: CShells hands a shell out only once its construction completed.</remarks>
    public static ShellSuccession From(IServiceProvider services) =>
        new(services.GetService<IShellRegistry>(), services.GetService<IShell>);

    /// <summary>Whether a newer generation of this shell is active now.</summary>
    public bool HasSuccessor =>
        registry is not null
        && shell() is { } current
        && registry.GetActive(current.Descriptor.Name) is { } active
        && active.Descriptor.Generation > current.Descriptor.Generation;
}
