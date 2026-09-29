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
/// through it, and neither references the other. Both read through <see cref="LoadedAssemblies"/>.
/// </para>
/// </remarks>
public interface ISupersededAssemblySource
{
    /// <summary>
    /// Every loaded assembly a newer generation of the same assembly has replaced in the host's active package set: what
    /// the next shell generation the host builds from its active package set will not compose. A generation still
    /// running, or one built from a feature catalog not yet refreshed, may still execute one.
    /// </summary>
    ValueTask<IReadOnlySet<Assembly>> GetReplacedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The replaced assemblies nothing in the host can execute any more: no shell generation whose container has not
    /// finished disposing composes a feature from their load context, and the feature catalog the next shell generation
    /// is built from names neither them nor a feature in their load context. While two generations are both live, both
    /// still count.
    /// </summary>
    ValueTask<IReadOnlySet<Assembly>> GetRetiredAsync(CancellationToken cancellationToken = default);
}
