using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.If.Models;

/// <summary>
/// Authored design-time structure for an <c>If</c> node: the optional <c>Then</c> and <c>Else</c>
/// branch activities, each a single child placed in a named slot.
/// </summary>
public sealed class IfAuthoredStructure
{
    [JsonConstructor]
    public IfAuthoredStructure(ActivityNode? then = null, ActivityNode? @else = null)
    {
        Then = then;
        Else = @else;
    }

    [JsonPropertyName("then")]
    public ActivityNode? Then { get; }

    [JsonPropertyName("else")]
    public ActivityNode? Else { get; }
}
