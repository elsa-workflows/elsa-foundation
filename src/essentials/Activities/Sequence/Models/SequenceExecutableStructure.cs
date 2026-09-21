using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;

namespace Elsa.Activities.Sequence.Models;

public sealed class SequenceExecutableStructure
{
    [JsonConstructor]
    public SequenceExecutableStructure(
        IReadOnlyCollection<string>? activities = null,
        IReadOnlyCollection<VariableDefinition>? variables = null)
    {
        Activities = activities ?? [];
        Variables = variables ?? [];
    }

    [JsonPropertyName("activities")]
    public IReadOnlyCollection<string> Activities { get; }

    /// <summary>
    /// Container-scoped variable declarations materialized for the runtime so the executable
    /// artifact carries them without re-reading the design document.
    /// </summary>
    [JsonPropertyName("variables")]
    public IReadOnlyCollection<VariableDefinition> Variables { get; }
}
