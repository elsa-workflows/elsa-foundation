using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>Behavioral parity bites for status selection, diagnostics, sanitization, and cancellation.</summary>
public sealed class PublishingMinimalApiBehaviorTests
{
    [Fact]
    public async Task Workflow_publish_preserves_dynamic_created_and_replayed_statuses()
    {
        var factory = new RecordingRequestSenderFactory();
        await using var host = await PublishingMinimalApiHost.StartAsync(factory.Create);
        factory.Sender.WorkflowWasCreated = true;

        using var created = await PublishWorkflowAsync(host.Client, "publish-created");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        factory.Sender.WorkflowWasCreated = false;
        using var replay = await PublishWorkflowAsync(host.Client, "publish-replay");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }

    [Fact]
    public async Task Activity_publish_preserves_created_location_and_test_run_accepted_statuses()
    {
        var factory = new RecordingRequestSenderFactory();
        await using var host = await PublishingMinimalApiHost.StartAsync(factory.Create);
        factory.Sender.ActivityIdempotencyKey = "publication/location";

        using var publication = await SendAsync(
            host.Client,
            HttpMethod.Post,
            "/design/activities/drafts/draft-route/publish",
            """
                {
                  "expectedDraftRevision": 8,
                  "version": "1.0.0",
                  "reviewToken": "review",
                  "idempotencyKey": "publication-location"
                }
                """,
            "trusted-success");
        Assert.Equal(HttpStatusCode.Created, publication.StatusCode);
        Assert.Equal(
            "/design/activities/publications/publication%2Flocation",
            publication.Headers.Location?.OriginalString);

        using var start = await SendAsync(
            host.Client,
            HttpMethod.Post,
            "/publishing/activity-drafts/draft-route/test-runs",
            "{\"expectedRevision\":1,\"idempotencyKey\":\"run-1\"}",
            "trusted-success");
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);

