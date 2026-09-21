using System.Text.Json.Serialization;

namespace Elsa.Activities.Switch.Models;

/// <summary>
/// Compiled executable structure for a <c>Switch</c> node: the ordered cases (each a match value plus
/// the executable node id of its optional branch) and the optional default branch node id. The branch
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
/// A single compiled case: the value the switch expression is matched against and the executable node id
/// of its optional branch (<c>null</c> when the case has an empty branch).
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
}
