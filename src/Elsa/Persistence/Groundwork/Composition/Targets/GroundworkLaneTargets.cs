using Elsa.Persistence.Groundwork.Composition;

namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// Resolves, at runtime, which Groundwork target a persistence lane was bound to.
/// <para>
/// Almost nothing needs this: an adapter is handed its lane's store at registration and never asks. The
/// exception is an operation that spans lanes — reusable-activity publication writes design, runtime, and
/// publishing documents — which has to know whether those lanes share a store before it can decide how to
/// commit.
/// </para>
/// </summary>
public sealed class GroundworkLaneTargets(GroundworkManifestBindings bindings)
{
    /// <summary>The target owning the lane declared by <typeparamref name="TManifestSource"/>.</summary>
    public string For<TManifestSource>()
        where TManifestSource : IGroundworkStorageManifestSource => For(typeof(TManifestSource));

    /// <summary>
    /// The target owning the lane whose manifest source is <paramref name="manifestSourceType"/>.
    /// <para>
    /// An unbound source resolves to the default target, which is intended — a lane that names no target
    /// belongs to the default one. That makes passing the wrong type indistinguishable from an unbound lane,
    /// so the type is checked here rather than silently answering "default" for, say, a manifest type.
    /// </para>
    /// </summary>
    public string For(Type manifestSourceType)
    {
        ArgumentNullException.ThrowIfNull(manifestSourceType);
        if (!typeof(IGroundworkStorageManifestSource).IsAssignableFrom(manifestSourceType))
        {
            throw new ArgumentException(
                $"'{manifestSourceType.FullName}' is not an {nameof(IGroundworkStorageManifestSource)}. " +
                "Lane targets are keyed by manifest source type; a manifest type resolves nothing.",
                nameof(manifestSourceType));
        }

        return bindings.TargetFor(manifestSourceType);
    }

    /// <summary>Whether every named lane resolves to the same target.</summary>
    public bool AreColocated(IReadOnlyDictionary<string, Type> lanesByName)
    {
        ArgumentNullException.ThrowIfNull(lanesByName);
        if (lanesByName.Count <= 1)
            return true;

        var targets = lanesByName.Values.Select(For).Distinct(StringComparer.Ordinal);
        return targets.Count() == 1;
    }

    /// <summary>A log-safe "lane → target" description for a diagnostic about a cross-lane operation.</summary>
    public string Describe(IReadOnlyDictionary<string, Type> lanesByName)
    {
        ArgumentNullException.ThrowIfNull(lanesByName);
        return string.Join(
            ", ",
            lanesByName
                .OrderBy(lane => lane.Key, StringComparer.Ordinal)
                .Select(lane => $"{lane.Key} → '{For(lane.Value)}'"));
    }
}
