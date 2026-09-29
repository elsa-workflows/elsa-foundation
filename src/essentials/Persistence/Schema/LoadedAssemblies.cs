using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.Loader;

namespace Elsa.Persistence.Schema;

/// <summary>The assemblies loaded in this process, across every load context, less the ones a host no longer counts.</summary>
public static class LoadedAssemblies
{
    /// <summary>Nothing superseded: what a host that composes no <see cref="ISupersededAssemblySource"/> reads.</summary>
    public static IReadOnlySet<Assembly> NoneSuperseded => FrozenSet<Assembly>.Empty;

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

    /// <summary>
    /// What the readability report reads: every loaded assembly but those <paramref name="source"/> names retired, or every
    /// loaded assembly when the host composes no source.
    /// </summary>
    public static ValueTask<IReadOnlyList<Assembly>> ExceptRetiredAsync(ISupersededAssemblySource? source, CancellationToken cancellationToken = default) =>
        ExceptAsync(source?.GetRetiredAsync(cancellationToken));

    /// <summary>
    /// What the EF activation guard reads: every loaded assembly but those <paramref name="source"/> names replaced, or every
    /// loaded assembly when the host composes no source.
    /// </summary>
    public static ValueTask<IReadOnlyList<Assembly>> ExceptReplacedAsync(ISupersededAssemblySource? source, CancellationToken cancellationToken = default) =>
        ExceptAsync(source?.GetReplacedAsync(cancellationToken));

    private static async ValueTask<IReadOnlyList<Assembly>> ExceptAsync(ValueTask<IReadOnlySet<Assembly>>? superseded) =>
        Except(superseded is { } pending ? await pending : NoneSuperseded);
}
