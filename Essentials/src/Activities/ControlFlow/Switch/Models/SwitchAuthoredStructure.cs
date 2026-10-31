using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Switch.Models;

/// <summary>
/// Authored design-time structure for a <c>Switch</c> node: the ordered set of cases (each a match value
/// plus an optional single branch activity) and an optional default branch activity. Every branch is a
/// single child placed in its own named slot.
/// </summary>
public sealed class SwitchAuthoredStructure
{
    [JsonConstructor]
    public SwitchAuthoredStructure(
        IReadOnlyCollection<SwitchAuthoredCase>? cases = null,
        ActivityNode? @default = null)
    {
        Cases = cases ?? [];
        Default = @default;
    }

    [JsonPropertyName("cases")]
    public IReadOnlyCollection<SwitchAuthoredCase> Cases { get; }

    [JsonPropertyName("default")]
    public ActivityNode? Default { get; }
}

/// <summary>
/// A single authored case: the value the switch expression is matched against and the optional branch
/// activity to run when it matches.
/// </summary>
public sealed class SwitchAuthoredCase
{
    [JsonConstructor]
    public SwitchAuthoredCase(string match, ActivityNode? activity = null)
    {
        Match = match;
        Activity = activity;
    }

    [JsonPropertyName("match")]
    public string Match { get; }

    [JsonPropertyName("activity")]
    public ActivityNode? Activity { get; }

    [JsonPropertyName("caseId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CaseId { get; init; }

    /// <summary>A present argument container is a predicate even when its value is unbound.</summary>
    [JsonPropertyName("condition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArgumentState? Condition { get; init; }
}
