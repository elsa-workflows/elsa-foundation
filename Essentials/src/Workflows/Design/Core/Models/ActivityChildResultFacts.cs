namespace Elsa.Workflows.Design.Core.Models;

/// <summary>What the owning structure knows about one direct child's result at another child.</summary>
public sealed record ActivityChildResultFacts(
    ActivityChildResultAvailability Availability,
    bool? IsCyclicBackEdge = null,
    bool? MayRunConcurrently = null,
    bool? AreMutuallyExclusive = null)
{
    internal static ActivityChildResultFacts FromCompletedBefore(
        IReadOnlyList<string>? completedBefore,
        string producerChildNodeId) =>
        new(completedBefore?.Contains(producerChildNodeId, StringComparer.Ordinal) == true
            ? ActivityChildResultAvailability.Definite
            : ActivityChildResultAvailability.Unknown);
}

/// <summary>Availability supplied by the structure, independent of an input's optional-source policy.</summary>
public enum ActivityChildResultAvailability
{
    /// <summary>The structure has not supplied enough facts to establish availability.</summary>
    Unknown,

    /// <summary>The producer result is committed on every path to the consumer.</summary>
    Definite,

    /// <summary>The producer result can be committed on some paths to the consumer.</summary>
    Conditional,

    /// <summary>The producer result cannot be available before this consumer.</summary>
    Unavailable
}
