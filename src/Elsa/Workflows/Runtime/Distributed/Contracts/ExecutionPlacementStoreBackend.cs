namespace Elsa.Workflows.Runtime.Distributed.Contracts;

/// <summary>
/// Records the placement-store owner selected for a distributed runtime composition.
/// </summary>
/// <remarks>
/// The distributed feature has a process-local default, while persistence leaves may replace
/// only placement and leave the command transport untouched. Keeping this marker in the leaf's
/// contract assembly makes that composition order-independent without making Runtime.Core aware
/// of any persistence implementation.
/// </remarks>
public sealed record ExecutionPlacementStoreBackend(string Name)
{
    public const string InMemory = "in-memory";
    public const string Groundwork = "groundwork";
    public const string EntityFramework = "entity-framework";

    public static void EnsureKnown(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is not InMemory and not Groundwork and not EntityFramework)
            throw new ArgumentException($"Unknown execution placement store backend '{name}'.", nameof(name));
    }
}
