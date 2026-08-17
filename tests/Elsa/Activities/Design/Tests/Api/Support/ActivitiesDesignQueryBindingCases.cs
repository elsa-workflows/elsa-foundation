using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;

namespace Elsa.Activities.Design.Tests.Api.Support;

internal static class ActivitiesDesignQueryBindingCases
{
    public static IReadOnlyList<HttpCompatibilityCase> All { get; } =
    [
        Create("Catalog.List|trusted-invalid-enum", "/design/activities/catalog?availability=invalid", "GET /design/activities/catalog"),
        Create("Definitions.List|trusted-invalid-int", "/design/activities/definitions?limit=invalid", "GET /design/activities/definitions"),
        Create("Versions.Dependencies|trusted-invalid-nullable-int", "/design/activities/versions/version-route/dependencies?limit=invalid", "GET /design/activities/versions/{versionId}/dependencies"),
        Create("Versions.Dependencies|trusted-invalid-bool", "/design/activities/versions/version-route/dependencies?transitive=invalid", "GET /design/activities/versions/{versionId}/dependencies")
    ];

    private static HttpCompatibilityCase Create(string name, string requestPath, string endpoint)
    {
        var separator = endpoint.IndexOf(' ');
        var identity = new EndpointIdentity(endpoint[(separator + 1)..], endpoint[..separator]);
        return new(identity, name, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
            request.Headers.TryAddWithoutValidation(ActivitiesDesignCompatibilityCases.IdentityHeader, "trusted-binding");
            return request;
        })
        {
            Binding = $"query={requestPath.Split('?', 2)[1]};typed-parse=invalid",
            PagingFiltering = requestPath.Split('?', 2)[1]
        };
    }
}
