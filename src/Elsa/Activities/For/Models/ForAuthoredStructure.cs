using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.For.Models;

/// <summary>
/// Authored design-time structure for a <c>For</c> node: the optional single body activity run once per
/// pass over the numeric range. The body is a single child placed in the <c>For.Body</c> slot.
/// </summary>
public sealed class ForAuthoredStructure
{
    [JsonConstructor]
    public ForAuthoredStructure(ActivityNode? body = null)
    {
        Body = body;
    }

    [JsonPropertyName("body")]
    public ActivityNode? Body { get; }
}
