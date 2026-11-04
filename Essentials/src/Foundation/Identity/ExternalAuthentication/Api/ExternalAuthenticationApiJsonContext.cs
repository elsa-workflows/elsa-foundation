using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

internal sealed record ExternalAntiforgeryResponse(string RequestToken);
internal sealed record ExternalAuthenticationFailureResponse(string Error);
internal sealed record ExternalAuthenticationStartRequest(string? ReturnUrl, string? ConnectionRecordId = null);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ExternalAntiforgeryResponse))]
[JsonSerializable(typeof(ExternalAuthenticationFailureResponse))]
[JsonSerializable(typeof(ExternalAuthenticationPreview))]
[JsonSerializable(typeof(ExternalAuthenticationStartRequest))]
internal partial class ExternalAuthenticationApiJsonContext : JsonSerializerContext;
