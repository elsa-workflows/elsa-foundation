using System.Text.Json.Serialization;

namespace Elsa.Activities.ControlFlow.Loops;

/// <summary>
/// Compiled executable structure of a loop activity: the executable node id of its optional body
/// (<c>null</c> when the loop has an empty body). The body activity itself is carried in the loop's named body
/// slot; this structure records which slot child is the body so the runtime can schedule it each pass without
/// re-reading the design document.
/// </summary>
public sealed class LoopExecutableStructure
{
    [JsonConstructor]
    public LoopExecutableStructure(string? body = null) => Body = body;

    [JsonPropertyName("body")]
    public string? Body { get; }
}
