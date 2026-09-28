using Elsa.Cluster.Core.Models;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>Why a member refused to claim or renew placement of an execution (spec 184, FR-011, FR-012 and FR-016).</summary>
public enum PlacementRefusalKind
{
    /// <summary>The member is joining, draining or has left (FR-012).</summary>
    MemberNotActive,

    /// <summary>The shell's join sweep has not completed in this process (FR-012, FR-022).</summary>
    JoinSweepPending,

    /// <summary>The shell's runtime does not satisfy the execution's placement requirement (FR-011).</summary>
    RequirementUnmet,

    /// <summary>The execution's placement requirement could not be resolved (FR-016).</summary>
    RequirementUnresolved
}

/// <summary>
/// Whether this member may claim or renew placement of one execution now. A refusal is placement's decision, not a
/// runtime fault: it writes nothing and records no incident (FR-013).
/// </summary>
public sealed record PlacementDecision
{
    private PlacementDecision(PlacementRefusalKind? refusal, string? reason, IReadOnlyList<RunnabilityRequirement> requirements)
    {
        Refusal = refusal;
        Reason = reason;
        Requirements = requirements;
    }

    /// <summary>This member may claim or renew.</summary>
    public static PlacementDecision Runnable(IReadOnlyList<RunnabilityRequirement> requirements) => new(null, null, requirements);

    /// <summary>This member must not claim or renew, for <paramref name="kind"/>.</summary>
    public static PlacementDecision Refused(PlacementRefusalKind kind, string reason, IReadOnlyList<RunnabilityRequirement>? requirements = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(kind, reason, requirements ?? []);
    }

    /// <summary>The refusal, or <see langword="null"/> when this member may claim.</summary>
    public PlacementRefusalKind? Refusal { get; }

    /// <summary>A diagnostic naming what is unmet or unresolved, or why the member is not claiming. It never names another
    /// host.</summary>
    public string? Reason { get; }

    /// <summary>The execution's placement requirement as membership requirement kinds, when it was resolved and checked,
    /// so a member can ask whether any active member could run it (FR-017).</summary>
    public IReadOnlyList<RunnabilityRequirement> Requirements { get; }

    public bool IsRunnable => Refusal is null;

    /// <summary>Whether the refusal is about the work rather than the member: such work may be unplaceable (FR-017).</summary>
    public bool IsAboutRequirement => Refusal is PlacementRefusalKind.RequirementUnmet or PlacementRefusalKind.RequirementUnresolved;
}
