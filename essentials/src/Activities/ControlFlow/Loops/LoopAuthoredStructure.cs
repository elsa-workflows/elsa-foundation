using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.ControlFlow.Loops;

/// <summary>
/// Authored design-time structure of a loop activity (<c>Do</c>, <c>While</c>, <c>For</c>, <c>ForEach</c>): the
/// optional body activity, a single child placed in the loop's named body slot.
/// </summary>
public sealed class LoopAuthoredStructure
{
    [JsonConstructor]
    public LoopAuthoredStructure(ActivityNode? body = null) => Body = body;

    [JsonPropertyName("body")]
    public ActivityNode? Body { get; }
}
