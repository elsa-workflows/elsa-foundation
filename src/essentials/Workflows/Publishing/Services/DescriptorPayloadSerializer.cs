using System.Text.Json;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// The one set of serializer options publication reads and writes activity descriptor, structure and variable payloads
/// with.
/// </summary>
internal static class DescriptorPayloadSerializer
{
    /// <summary>The web-default options for descriptor and payload JSON.</summary>
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
