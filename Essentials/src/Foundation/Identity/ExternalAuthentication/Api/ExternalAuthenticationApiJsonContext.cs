using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

internal sealed record ExternalAntiforgeryResponse(string RequestToken, string FormFieldName, string? HeaderName);
internal sealed record ExternalAuthenticationFailureResponse(string Error);
internal sealed record ExternalAuthenticationStartRequest(string? ReturnUrl, string? ConnectionRecordId = null);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ExternalAntiforgeryResponse))]
[JsonSerializable(typeof(ExternalAuthenticationFailureResponse))]
[JsonSerializable(typeof(ExternalAuthenticationPreview))]
[JsonSerializable(typeof(ExternalAuthenticationStartRequest))]
[JsonSerializable(typeof(ManagedConnectionCatalogResponse))]
[JsonSerializable(typeof(ManagedConnectionDetailResponse))]
[JsonSerializable(typeof(ManagedConnectionAssessmentResponse))]
[JsonSerializable(typeof(ManagedConnectionTestRequest))]
[JsonSerializable(typeof(ManagedConnectionTestResponse))]
[JsonSerializable(typeof(ManagedUserPage))]
[JsonSerializable(typeof(ManagedRolePage))]
[JsonSerializable(typeof(ManagedConnectionError))]
[JsonSerializable(typeof(ManagedSessionPage))]
[JsonSerializable(typeof(ManagedIdentityUsersResponse))]
[JsonSerializable(typeof(ManagedSessionRevokeRequest))]
[JsonSerializable(typeof(ManagedSessionRevokeResponse))]
[JsonSerializable(typeof(ManagedIdentityLinkPage))]
[JsonSerializable(typeof(ManagedLinkAttachRequest))]
[JsonSerializable(typeof(ManagedLinkAttachResponse))]
[JsonSerializable(typeof(ManagedLinkRemoveRequest))]
[JsonSerializable(typeof(ManagedLinkRemoveResponse))]
[JsonSerializable(typeof(ConnectionWrite))]
[JsonSerializable(typeof(ConnectionWriteResult))]
internal partial class ExternalAuthenticationApiJsonContext : JsonSerializerContext;
