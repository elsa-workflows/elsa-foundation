using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Elsa.Foundation.Identity.Tests.Api;

[Collection(FastEndpointsHostCollection.Name)]
public sealed class IdentityCompatibilityComparerTests
{
    [Fact]
    public async Task Minimal_api_after_matches_parent_fastendpoints_before_for_all_nine_routes()
    {
        var beforeHttp = BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(AppContext.BaseDirectory, "Baselines", "identity-http-fastendpoints.json"));
        var beforeOpenApi = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(AppContext.BaseDirectory, "Baselines", "identity-openapi-fastendpoints.json"));
        var approvals = BaselineFile.Load<ApprovedDifference[]>(Path.Join(AppContext.BaseDirectory, "Baselines", "identity-approved-differences.json"));

        await using var fixture = await TokenEndpointFixture.StartAsync();
        using var client = fixture.Client;
        var afterHttp = new List<HttpCompatibilityObservation>();
        foreach (var testCase in Cases)
            afterHttp.Add(await HttpEvidenceCapture.CaptureAsync(client, testCase));
        await LoginTestHelper.LoginAsync(client);
        foreach (var testCase in AuthenticatedCases)
            afterHttp.Add(await HttpEvidenceCapture.CaptureAsync(client, testCase));

        var openApi = OpenApiEvidenceCapture.Capture(await client.GetStringAsync("/openapi/v1.json"));
        var afterOpenApi = new OpenApiEvidenceDocument(openApi.Operations
            .Where(operation => operation.Endpoint.Route.Value.Contains("/_elsa/identity/", StringComparison.Ordinal))
            .ToArray());
        AssertIdentityOpenApiMetadata(await client.GetStringAsync("/openapi/v1.json"));
        var comparison = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = beforeHttp.Select(Normalize).ToArray(), OpenApi = beforeOpenApi },
            new CompatibilityEvidenceSet { Http = afterHttp.Select(Normalize).ToArray(), OpenApi = afterOpenApi },
            approvals);

        Assert.True(comparison.IsCompatible, string.Join(Environment.NewLine, comparison.Failures));
        Assert.Equal(9, afterOpenApi.Operations.Count);
        Assert.Equal(12, approvals.Length);
    }

    private static void AssertIdentityOpenApiMetadata(string document)
    {
        var root = JsonNode.Parse(document)?.AsObject()
            ?? throw new InvalidDataException("The current OpenAPI document is not a JSON object.");
        var expected = new Dictionary<(string Path, string Method), string>
        {
            [("/_elsa/identity/bootstrap", "get")] = "FoundationIdentityBootstrap",
            [("/_elsa/identity/capabilities", "get")] = "FoundationIdentityCapabilities",
            [("/_elsa/identity/session", "get")] = "FoundationIdentitySession",
            [("/_elsa/identity/token", "get")] = "FoundationIdentityToken",
            [("/_elsa/identity/challenge/{provider}", "get")] = "FoundationIdentityChallenge",
            [("/_elsa/identity/logout/{provider}", "post")] = "FoundationIdentityLogout",
            [("/_elsa/identity/refresh", "post")] = "FoundationIdentityRefresh",
            [("/_elsa/identity/login", "get")] = "AspNetCoreIdentityLoginPage",
            [("/_elsa/identity/login", "post")] = "AspNetCoreIdentityLogin"
        };

        foreach (var ((path, method), operationId) in expected)
        {
            var operation = root["paths"]?[path]?[method]?.AsObject()
                ?? throw new InvalidDataException($"OpenAPI operation is missing: {method.ToUpperInvariant()} {path}");
            Assert.Equal(operationId, operation["operationId"]?.GetValue<string>());
            var tags = operation["tags"]?.AsArray().Select(value => value!.GetValue<string>()).ToArray() ?? [];
            Assert.Equal(["Identity"], tags);
        }
    }

    private static readonly IReadOnlyList<HttpCompatibilityCase> Cases =
    [
        Case(HttpMethod.Get, "/_elsa/identity/bootstrap", "anonymous-bootstrap"),
        Case(HttpMethod.Get, "/_elsa/identity/capabilities", "anonymous-capabilities"),
        Case(HttpMethod.Get, "/_elsa/identity/challenge/unknown", "unknown-challenge"),
        new(new("/_elsa/identity/logout/unknown", "POST"), "unknown-logout", () => Json(HttpMethod.Post, "/_elsa/identity/logout/unknown", "{\"provider\":\"unknown\"}")) { Binding = "body=provider" },
        Case(HttpMethod.Get, "/_elsa/identity/challenge/aspnetcore-identity", "known-challenge"),
        new(new("/_elsa/identity/refresh", "POST"), "malformed-refresh", () => Json(HttpMethod.Post, "/_elsa/identity/refresh", "{")) { Binding = "body=refreshToken" },
        Case(HttpMethod.Get, "/_elsa/identity/session", "anonymous-session"),
        Case(HttpMethod.Get, "/_elsa/identity/token", "anonymous-token"),
        Case(HttpMethod.Get, "/_elsa/identity/login", "login-page"),
        new(new("/_elsa/identity/login", "POST"), "invalid-login-json", () => Json(HttpMethod.Post, "/_elsa/identity/login", "{}")) { Binding = "body=username,password" }
    ];

    private static readonly IReadOnlyList<HttpCompatibilityCase> AuthenticatedCases =
    [
        Case(HttpMethod.Get, "/_elsa/identity/bootstrap", "authenticated-bootstrap"),
        Case(HttpMethod.Get, "/_elsa/identity/capabilities", "authenticated-capabilities"),
        Case(HttpMethod.Get, "/_elsa/identity/session", "authenticated-session"),
        Case(HttpMethod.Get, "/_elsa/identity/token", "authenticated-token"),
        new(new("/_elsa/identity/refresh", "POST"), "empty-refresh", () => Json(HttpMethod.Post, "/_elsa/identity/refresh", "{}")) { Binding = "body=refreshToken" },
        new(new("/_elsa/identity/refresh", "POST"), "garbage-refresh", () => Json(HttpMethod.Post, "/_elsa/identity/refresh", "{\"refreshToken\":\"not-a-real-refresh-token\"}")) { Binding = "body=refreshToken" },
        new(new("/_elsa/identity/refresh", "POST"), "unsupported-refresh", () => new HttpRequestMessage(HttpMethod.Post, "/_elsa/identity/refresh") { Content = new StringContent("{}", Encoding.UTF8, "text/plain") }) { Binding = "body=refreshToken" },
        new(new("/_elsa/identity/login", "POST"), "authenticated-login-json", () => Json(HttpMethod.Post, "/_elsa/identity/login", "{\"username\":\"admin\",\"password\":\"Password123!\"}")) { Binding = "body=username,password" },
        new(new("/_elsa/identity/logout/aspnetcore-identity", "POST"), "authenticated-logout", () => Json(HttpMethod.Post, "/_elsa/identity/logout/aspnetcore-identity", "{\"provider\":\"aspnetcore-identity\"}")) { Binding = "body=provider" }
    ];

    private static HttpCompatibilityCase Case(HttpMethod method, string path, string name) =>
        new(new(path, method.Method), name, () => new HttpRequestMessage(method, path));

    private static HttpRequestMessage Json(HttpMethod method, string path, string body) =>
        new(method, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpCompatibilityObservation Normalize(HttpCompatibilityObservation observation)
    {
        static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            var normalized = Regex.Replace(value, "eyJ[A-Za-z0-9_=-]+\\.[A-Za-z0-9_=-]+\\.[A-Za-z0-9_=-]+", "<jwt>");
            normalized = Regex.Replace(normalized, "(?:__Host-)?[A-Za-z0-9_.-]+=[^;\\\"\\r\\n]+", "<cookie>");
            normalized = Regex.Replace(normalized, "(name=\"__csrf\" value=\")[^\"]+(\")", "$1<csrf>$2");
            try
            {
                if (JsonNode.Parse(normalized) is JsonObject obj)
                {
                    foreach (var property in new[] { "accessToken", "requestToken", "expiresAt", "subject" })
                        if (obj[property] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text))
                            obj[property] = $"<{property}>";
                    if (obj["roles"] is JsonArray roles && roles.Count > 0)
                        obj["roles"] = new JsonArray("<role>");
                    return CompatibilityJson.Canonicalize(obj);
                }
            }
            catch (JsonException) { }
            return normalized;
        }

        return observation with
        {
            Body = Clean(observation.Body),
            Json = Clean(observation.Json),
            Headers = observation.Headers.ToDictionary(x => x.Key, x => Clean(x.Value), StringComparer.OrdinalIgnoreCase)
        };
    }
}
