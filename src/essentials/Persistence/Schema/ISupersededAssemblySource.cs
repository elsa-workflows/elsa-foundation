using System.Reflection;
using System.Runtime.Loader;

namespace Elsa.Persistence.Schema;

/// <summary>
/// The assemblies loaded in this process that an earlier generation of a package left behind (spec 183, FR-021, amended
/// 2026-09-29). A load context a package runtime cannot unload keeps an old generation's assemblies in
/// <see cref="AssemblyLoadContext.All"/> for the life of the process, so a reader of every loaded declaration would keep
/// reading them after the host moved on: its readability report would never credit a version only the new generation
/// reads, and the EF module catalog would see one module declared twice.
/// </summary>
/// <remarks>
/// <para>
/// A source names only what it has positive evidence for and nothing else, so every mistake it can make is the
/// conservative one: an assembly it does not name keeps counting. A host that composes none has nothing superseded, and
/// reads every load context as before.
/// </para>
/// <para>
/// It lives in <c>Elsa.Persistence.Schema</c>, beside the declaration reader it narrows, because both the readability
/// report (<c>Elsa.Cluster.Readability</c>) and the EF activation guard (<c>Elsa.Modularity.EntityFramework</c>) read
/// through it, and neither references the other.
/// </para>
/// </remarks>
public interface ISupersededAssemblySource
{
    /// <summary>
    /// Every loaded assembly a newer generation of the same assembly has replaced in the host's active package set: what
    /// the next shell generation the host builds will not compose. A generation still running may still execute one.
    /// </summary>
    ValueTask<IReadOnlySet<Assembly>> GetReplacedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The replaced assemblies nothing in the host can execute any more: no shell generation that has not been disposed
    /// composes a feature from a replaced assembly in the same load context. While two generations are both live, both
    /// still count.
    /// </summary>
    ValueTask<IReadOnlySet<Assembly>> GetRetiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>The assemblies loaded in this process, across every load context, less the ones a host no longer counts.</summary>
public static class LoadedAssemblies
{
    /// <summary>Nothing superseded: what a host that composes no <see cref="ISupersededAssemblySource"/> reads.</summary>
    public static IReadOnlySet<Assembly> NoneSuperseded { get; } = new HashSet<Assembly>();

    /// <summary>
    /// Every assembly loaded in this process, across every <see cref="AssemblyLoadContext"/> - a package's load context as
    /// much as the default one - except those in <paramref name="superseded"/>. Read afresh on every call: a reconcile can
    /// load or replace a package between two.
    /// </summary>
    public static IReadOnlyList<Assembly> Except(IReadOnlySet<Assembly> superseded)
    {
        ArgumentNullException.ThrowIfNull(superseded);
        return [.. AssemblyLoadContext.All.SelectMany(context => context.Assemblies).Distinct().Where(assembly => !superseded.Contains(assembly))];
    }
}
