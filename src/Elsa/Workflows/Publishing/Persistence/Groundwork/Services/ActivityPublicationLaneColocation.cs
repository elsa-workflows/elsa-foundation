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
internal static class ActivityPublicationLaneColocation
{
    /// <summary>The lanes a reusable-activity publication commits together.</summary>
    private static readonly Dictionary<string, Type> Lanes = new(StringComparer.Ordinal)
    {
        ["activities design"] = typeof(ActivitiesDesignGroundworkStorageManifestSource),
        ["runtime"] = typeof(RuntimeGroundworkStorageManifestSource),
        ["publishing"] = typeof(PublishingGroundworkStorageManifestSource)
    };

    /// <summary>Throws unless design, runtime, and publishing share one target.</summary>
    public static void EnsureColocated(GroundworkLaneTargets laneTargets, string operation)
    {
        ArgumentNullException.ThrowIfNull(laneTargets);
        if (laneTargets.AreColocated(Lanes))
            return;

        throw new ActivityPublicationLaneSplitException(
            $"{operation} commits design, runtime, and publishing documents in one Groundwork transaction, " +
            $"but those lanes are on different targets ({laneTargets.Describe(Lanes)}). Groundwork has no " +
            "cross-store transaction, so this publication cannot be made atomic as composed. Put those three " +
            "lanes on one target, or disable reusable-activity publishing on this host.");
    }
}

/// <summary>
/// Raised when reusable-activity publication is composed across Groundwork targets it cannot commit
/// atomically. Splitting the lanes is otherwise supported; only this one operation requires co-location.
/// </summary>
public sealed class ActivityPublicationLaneSplitException(string message) : InvalidOperationException(message);
