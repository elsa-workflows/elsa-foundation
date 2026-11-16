namespace Elsa.Workflows.Design.Core.Models;

/// <summary>What the owning structure knows about one direct child's result at another child.</summary>
public sealed record ActivityChildResultFacts(
    ActivityChildResultAvailability Availability,
    bool? IsCyclicBackEdge = null,
    bool? MayRunConcurrently = null,
    bool? AreMutuallyExclusive = null)
{
    /// <summary>
    /// Whether, within one run of this structure, an earlier run of the producer child can be among the runs that
    /// led to the consumer child, beside the producer run that precedes it. A result binding that names only the
    /// producer cannot tell those runs apart, so it is admitted only when this is <see langword="false"/>.
    /// <see langword="null"/> means the structure has not said, which is treated as "it can".
    /// </summary>
    /// <remarks>
    /// The answer follows from who schedules a child. A structure that schedules each child once per run, or each
    /// pass of a loop itself, never links one run of a child to an earlier one: <see langword="false"/>. A structure
    /// in which a completed child schedules the next one links every pass to the pass before it, so a child that
    /// can be reached again from itself answers <see langword="true"/>. Repetition of the whole structure by an
    /// enclosing one is the enclosing structure's to report: asked about its child with that same child as producer
    /// and consumer, it answers whether one run of the child can lead to a later run of it. Every structure above the
    /// one that holds both must answer <see langword="false"/> there for the binding to be admitted.
    /// </remarks>
    public bool? EarlierProducerRunCanReachConsumer { get; init; }

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
