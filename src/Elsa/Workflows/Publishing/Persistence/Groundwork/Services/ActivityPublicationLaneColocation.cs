using System.Collections.Frozen;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Targets;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Services;

/// <summary>
/// Guards the one Elsa operation that writes Design, Runtime, and Publishing documents in a single commit:
/// reusable-activity publication.
/// <para>
/// Groundwork has no cross-store transaction, so that commit only holds when the three lanes resolve to the
/// same target. A host that splits them gets a composition-time-shaped failure with the actual lane-to-target
/// mapping rather than a publication that writes runtime documents into the design database — which is what
/// would happen if the command simply used whichever store it was handed.
/// </para>
/// </summary>
public static class ActivityPublicationLaneColocation
{
    /// <summary>The lanes a reusable-activity publication commits together.</summary>
    private static readonly FrozenDictionary<string, Type> Lanes = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["activities design"] = typeof(ActivitiesDesignGroundworkStorageManifestSource),
        ["runtime"] = typeof(RuntimeGroundworkStorageManifestSource),
        ["publishing"] = typeof(PublishingGroundworkStorageManifestSource)
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Whether design, runtime, and publishing share one target, and so whether the publication can be one
    /// transaction. When they do not, the commands fall back to the ordered sequence described on
    /// <c>CommitAcrossTargetsAsync</c>.
    /// </summary>
    public static bool AreColocated(GroundworkLaneTargets laneTargets)
    {
        ArgumentNullException.ThrowIfNull(laneTargets);
        return laneTargets.AreColocated(Lanes);
    }

    /// <summary>A log-safe lane-to-target mapping, for diagnostics about a split publication.</summary>
    public static string Describe(GroundworkLaneTargets laneTargets)
    {
        ArgumentNullException.ThrowIfNull(laneTargets);
        return laneTargets.Describe(Lanes);
    }
}