        using var cancel = await SendAsync(
            host.Client,
            HttpMethod.Post,
            "/publishing/activity-test-runs/test-run-route/cancel",
            identity: "trusted-success");
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
    }

    [Fact]
    public async Task Ordinary_catalog_and_policy_routes_remain_200()
    {
        await using var host = await PublishingMinimalApiHost.StartAsync();
        using var activities = await SendAsync(host.Client, HttpMethod.Get, "/publishing/activities", identity: "trusted-success");
        using var policy = await SendAsync(host.Client, HttpMethod.Get, "/publishing/workflows/definition-route/policy", identity: "trusted-success");

        Assert.Equal(HttpStatusCode.OK, activities.StatusCode);
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
    }

    [Fact]
    public async Task Domain_and_custom_error_families_keep_their_status_and_safe_payloads()
    {
        var factory = new RecordingRequestSenderFactory();
        await using var host = await PublishingMinimalApiHost.StartAsync(factory.Create);

        using var notFound = await SendAsync(
            host.Client,
            HttpMethod.Get,
            "/publishing/workflows/missing-definition/slots/default",
            identity: "trusted-domain-not-found");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);

        using var conflict = await SendAsync(
            host.Client,
            HttpMethod.Post,
            "/publishing/workflows/version-route/publish",
            "{}",
            "trusted-domain-conflict");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var runtimeUnavailable = await SendAsync(
            host.Client,
            HttpMethod.Post,
            "/publishing/preflight",
            "{\"scope\":\"ActiveRetainedArtifacts\",\"artifactIds\":null}",
            "trusted-domain-unavailable");
        Assert.Equal(HttpStatusCode.BadRequest, runtimeUnavailable.StatusCode);

        using var diagnostics = await SendAsync(
            host.Client,
            HttpMethod.Post,
            "/design/activities/drafts/draft-route/publication-preflight",
            "{\"expectedDraftRevision\":8}",
            "trusted-activity-diagnostics");
        Assert.Equal((HttpStatusCode)422, diagnostics.StatusCode);
        var diagnosticsBody = await diagnostics.Content.ReadAsStringAsync();
        Assert.Contains("activity.publication.invalid", diagnosticsBody, StringComparison.Ordinal);
        Assert.Contains("The activity contract is invalid.", diagnosticsBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unexpected_failures_are_500_and_do_not_leak_exception_details()
    {
        var factory = new RecordingRequestSenderFactory();
        await using var host = await PublishingMinimalApiHost.StartAsync(factory.Create);
        using var response = await SendAsync(
            host.Client,
            HttpMethod.Post,
            "/design/activities/drafts/draft-route/publication-preflight",
            "{\"expectedDraftRevision\":8}",
            "trusted-generic-500");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private provider payload", body, StringComparison.Ordinal);
        Assert.Contains("activity.operation.failed", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET", "/publishing/workflows/definition-route/policy")]
    [InlineData("PUT", "/publishing/workflows/definition-route/policy")]
    public async Task Unexpected_policy_store_failures_preserve_legacy_problem_details(
        string method,
        string path)
    {
        await using var host = await PublishingMinimalApiScenarioHost.StartAsync(
            configureServices: services =>
            {
                services.RemoveAll<IPublicationPolicyStore>();
                services.AddSingleton<IPublicationPolicyStore, ThrowingPublicationPolicyStore>();
            });

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-store-500");
        if (method == "PUT")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private store payload", body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.Equal("One or more errors occurred.", problem.GetProperty("title").GetString());
        Assert.Equal("https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1", problem.GetProperty("type").GetString());
        Assert.Equal("generalErrors", problem.GetProperty("errors")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Cancellation_is_propagated_as_the_same_request_operation_and_not_translated_to_500()
    {
        var factory = new RecordingRequestSenderFactory();
        await using var host = await PublishingMinimalApiHost.StartAsync(factory.Create);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/publishing/activity-test-runs/test-run-route");
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-cancellation");

        var observation = await CaptureFaultAsync(host.Client, new HttpCompatibilityCase(
            new Elsa.Api.Compatibility.Testing.Manifests.EndpointIdentity(
                "/publishing/activity-test-runs/{testRunId}", "GET"),
            "cancellation",
            () => request));

        Assert.Equal(0, observation.StatusCode);
        Assert.StartsWith("Faulted:System.OperationCanceledException", observation.TerminalState, StringComparison.Ordinal);
        Assert.True(factory.Sender.LastCancellation.CanBeCanceled);
    }

    private static async Task<HttpResponseMessage> PublishWorkflowAsync(HttpClient client, string token)
    {
        return await SendAsync(
            client,
            HttpMethod.Post,
            "/publishing/workflows/version-route/publish",
            $"{{\"versionId\":\"version-route\",\"preflightToken\":\"{token}\"}}",
            "trusted-success");
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? body = null,
        string? identity = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, identity);
        if (body is not null)
            request.Content = Json(body);
        return await client.SendAsync(request);
    }

    private static async Task<HttpCompatibilityObservation> CaptureFaultAsync(
        HttpClient client,
        HttpCompatibilityCase testCase)
    {
        try
        {
            return await HttpEvidenceCapture.CaptureAsync(client, testCase);
        }
        catch (Exception exception)
        {
            var terminal = exception.GetBaseException();
            return new HttpCompatibilityObservation
            {
                Endpoint = testCase.Endpoint,
                Case = testCase.Case,
                StatusCode = 0,
                TerminalState = $"Faulted:{terminal.GetType().FullName}"
            };
        }
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}


internal sealed class ThrowingPublicationPolicyStore(IHttpContextAccessor contextAccessor) : IPublicationPolicyStore
{
    private readonly InMemoryPublicationPolicyStore inner = new();

    public ValueTask<PublicationPolicy?> FindAsync(string? workflowDefinitionId, CancellationToken cancellationToken = default) =>
        ShouldThrow() ? Fail<PublicationPolicy?>() : inner.FindAsync(workflowDefinitionId, cancellationToken);

    public ValueTask<PublicationPolicyWriteResult> TrySaveAsync(
        PublicationPolicy policy,
        long expectedRevision,
        CancellationToken cancellationToken = default) =>
        ShouldThrow() ? Fail<PublicationPolicyWriteResult>() : inner.TrySaveAsync(policy, expectedRevision, cancellationToken);

    private bool ShouldThrow() => string.Equals(
        contextAccessor.HttpContext?.Request.Headers[PublishingCompatibilityCases.IdentityHeader].ToString(),
        "trusted-store-500",
        StringComparison.Ordinal);

    private static ValueTask<T> Fail<T>() =>
        ValueTask.FromException<T>(new InvalidOperationException("private store payload"));
}
