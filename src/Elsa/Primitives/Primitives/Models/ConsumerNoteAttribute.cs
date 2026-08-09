namespace Elsa.Primitives.Models;

/// <summary>
/// A short, consumer-facing note about behaviour the type's own shape does not express — attached to the
/// code it describes (RFC #1191 Part 2, spec 150 FR-B1). Notes ride the contract fragments into the merged
/// catalog, so a consumer reads them through the artifact it already reads.
/// </summary>
/// <remarks>
/// <para>
/// The attribute exists because the alternative places consumer knowledge somewhere it rots: a wiki nobody
/// updates, or a doc file that survives the refactor that invalidated it. An attribute moves with the type,
/// is reviewed in the pull request that changes the behaviour, and is projected at build time.
/// </para>
/// <para>
/// <b>Advisory by design.</b> No build gate demands a note. Gate G4 reports which consumer-facing surface
/// carries none, and that report is a prompt, not a failure — forcing notes would produce filler text, which
/// is worse than an honest absence. What the process guarantees is that an omission was visible and chosen.
/// </para>
/// <para>
/// A note states cause and effect a consumer must anticipate, not a restatement of the member's name. Tools
/// and agents may draft one; nothing enters the contract without human approval in review.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = true,
    Inherited = false)]
public sealed class ConsumerNoteAttribute(ConsumerNoteKind kind, string note) : Attribute
{
    public ConsumerNoteKind Kind { get; } = kind;

    public string Note { get; } = note;

    /// <summary>
    /// Optional: the specific member of an enum-typed input this note describes, so a per-member effect
    /// (RFC finding F1) can be attached without one note per property.
    /// </summary>
    public string? Member { get; init; }
}

/// <summary>What a <see cref="ConsumerNoteAttribute"/> is telling the consumer. Published verbatim in the fragments.</summary>
public enum ConsumerNoteKind
{
    /// <summary>What happens when you leave this alone — usually the trap, since defaults are invisible.</summary>
    Default,

    /// <summary>Something the consumer must supply or do for this to work.</summary>
    Requirement,

    /// <summary>A rule the consumer must satisfy; violating it is rejected.</summary>
    Constraint,

    /// <summary>How this fails, and what the failure looks like from the outside.</summary>
    FailureMode,

    /// <summary>A bound: size, count, duration, precision.</summary>
    Limit,

    /// <summary>How this behaves in combination with something else — hosting, routing, another feature.</summary>
    Composition
}
