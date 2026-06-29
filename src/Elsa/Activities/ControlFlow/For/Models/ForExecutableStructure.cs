using System.Text.Json.Serialization;

namespace Elsa.Activities.For.Models;

/// <summary>
/// Compiled executable structure for a <c>For</c> node: the executable node id of the optional body
/// activity (<c>null</c> when the loop has an empty body). The body activity itself is carried in the
/// <c>For.Body</c> child slot; this structure records which slot child is the body so the runtime can
/// schedule it each pass without re-reading the design document.
/// </summary>
public sealed class ForExecutableStructure
{
    [JsonConstructor]
    public ForExecutableStructure(string? body = null)
    {
        Body = body;
    }

    [JsonPropertyName("body")]
    public string? Body { get; }
}
