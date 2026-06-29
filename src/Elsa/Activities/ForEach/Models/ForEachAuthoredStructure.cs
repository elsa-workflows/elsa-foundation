using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.ForEach.Models;

/// <summary>
/// Authored design-time structure for a <c>ForEach</c> node: the optional single body activity run once
/// per collection item. The body is a single child placed in the named body slot.
/// </summary>
public sealed class ForEachAuthoredStructure
{
    [JsonConstructor]
    public ForEachAuthoredStructure(ActivityNode? body = null)
    {
        Body = body;
    }

    [JsonPropertyName("body")]
    public ActivityNode? Body { get; }
}
