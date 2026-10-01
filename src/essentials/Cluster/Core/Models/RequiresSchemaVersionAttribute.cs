namespace Elsa.Cluster.Core.Models;

/// <summary>
/// Declares that a shell feature needs schema family <see cref="Family"/> at version <see cref="Version"/> or later: a
/// dormancy requirement (spec 182, FR-001). Until this host observes that version as finalized, and, with
/// <see cref="RequiresCompleteness"/>, until the family's finish record says no row below it remains (FR-005), the
/// feature is dormant: composed and running, listed as enabled, but its operations that need the new data are refused.
/// </summary>
/// <remarks>
/// <para>
/// Carried on the feature class, in the style of <c>[UsesEfModule]</c>: constant arguments only, naming the family and
/// the version by the labels the family's <c>[EfSchemaFamily]</c> declaration spells, so it can be read as metadata
/// without loading anything the family's module owns. <see cref="AllowMultiple"/> because a feature can need several
/// families; it is dormant until every requirement is met.
/// </para>
/// <para>
/// Declaring it changes nothing about composition (FR-006). The feature's services are registered and its endpoints
/// mapped exactly as without it. What reads it is the feature catalog, Attention and the operations themselves, each
/// through the one shared dormancy check.
/// </para>
/// <para>
/// A task class may carry it too (#2192): the task executor asks the shared check before it runs the task, and before it
/// takes the task's single-node lock, and skips the task while this node is dormant for it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresSchemaVersionAttribute(string family, string version) : Attribute
{
    /// <summary>The schema family, as its declaration names it.</summary>
    public string Family { get; } = family;

    /// <summary>The oldest version of the family whose data the feature needs.</summary>
    public string Version { get; } = version;

    /// <summary>
    /// Whether the feature also needs every row of the family to carry that version's data, as a query or lookup over a
    /// projection introduced at that version does (FR-005). Such a feature stays dormant after finalization until the
    /// family's completeness is established by the post-finalization backfill's finish record (spec 186).
    /// </summary>
    public bool RequiresCompleteness { get; init; }
}
