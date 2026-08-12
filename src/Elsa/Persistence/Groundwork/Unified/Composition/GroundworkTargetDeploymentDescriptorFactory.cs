using Elsa.Persistence.Groundwork.Targets;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// Turns a host's deployment schema source and its lane-to-target bindings into the descriptor
/// <c>Groundwork.Tool</c> reads.
/// <para>
/// This is the whole point of the descriptor: the runtime already knows which lane belongs to which target,
/// so it exports that rather than teaching a second process to work it out. Nothing here re-derives a
/// binding, and there is no separate notion of which lanes exist. Both come from the same two objects the
/// runtime composes from.
/// </para>
/// </summary>
public static class GroundworkTargetDeploymentDescriptorFactory
{
    /// <summary>
    /// Builds the descriptor for <paramref name="source"/> under <paramref name="bindings"/>.
    /// <para>
    /// A lane with no recorded binding belongs to <see cref="GroundworkTargetNames.Default"/>, which is the
    /// same rule composition applies, so a host that never named a target produces a one-target descriptor
    /// carrying the bare identity and the tool behaves exactly as it did before targets existed.
    /// </para>
    /// </summary>
    public static GroundworkTargetDeploymentDescriptor Create(
        GroundworkDeploymentSchemaManifestSource source,
        GroundworkManifestBindings? bindings = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var resolved = bindings ?? new GroundworkManifestBindings();
        var entries = source.GetManifestSourceTypes()
            .GroupBy(resolved.TargetFor, StringComparer.Ordinal)
            .Select(group => GroundworkTargetDeploymentEntry.Create(
                group.Key,
                GroundworkStorageCompositionDescriptor.IdentityFor(group.Key).Value,
                group.Select(GroundworkTargetDeploymentDescriptor.NameOf).ToArray()))
            .ToArray();

        return GroundworkTargetDeploymentDescriptor.Create(
            GroundworkTargetDeploymentDescriptor.NameOf(source.GetType()),
            entries);
    }

}
