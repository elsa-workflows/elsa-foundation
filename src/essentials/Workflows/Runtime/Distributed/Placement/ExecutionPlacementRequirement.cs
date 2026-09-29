using Elsa.Cluster.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>
/// An execution's placement requirement (spec 184, FR-005): what its pinned executable needs from the runtime that runs
/// it, as the requirements <c>IRuntimeRequirementChecker</c> evaluates for it. It is one of three things: the check
/// subject of a pinned executable, nothing at all for an execution that has no pinned executable yet and is not being
/// started, or unresolved, with the reason.
/// </summary>
/// <remarks>
/// An unresolved requirement is never treated as satisfied (FR-016): the member does not claim the execution, and
/// reports it. An execution with no pinned executable anywhere, neither in durable state nor in a start command, runs no
/// executable node, so no module can be missing for it and it needs nothing.
/// </remarks>
public sealed record ExecutionPlacementRequirement
{
    private ExecutionPlacementRequirement(RuntimeRequirementCheckSubject? subject, string? unresolvedReason)
    {
        Subject = subject;
        UnresolvedReason = unresolvedReason;
    }

    /// <summary>The requirement of an execution with no pinned executable: nothing.</summary>
    public static ExecutionPlacementRequirement None { get; } = new(null, null);

    /// <summary>The requirement of a pinned executable.</summary>
    public static ExecutionPlacementRequirement Of(RuntimeRequirementCheckSubject subject) =>
        new(subject ?? throw new ArgumentNullException(nameof(subject)), null);

    /// <summary>A requirement that could not be resolved, with the reason.</summary>
    public static ExecutionPlacementRequirement Unresolved(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(null, reason);
    }

    /// <summary>The pinned executable's check subject, or <see langword="null"/> for <see cref="None"/> and an
    /// unresolved requirement.</summary>
    public RuntimeRequirementCheckSubject? Subject { get; }

    /// <summary>Why the requirement could not be resolved, or <see langword="null"/> when it was.</summary>
    public string? UnresolvedReason { get; }

    public bool IsResolved => UnresolvedReason is null;

    /// <summary>
    /// The membership requirement kinds this requirement translates into (FR-006), from a check of it: one per runtime
    /// consumer at its schema version, per storage driver and per activity type alias. They are how a member asks
    /// whether any other active member could run the work (FR-017); a member never asks about itself this way (FR-011).
    /// Each is asked of the database the execution lives in, when this shell's Runtime EF module has read its identity
    /// (FR-009): a member that serves another database cannot run it.
    /// </summary>
    public static IReadOnlyList<RunnabilityRequirement> ToMemberRequirements(RuntimeRequirementCheckResult check, string? databaseIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        return check.Requirements
            .Select(entry => (RunnabilityRequirement)new ActivatesRuntimeConsumer(entry.ConsumerKey, entry.SchemaVersion, databaseIdentity))
            .Concat(check.StorageDrivers.Select(entry => new HasStorageDriver(entry.DriverKey, databaseIdentity)))
            .Concat(check.ActivityTypes
                .Where(entry => !string.IsNullOrWhiteSpace(entry.TypeAlias))
                .Select(entry => new ResolvesActivityType(entry.TypeAlias, databaseIdentity)))
            .ToArray();
    }
}
