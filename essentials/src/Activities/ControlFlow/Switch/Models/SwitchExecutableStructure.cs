using System.Text.Json.Serialization;

namespace Elsa.Activities.Switch.Models;

/// <summary>
/// Compiled executable structure for a <c>Switch</c> node: ordered labels, optional pinned condition
/// input keys and branch node ids, plus an optional default branch node id. The branch
/// activities themselves are carried in named child slots; this structure records which slot child
/// belongs to which case so the runtime can schedule the matching one without re-reading the design
/// document.
/// </summary>
public sealed class SwitchExecutableStructure
{
    [JsonConstructor]
    public SwitchExecutableStructure(
        IReadOnlyCollection<SwitchExecutableCase>? cases = null,
        string? @default = null)
    {
        Cases = cases ?? [];
        Default = @default;
    }

    [JsonPropertyName("cases")]
    public IReadOnlyCollection<SwitchExecutableCase> Cases { get; }

    [JsonPropertyName("default")]
    public string? Default { get; }
}

/// <summary>
/// One compiled case: its label/legacy match value, optional condition input key and branch node id.
/// A null branch still completes with this label when selected.
/// </summary>
public sealed class SwitchExecutableCase
{
    [JsonConstructor]
    public SwitchExecutableCase(string match, string? activity = null)
    {
        Match = match;
        Activity = activity;
    }

    [JsonPropertyName("match")]
    public string Match { get; }

    [JsonPropertyName("activity")]
    public string? Activity { get; }

    [JsonPropertyName("conditionInputKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConditionInputKey { get; init; }
}
