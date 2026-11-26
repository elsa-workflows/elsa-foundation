using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

internal sealed record ExternalAntiforgeryResponse(string RequestToken, string FormFieldName, string? HeaderName);
internal sealed record ExternalAuthenticationFailureResponse(string Error);
internal sealed record ExternalAuthenticationStartRequest(string? ReturnUrl, string? ConnectionRecordId = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
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
[JsonSerializable(typeof(ConnectionWrite))]
[JsonSerializable(typeof(ConnectionWriteResult))]
[JsonSerializable(typeof(ManagedSessionPage))]
[JsonSerializable(typeof(ManagedIdentityUsersResponse))]
[JsonSerializable(typeof(ManagedSessionRevokeRequest))]
[JsonSerializable(typeof(ManagedSessionRevokeResponse))]
[JsonSerializable(typeof(ManagedIdentityLinkPage))]
[JsonSerializable(typeof(ManagedLinkAttachRequest))]
[JsonSerializable(typeof(ManagedLinkAttachResponse))]
[JsonSerializable(typeof(ManagedLinkRemoveRequest))]
[JsonSerializable(typeof(ManagedLinkRemoveResponse))]
internal partial class ExternalAuthenticationApiJsonContext : JsonSerializerContext
{
    internal static ExternalAuthenticationApiJsonContext WireContext { get; } = new(CreateWireOptions());

    private static JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }
}
