using System.Text.Json.Serialization;

namespace Elsa.Activities.While.Models;

/// <summary>
/// Compiled executable structure for a <c>While</c> node: the executable node id of the optional
/// <c>Body</c> branch. The body activity itself is carried in the named child slot; this structure
/// records which slot child is the body so the runtime can schedule it without re-reading the design
/// document.
/// </summary>
public sealed class WhileExecutableStructure
{
    [JsonConstructor]
    public WhileExecutableStructure(string? body = null) => Body = body;

    [JsonPropertyName("body")]
    public string? Body { get; }
}
