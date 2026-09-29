using System.Reflection;
using System.Runtime.Loader;
using Elsa.Persistence.Schema;

namespace Elsa.Modularity.EntityFramework;

/// <summary>
/// The assemblies the activation guard reads <c>[EfModule]</c> and <c>[UsesEfModule]</c> declarations
/// from. A seam rather than a fixed call, because what "this host's modules" means differs between a
/// plain host and one whose packages arrive through Nuplane.
/// </summary>
public interface IEfModuleAssemblySource
{
    ValueTask<IReadOnlyList<Assembly>> GetAssembliesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Every assembly loaded into this process, across every <see cref="AssemblyLoadContext"/> — the same set
/// the persistence tooling discovers modules from, and for the same reason: a Nuplane
/// <c>HostIntegrated</c> package graph loads into a context of its own rather than the default one, so
/// <c>AppDomain.CurrentDomain.GetAssemblies()</c> would silently miss exactly the modules a package
/// installed later, which is the case the guard exists for — less every assembly the host's
/// <see cref="ISupersededAssemblySource"/> says a newer generation replaced (spec 183, FR-021, amended 2026-09-29).
/// </summary>
/// <remarks>
/// <para>
/// Read on every evaluation rather than cached: a reconcile can load a module's assembly between two
/// apply calls, and a cache would keep answering from before it did.
/// </para>
/// <para>
/// The guard judges the shell generation an apply is about to build, and that generation composes the active
/// package set, so a replaced generation stops counting here at once, even while a shell generation still running
/// it drains; the readability report keeps counting it until that shell is gone. Without the exclusion, an upgraded
/// module's two loaded generations declare the same module twice, and the catalog refuses every apply until a restart.
/// A host that composes no source excludes nothing.
/// </para>
/// </remarks>
public sealed class LoadedEfModuleAssemblySource(ISupersededAssemblySource? superseded = null) : IEfModuleAssemblySource
{
    public ValueTask<IReadOnlyList<Assembly>> GetAssembliesAsync(CancellationToken cancellationToken = default) =>
        LoadedAssemblies.ExceptReplacedAsync(superseded, cancellationToken);
}
