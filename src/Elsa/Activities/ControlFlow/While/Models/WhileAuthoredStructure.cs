using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.While.Models;

/// <summary>
/// Authored design-time structure for a <c>While</c> node: the optional <c>Body</c> branch activity, a
/// single child placed in a named slot.
/// </summary>
public sealed class WhileAuthoredStructure
{
    [JsonConstructor]
    public WhileAuthoredStructure(ActivityNode? body = null) => Body = body;

    [JsonPropertyName("body")]
    public ActivityNode? Body { get; }
}
