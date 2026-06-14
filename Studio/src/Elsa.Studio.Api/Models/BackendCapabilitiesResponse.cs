using System.Text.Json.Serialization;

namespace Elsa.Studio.Api.Models;

public sealed record BackendCapabilitiesResponse(
    [property: JsonPropertyName("capabilities")] IReadOnlyCollection<BackendCapabilityResponse> Capabilities);

public sealed record BackendCapabilityResponse(
    [property: JsonPropertyName("id")] string Id);
