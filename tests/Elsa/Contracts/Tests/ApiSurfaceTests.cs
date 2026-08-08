using System.Text.Json;
using Elsa.Contracts.Generator;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// Spec 150 FR-C14/C15. The benchmark named the missing API surface the single largest gap: /swagger and
/// /openapi both 404, so every session obtained the publish/execute/instances endpoints by blind probing
/// or by reading an existing consumer suite.
/// </summary>
public sealed class ApiSurfaceTests
{
    private static string ContractsRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent!)
        {
            if (File.Exists(Path.Combine(current.FullName, "Elsa.Server.slnx")))
                return Path.Combine(current.FullName, "docs", "contracts");
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static JsonDocument Api() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot(), "api.json")));

    private static IEnumerable<JsonElement> Endpoints(JsonDocument api) => api.RootElement.GetProperty("endpoints").EnumerateArray();

    /// <summary>
    /// The routes a consumer needs to author, publish and prove a workflow. Named explicitly because
    /// "some endpoints are published" is not the requirement — these specific ones were probed for.
    /// </summary>
    [Theory]
    [InlineData("POST", "/design/workflows/definitions/submit")]
    [InlineData("GET", "/design/workflows/definitions/submit/schema")]
    [InlineData("GET", "/design/activities/catalog")]
    public void The_authoring_path_is_published(string verb, string route)
    {
        using var api = Api();

        Assert.True(
            Endpoints(api).Any(endpoint =>
                endpoint.GetProperty("verb").GetString() == verb &&
                endpoint.GetProperty("route").GetString() == route),
            $"{verb} {route} is not in the published API surface, so a contracts-only consumer must discover it by probing.");
    }

    [Fact]
    public void The_publish_endpoint_publishes_both_success_statuses_and_the_rule_between_them()
    {
        using var api = Api();

        // Specifically the workflow publish route — /design/activities/drafts/{id}/publish is a different
        // endpoint that also ends in "/publish".
        var publish = Endpoints(api).Single(endpoint =>
            endpoint.GetProperty("verb").GetString() == "POST" &&
            endpoint.GetProperty("route").GetString()! is var route &&
            route.StartsWith("/publishing/workflows/", StringComparison.Ordinal) &&
            route.EndsWith("/publish", StringComparison.Ordinal));

        var statuses = publish.GetProperty("successStatuses").EnumerateArray().Select(value => value.GetInt32()).ToArray();

        // 201 alone would be as wrong as 200 alone: the endpoint answers 201 when it creates the
        // publication and 200 when it updates one in place.
        Assert.Contains(201, statuses);
        Assert.Contains(200, statuses);
        Assert.False(string.IsNullOrWhiteSpace(publish.GetProperty("successStatusCondition").GetString()),
            "Publishing more than one success status without stating which is returned when leaves the consumer guessing again.");
    }

    [Fact]
    public void Success_status_is_absent_rather_than_guessed_when_an_endpoint_does_not_declare_it()
    {
        using var api = Api();

        // The point of the opt-in attribute: an undeclared status is published as null, never as a
        // defaulted 200 that a consumer would trust and assert against.
        Assert.Contains(Endpoints(api), endpoint => endpoint.GetProperty("successStatuses").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public void Every_endpoint_publishes_a_verb_route_and_permission_set()
    {
        using var api = Api();
        var endpoints = Endpoints(api).ToArray();

        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            Assert.False(string.IsNullOrWhiteSpace(endpoint.GetProperty("verb").GetString()));
            Assert.StartsWith("/", endpoint.GetProperty("route").GetString()!, StringComparison.Ordinal);
            Assert.Equal(JsonValueKind.Array, endpoint.GetProperty("permissions").ValueKind);

            var permissions = endpoint.GetProperty("permissions").EnumerateArray().Select(value => value.GetString()!).ToArray();
            Assert.Equal(permissions.Distinct(StringComparer.Ordinal).Count(), permissions.Length);
        }
    }

    [Fact]
    public void The_api_surface_is_route_sorted_and_fingerprinted()
    {
        using var api = Api();
        var keys = Endpoints(api)
            .Select(endpoint => (endpoint.GetProperty("route").GetString()!, endpoint.GetProperty("verb").GetString()!))
            .ToArray();

        Assert.Equal(keys.OrderBy(key => key.Item1, StringComparer.Ordinal).ThenBy(key => key.Item2, StringComparer.Ordinal), keys);

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot(), "manifest.json")));
        Assert.Equal(
            manifest.RootElement.GetProperty("api").GetString(),
            DeterministicJson.Fingerprint(File.ReadAllBytes(Path.Combine(ContractsRoot(), "api.json"))));
    }
}
