using Elsa.Activities.Design.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>Minimal API binding and route-selection bites for all identity-bearing Publishing shapes.</summary>
public sealed class PublishingMinimalApiBindingTests
{
    [Fact]
    public async Task Reserved_drafts_route_wins_over_the_parameterized_version_route()
    {
        var senderFactory = new RecordingRequestSenderFactory();
        await using var host = await PublishingMinimalApiHost.StartAsync(senderFactory.Create);

        using var response = await SendAuthenticatedAsync(host.Client,
            "/publishing/workflows/drafts/test-runs",
            """
                {
                  "definitionId": "definition-body",
                  "snapshotId": "snapshot-body",
                  "state": {},
                  "artifactVersion": "draft"
                }
                """);

        Assert.Equal(StatusCodes.Status200OK, (int)response.StatusCode);
        var starter = host.App.Services.GetRequiredService<CaptureWorkflowTestRunStarter>();
        Assert.NotNull(starter.LastStartDraft);
        Assert.Null(starter.LastStart);
    }

    [Fact]
    public async Task Route_values_override_conflicting_workflow_and_activity_body_values()
    {
        var senderFactory = new RecordingRequestSenderFactory();
        await using var host = await PublishingMinimalApiHost.StartAsync(senderFactory.Create);

        using var workflow = await SendAuthenticatedAsync(host.Client,
            "/publishing/workflows/version-route/publish",
            """
                {
                  "versionId": "version-body",
                  "slotName": "body-slot",
                  "preflightToken": "body-token"
                }
                """);
        Assert.Equal(StatusCodes.Status201Created, (int)workflow.StatusCode);
        var publish = Assert.IsType<PublishWorkflow>(senderFactory.Sender.LastRequest);
        Assert.Equal("version-route", publish.VersionId);
        Assert.Equal("body-slot", publish.SlotName);

        using var activity = await SendAuthenticatedAsync(host.Client,
            "/publishing/activity-drafts/draft-route/test-runs",
            """
                {
                  "draftId": "draft-body",
                  "expectedRevision": 7,
                  "idempotencyKey": "run-body"
                }
                """);
        Assert.Equal(StatusCodes.Status202Accepted, (int)activity.StatusCode);
        var run = Assert.IsType<StartActivityDraftTestRun>(
            host.App.Services.GetRequiredService<CaptureActivityTestRunService>().LastStart);
        Assert.Equal("draft-route", run.DraftId);
        Assert.Equal("run-body", run.IdempotencyKey);
    }

    [Fact]
    public async Task Activity_preflight_route_identity_is_not_serialized_as_a_body_field()
    {
        var senderFactory = new RecordingRequestSenderFactory();
        await using var host = await PublishingMinimalApiHost.StartAsync(senderFactory.Create);

        using var response = await SendAuthenticatedAsync(host.Client,
            "/design/activities/drafts/draft-route/publication-preflight",
            "{\"expectedDraftRevision\":8}");

        Assert.Equal(StatusCodes.Status200OK, (int)response.StatusCode);
        var request = host.App.Services.GetRequiredService<CaptureActivityPublisher>().LastPreflight;
        Assert.NotNull(request);
        Assert.Equal("draft-route", request.DraftId);
        Assert.Equal(8, request.ExpectedDraftRevision);
        Assert.Null(request.ExpectedDefinitionHeadVersionId);
    }

    [Theory]
    [InlineData("/publishing/workflows/preflight", "{", "application/json", 400)]
    [InlineData("/publishing/workflows/preflight", "null", "application/json", 400)]
    [InlineData("/design/activities/drafts/draft-route/publish", "", "application/json", 400)]
    [InlineData("/design/activities/drafts/draft-route/publication-preflight", "{}", "text/plain", 415)]
    public async Task Malformed_null_empty_and_unsupported_content_are_rejected_without_invoking_the_sender(
        string path,
        string body,
        string contentType,
        int expectedStatus)
    {
        var senderFactory = new RecordingRequestSenderFactory();
        await using var host = await PublishingMinimalApiHost.StartAsync(senderFactory.Create);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-binding");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Null(senderFactory.Sender.LastRequest);
    }

