using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Do.Models;

/// <summary>
/// Authored design-time structure for a <c>Do</c> node: the optional <c>Body</c> branch activity, a
/// single child placed in a named slot.
/// </summary>
public sealed class DoAuthoredStructure
{
    [JsonConstructor]
    public DoAuthoredStructure(ActivityNode? body = null) => Body = body;

    [JsonPropertyName("body")]
    public ActivityNode? Body { get; }
}
