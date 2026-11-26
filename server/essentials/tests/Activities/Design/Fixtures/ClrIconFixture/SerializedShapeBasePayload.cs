using System.Text.Json.Serialization;

namespace Elsa.Activities.Design.Tests.ClrIconFixture;

/// <summary>A separate metadata asset whose inherited serialized members must enter shape revisions.</summary>
public class SerializedShapeBasePayload
{
    [JsonPropertyName("inherited.id")]
    public string Identifier { get; set; } = string.Empty;
}
