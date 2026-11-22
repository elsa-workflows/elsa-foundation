namespace Elsa3.Mapping.ActivityMap;

/// <summary>
/// An Elsa 3 outcome and the Elsa 4 outcome(s) a connection from it becomes, for graph conversion (#2705) to apply.
/// This spec only carries the data.
/// </summary>
/// <param name="Elsa3Outcome">The Elsa 3 outcome name.</param>
/// <param name="Elsa4Outcomes">The Elsa 4 outcomes, for example If <c>Done</c> → [<c>True</c>, <c>False</c>]; empty when unsupported.</param>
/// <param name="UnsupportedReason">Why the outcome has no Elsa 4 counterpart, when it has none.</param>
public sealed record Elsa3OutcomeRule(
    string Elsa3Outcome,
    IReadOnlyList<string> Elsa4Outcomes,
    string? UnsupportedReason = null);
