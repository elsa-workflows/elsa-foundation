using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Sequence.Models;

public sealed class SequenceAuthoredStructure
{
    [JsonConstructor]
    public SequenceAuthoredStructure(
        IReadOnlyCollection<ActivityNode>? activities = null,
        IReadOnlyCollection<VariableDefinition>? variables = null)
    {
        Activities = activities ?? [];
        Variables = variables ?? [];
    }

    [JsonPropertyName("activities")]
    public IReadOnlyCollection<ActivityNode> Activities { get; }

    /// <summary>
    /// Container-scoped variables declared by this Sequence. They are owned by the declaring
    /// Sequence node and visible to its descendant activities (scoped variable model, ADR 0027).
    /// </summary>
    [JsonPropertyName("variables")]
    public IReadOnlyCollection<VariableDefinition> Variables { get; }
}
