using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Switch.Models;

/// <summary>
/// Authored design-time structure for a <c>Switch</c> node: ordered case labels, optional Boolean
/// condition arguments and branch activities, plus an optional default branch. Each branch occupies
/// one named child slot. Cases without a condition retain legacy Value matching.
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
/// One authored case: its human label (also the legacy comparison value), optional predicate and branch.
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
