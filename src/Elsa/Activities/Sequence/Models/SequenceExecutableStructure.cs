using System.Text.Json.Serialization;

namespace Elsa.Activities.Sequence.Models;

public sealed class SequenceExecutableStructure
{
    [JsonConstructor]
    public SequenceExecutableStructure(IReadOnlyCollection<string>? activities = null)
    {
        Activities = activities ?? [];
    }

    [JsonPropertyName("activities")]
    public IReadOnlyCollection<string> Activities { get; }
}
