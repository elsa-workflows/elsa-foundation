using Elsa.Activities.Design.Tests.Api.Support;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

/// <summary>Executable HTTP behavior bites for the post-migration Minimal API owner.</summary>
public sealed class ActivitiesDesignMinimalApiBehaviorTests
{
    public static TheoryData<string, string> CreatedRoutes => new()
    {
        { "Definitions.Add", "/design/activities/definitions/capture" },
        { "Definitions.AddDraft", "/design/activities/drafts/capture" },
        { "Drafts.ConflictCopy", "/design/activities/drafts/capture" },
        { "Drafts.MigrateProvider", "/design/activities/drafts/capture" },
        { "Forks.Apply", "/design/activities/definitions/capture" },
        { "UpgradePlans.Create", "/design/activities/upgrade-plans/capture" },
        { "UpgradePlans.Refresh", "/design/activities/upgrade-plans/capture" }
    };

    public static TheoryData<string, string, string> InvalidTypedQueries => new()
    {
        { "/design/activities/catalog?availability=invalid", "availability", "ActivityCatalogAvailability" },
        { "/design/activities/definitions?limit=invalid", "limit", "Int32" },
        { "/design/activities/definitions/picker?offset=invalid", "offset", "Int32" },
        { "/design/activities/definitions/picker?limit=invalid", "limit", "Int32" },
        { "/design/activities/definitions/definition-route/drafts?limit=invalid", "limit", "Int32" },
        { "/design/activities/definitions/definition-route/versions?limit=invalid", "limit", "Int32" },
        { "/design/activities/versions/version-route/dependencies?limit=invalid", "limit", "Int32" },
        { "/design/activities/versions/version-route/dependencies?transitive=invalid", "transitive", "Boolean" }
    };

    [Theory]
    [MemberData(nameof(CreatedRoutes))]
    public async Task Every_created_operation_returns_201_and_the_exact_location(
        string routeId,
        string expectedLocation)
    {
        await using var host = await ActivitiesDesignMinimalApiHost.StartAsync();
        var route = ActivitiesDesignCompatibilityCases.Manifest.Single(candidate => candidate.Id == routeId);
        using var request = Request(route, "trusted-success", "{}");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Discard_returns_an_empty_204_response()
    {
        await using var host = await ActivitiesDesignMinimalApiHost.StartAsync();
        var route = ActivitiesDesignCompatibilityCases.Manifest.Single(candidate => candidate.Id == "Drafts.Discard");
        using var request = Request(route, "trusted-success", "{}");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ordinary_success_preserves_200_json_response_behavior()
    {
        await using var host = await ActivitiesDesignMinimalApiHost.StartAsync();
        var route = ActivitiesDesignCompatibilityCases.Manifest.Single(candidate => candidate.Id == "Catalog.List");
        using var request = Request(route, "trusted-success");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("json", response.Content.Headers.ContentType?.MediaType ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.ValueKind);
    }

    [Fact]
    public async Task Authoring_failures_preserve_typed_problem_details()
    {
        await using var host = await ActivitiesDesignMinimalApiHost.StartAsync();
        var route = ActivitiesDesignCompatibilityCases.Manifest.Single(candidate => candidate.Id == "Drafts.Validate");
        using var request = Request(route, "trusted-domain-conflict", "{}");

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("\"errorCode\":\"capture.domain-conflict\"", body, StringComparison.Ordinal);
        Assert.Contains("\"status\":409", body, StringComparison.Ordinal);
        Assert.Contains("\"title\":\"Capture domain conflict\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_mediator_not_found_errors_keep_the_shared_404_contract()
    {
        await using var host = await ActivitiesDesignMinimalApiHost.StartAsync();
        var route = ActivitiesDesignCompatibilityCases.Manifest.Single(candidate => candidate.Id == "Availability.GetSettings");
        using var request = Request(route, "trusted-domain-not-found");

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("\"status\":404", body, StringComparison.Ordinal);
        Assert.Contains("Not Found", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidTypedQueries))]
    public async Task Every_malformed_typed_query_fails_before_mediator_dispatch(
        string path,
        string queryName,
        string typeName)
    {
        await using var host = await ActivitiesDesignMinimalApiHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(ActivitiesDesignCompatibilityCases.IdentityHeader, "trusted-binding");

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"\"name\":\"{queryName}\"", body, StringComparison.Ordinal);
        Assert.Contains($"Value [invalid] is not valid for a [{typeName}] property!", body, StringComparison.Ordinal);
        Assert.Null(host.RequestSender.LastRequest);
    }

    [Fact]
    public async Task Unexpected_authoring_failures_are_sanitized_to_the_stable_5xx_problem()
    {
        await using var host = await ActivitiesDesignMinimalApiHost.StartAsync();
        var route = ActivitiesDesignCompatibilityCases.Manifest.Single(candidate => candidate.Id == "Drafts.Validate");
        using var request = Request(route, "trusted-unexpected-failure", "{}");

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("activity.operation.failed", body, StringComparison.Ordinal);
        Assert.Contains("The activity operation failed.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("capture-secret-internal-details", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_reaches_the_sender_with_the_same_request_aborted_instance()
    {
        await using var host = await ActivitiesDesignMinimalApiHost.StartAsync();
        var route = ActivitiesDesignCompatibilityCases.Manifest.Single(candidate => candidate.Id == "Drafts.Get");
        using var request = Request(route, "trusted-cancellation");

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => host.Client.SendAsync(request));

        Assert.Same(host.RequestSender.LastCancellationException, exception);
        Assert.Equal(host.RequestSender.RequestAbortedAtDispatch, host.RequestSender.LastCancellationToken);
    }

    private static HttpRequestMessage Request(ActivityDesignRoute route, string identity, string? body = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(route.Endpoint.Method.Value), route.RequestPath);
        request.Headers.TryAddWithoutValidation(ActivitiesDesignCompatibilityCases.IdentityHeader, identity);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }
}
