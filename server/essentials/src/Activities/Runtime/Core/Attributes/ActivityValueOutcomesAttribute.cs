namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares that an activity derives extra, per-node outcome ports from the authored value of one of its collection
/// inputs — one outcome per configured item, plus a single <see cref="UnmatchedOutcome"/> port for values that match
/// none of them. The classic case is <c>SendHttpRequest.ExpectedStatusCodes</c>: authoring <c>[200, 404]</c> yields
/// outcome ports <c>"200"</c>, <c>"404"</c> and <c>"Unmatched status code"</c> so an author can branch per status
/// (issue #926). When the source input has no authored items the activity keeps its statically declared outcomes.
/// </summary>
/// <remarks>
/// The outcomes are pinned per node at publish time by the executable compiler (reading the authored literal) so the
/// runtime can validate the emitted outcome, and the shape is advertised to the designer through the activity's
/// <c>elsa.outcomes</c> design facet. Only the collection element's textual value is used as the port name, so it must
/// be a JSON string or number.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ActivityValueOutcomesAttribute(string inputKey) : Attribute
{
    /// <summary>The contract key of the collection input whose authored items become outcome ports.</summary>
    public string InputKey { get; } = !string.IsNullOrWhiteSpace(inputKey)
        ? inputKey
        : throw new ArgumentException("A source input key is required.", nameof(inputKey));

    /// <summary>The outcome taken when the runtime value matches none of the configured items. Defaults to <c>Unmatched</c>.</summary>
    public string UnmatchedOutcome { get; init; } = "Unmatched";
}
