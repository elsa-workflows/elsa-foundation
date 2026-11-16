using System.Text.Json.Serialization;

namespace Elsa.Activities.If.Models;

/// <summary>
/// Compiled executable structure for an <c>If</c> node: the executable node ids of the optional
/// <c>Then</c> and <c>Else</c> branches. The branch activities themselves are carried in named child
/// slots; this structure records which slot child is the <c>Then</c> branch and which is the
/// <c>Else</c> branch so the runtime can schedule the matching one without re-reading the design
/// document.
/// </summary>
public sealed class IfExecutableStructure
{
    [JsonConstructor]
    public IfExecutableStructure(string? then = null, string? @else = null)
    {
        Then = then;
        Else = @else;
    }

    [JsonPropertyName("then")]
    public string? Then { get; }

    [JsonPropertyName("else")]
    public string? Else { get; }
}
