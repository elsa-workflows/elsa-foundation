using System.Text.Json.Serialization;

namespace Elsa.Activities.ForEach.Models;

/// <summary>
/// Compiled executable structure for a <c>ForEach</c> node: the executable node id of its optional body
/// (<c>null</c> when the loop has an empty body). The body activity itself is carried in the named body
/// child slot; this structure records the slot child's node id so the runtime can schedule it per pass
/// without re-reading the design document.
/// </summary>
public sealed class ForEachExecutableStructure
{
    [JsonConstructor]
    public ForEachExecutableStructure(string? body = null)
    {
        Body = body;
    }

    [JsonPropertyName("body")]
    public string? Body { get; }
}
