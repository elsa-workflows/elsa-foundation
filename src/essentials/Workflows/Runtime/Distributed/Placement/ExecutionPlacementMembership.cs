using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>
/// What the placement pump needs to make placement a membership query and to act on the fleet (spec 184): the gate it
/// asks before claiming or renewing, the reclaimer, the unplaceable-work report, and this shell's runnability entry.
/// </summary>
public sealed class ExecutionPlacementMembership(
    ExecutionPlacementGate gate,
    HostIdReclaimer reclaimer,
    UnplaceableWorkRegistry unplaceable,
    ShellRunnabilityRegistry runnability,
    IServiceScopeFactory shellScopes)
{
    public ExecutionPlacementGate Gate { get; } = gate ?? throw new ArgumentNullException(nameof(gate));

    public HostIdReclaimer Reclaimer { get; } = reclaimer ?? throw new ArgumentNullException(nameof(reclaimer));

    public UnplaceableWorkRegistry Unplaceable { get; } = unplaceable ?? throw new ArgumentNullException(nameof(unplaceable));

    public ShellRunnabilityRegistry Runnability { get; } = runnability ?? throw new ArgumentNullException(nameof(runnability));

    /// <summary>Computes this shell's runnability entry from its registries now (FR-008).</summary>
    public async ValueTask<RunnabilityEntry> ComputeRunnabilityAsync()
    {
        await using var scope = shellScopes.CreateAsyncScope();
        return RunnabilityEntryFactory.Create(scope.ServiceProvider);
    }

    /// <summary>
    /// The host ids a view shows departed (spec 184, Terms): no live incarnation, and a latest incarnation, the one not
    /// displaced, that left or expired. A host id absent from the view, or with a live incarnation, is not departed, and
    /// neither is this member's own host id.
    /// </summary>
    public static IReadOnlyList<ClusterMemberIdentity> Departed(FleetView view, string selfHostId) =>
        view.Members
            .Where(member => !StringComparer.Ordinal.Equals(member.HostId, selfHostId))
            .GroupBy(member => member.HostId, StringComparer.Ordinal)
            .Where(incarnations => !incarnations.Any(member => member.IsLive))
            .Select(incarnations => incarnations.Where(member => !member.IsDisplaced).ToArray())
            .Where(current => current.Length == 1)
            .Select(current => current[0].Identity)
            .ToArray();
}
