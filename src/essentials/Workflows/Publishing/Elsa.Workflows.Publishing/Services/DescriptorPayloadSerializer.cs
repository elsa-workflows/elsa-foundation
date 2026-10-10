using System.Text.Json;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// The one set of serializer options the publication compile path reads and writes executable node descriptor payloads
/// and their variable payloads with. Other options in this project serve different purposes and are not covered: the
/// candidate snapshot hash, the placement occurrence overlay (string enums), the upgrade plan store and the EF and API
/// JSON contexts.
/// </summary>
internal static class DescriptorPayloadSerializer
{
    /// <summary>The web-default options for descriptor and payload JSON.</summary>
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
