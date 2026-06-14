using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Sequence.Models;

public sealed class SequenceAuthoredStructure
{
    [JsonConstructor]
    public SequenceAuthoredStructure(IReadOnlyCollection<ActivityNode>? activities = null)
    {
        Activities = activities ?? [];
    }

    [JsonPropertyName("activities")]
    public IReadOnlyCollection<ActivityNode> Activities { get; }
}