    [Fact]
    public async Task Request_body_io_failure_is_logged_and_translated_without_leaking_infrastructure_details()
    {
        await using var host = await PublishingMinimalApiScenarioHost.StartAsync(
            configurePipeline: app => app.Use(async (context, next) =>
            {
                if (context.Request.Headers.ContainsKey("X-Throw-On-Read"))
                    context.Request.Body = new ThrowingReadStream(new IOException("private stream payload"));
                await next(context);
            }));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publishing/workflows/preflight")
        {
            Content = Json("{}")
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-stream-failure");
        request.Headers.TryAddWithoutValidation("X-Throw-On-Read", "true");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(StatusCodes.Status500InternalServerError, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private stream payload", body, StringComparison.Ordinal);
        Assert.Contains("Unexpected error occurred", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_body_reader_is_logged_and_translated_without_leaking_configuration_details()
    {
        await using var host = await PublishingMinimalApiScenarioHost.StartAsync(
            configurePipeline: app => app.Use(async (context, next) =>
            {
                if (context.Request.Headers.ContainsKey("X-Throw-On-Read"))
                    context.Request.Body = new ThrowingReadStream(new NotSupportedException("private resolver payload"));
                await next(context);
            }));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/publishing/workflows/preflight")
        {
            Content = Json("{}")
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-resolver-failure");
        request.Headers.TryAddWithoutValidation("X-Throw-On-Read", "true");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(StatusCodes.Status500InternalServerError, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private resolver payload", body, StringComparison.Ordinal);
        Assert.Contains("Unexpected error occurred", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_body_shape_has_a_declared_json_request_contract_and_bodyless_route_has_none()
    {
        using var provider = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var routes = new BindingEndpointRouteBuilder(provider);
        // This deliberately inspects the public mapper rather than the old FastEndpoints endpoint classes.
        WorkflowsPublishingApi.MapWorkflowsPublishingApi(routes);

        foreach (var endpoint in routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>())
        {
            var route = PublishingCurrentSurface.Manifest.Single(candidate =>
                candidate.Endpoint.Method.Value == endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single() &&
                NormalizeRoute(endpoint.RoutePattern.RawText!) == NormalizeRoute(candidate.Endpoint.Route.Value));
            var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
            if (HasRequestMetadata(route))
            {
                Assert.True(accepts is not null, $"{route.Id} is missing request metadata for {route.Request}.");
                var contentTypes = accepts.ContentTypes;
                Assert.Contains("application/json", contentTypes);
                if (route.Endpoint.Method.Value is "GET" or "DELETE")
                    Assert.Contains("*/*", contentTypes);
            }
            else
                Assert.Null(accepts);
        }
    }

    private static string NormalizeRoute(string value) =>
        Regex.Replace("/" + value.TrimStart('/'), "\\{[^{}]+\\}", "{param}", RegexOptions.CultureInvariant);

    private static bool HasRequestMetadata(PublishingRoute route) => route.Id is not
        ("IncidentStrategies.List" or
        "ValueConversionProfiles.List" or
        "WorkflowExecutable.Export" or
        "ActivityPublications.GetReceipt" or
        "ActivityTestRuns.Get" or
        "ActivityTestRuns.GetByIdempotencyKey" or
        "ActivityTestRuns.Cancel");

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> SendAuthenticatedAsync(HttpClient client, string path, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = Json(body)
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        return await client.SendAsync(request);
    }
}

/// <summary>Records request objects while retaining the deterministic response/error behavior of the before corpus.</summary>
internal sealed class RecordingRequestSenderFactory
{
    public PublishingRecordingRequestSender Sender { get; private set; } = null!;

    public IRequestSender Create(IServiceProvider services)
    {
        var sender = new PublishingRecordingRequestSender(services.GetRequiredService<IHttpContextAccessor>());
        Sender = sender;
        return sender;
    }
}

internal sealed class PublishingRecordingRequestSender(IHttpContextAccessor contextAccessor) : IRequestSender
{
    private readonly CaptureRequestSender _inner = new(contextAccessor);

    public object? LastRequest { get; private set; }
    public CancellationToken LastCancellation { get; private set; }
    public bool? WorkflowWasCreated { get; set; }
    public string? ActivityIdempotencyKey { get; set; }

    public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
    {
        LastRequest = request;
        LastCancellation = cancellationToken;
        var scenario = contextAccessor.HttpContext?.Request.Headers[PublishingCompatibilityCases.IdentityHeader].ToString();
        if (scenario == "trusted-generic-500")
            throw new InvalidOperationException("private provider payload");
        if (scenario == "trusted-activity-diagnostics")
        {
            var subject = new ActivityDiagnosticSubject("ActivityDraft", "draft-route", "definition-route", Revision: 8);
            throw new ActivityPublicationRejectedException(
                "activity.publication.invalid",
                "Activity publication validation failed.",
                [new ActivityDiagnostic("activity.contract.invalid", ActivityDiagnosticSeverity.Error, "The activity contract is invalid.", subject)]);
        }
        var result = await _inner.Send(request, cancellationToken);
        if (WorkflowWasCreated is not null && result is PublishedWorkflowView workflow)
            return (T)(object)(workflow with { WasCreated = WorkflowWasCreated.Value });
        if (ActivityIdempotencyKey is not null && result is ActivityPublicationReceiptView receipt)
            return (T)(object)(receipt with { IdempotencyKey = ActivityIdempotencyKey });
        return result;
    }
}

internal sealed class BindingEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
{
    public IServiceProvider ServiceProvider { get; } = serviceProvider;
    public ICollection<EndpointDataSource> DataSources { get; } = [];
    public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
}

internal sealed class ThrowingReadStream(Exception exception) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw exception;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<int>(exception);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
