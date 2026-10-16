using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Foundation.Identity.Api.Endpoints.Applications;

namespace Elsa.Foundation.Identity.Api;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ListApplications))]
[JsonSerializable(typeof(ApplicationRoute))]
[JsonSerializable(typeof(CreateApplication))]
[JsonSerializable(typeof(UpdateApplication))]
[JsonSerializable(typeof(ApplicationResponse))]
[JsonSerializable(typeof(ApplicationPageResponse))]
[JsonSerializable(typeof(IssuedApplicationResponse))]
[JsonSerializable(typeof(ApiKeyResponse))]
internal partial class FoundationIdentityApplicationsJsonContext : JsonSerializerContext
{
}
