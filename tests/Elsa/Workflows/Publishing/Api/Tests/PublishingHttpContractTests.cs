using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Services;
using Elsa.Mediator.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublishingHttpContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Activity_preflight_publish_and_receipt_routes_match_the_reviewed_contract()
    {
        using var provider = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new PublishingTestEndpointRouteBuilder(provider);
        WorkflowsPublishingApi.MapWorkflowsPublishingApi(routes);

        AssertRoute(routes, "PreflightActivityDraftPublicationEndpoint", "POST",
            "/design/activities/drafts/{draftId}/publication-preflight");
        AssertRoute(routes, "PublishActivityDraftEndpoint", "POST",
            "/design/activities/drafts/{draftId}/publish");
        AssertRoute(routes, "GetActivityPublicationReceiptEndpoint", "GET",
            "/design/activities/publications/{idempotencyKey}");

        Assert.Equal(
            "{\"expectedDraftRevision\":8,\"expectedDefinitionHeadVersionId\":\"activity-ver-1\",\"version\":\"2.0.0\"}",
            JsonSerializer.Serialize(
                new PreflightActivityDraftPublication("draft-1", 8, "activity-ver-1")
                {
                    Version = "2.0.0"
                },
                JsonOptions));

        var json = JsonSerializer.Serialize(
            new PublishActivityDraft(
                "draft-1",
                8,
                "activity-ver-1",
                "2.0.0",
                "review-sha256",
                "publish-operation-1"),
            JsonOptions);
        Assert.Equal(
            "{\"expectedDraftRevision\":8,\"expectedDefinitionHeadVersionId\":\"activity-ver-1\",\"version\":\"2.0.0\",\"reviewToken\":\"review-sha256\",\"idempotencyKey\":\"publish-operation-1\"}",
            json);
        Assert.DoesNotContain("providerFingerprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("directDependencies", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Activity_preflight_response_serializes_the_version_bound_by_its_review_token()
    {
        var response = new ActivityPublicationPreflightView(
            "draft-1",
            8,
            "definition-1",
            null,
            false,
            "sha256:review",
            true,
            "1.1.0",
            ["1.1.0"],
            null,
            [],
            [],
            new("Provider", "test.provider", "1", "Available", ["1"]),
            [],
            [],
            [])
        {
            ReviewedVersion = "7.3.2"
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));

        Assert.Equal("1.1.0", document.RootElement.GetProperty("minimumVersion").GetString());
        Assert.Equal("7.3.2", document.RootElement.GetProperty("reviewedVersion").GetString());
    }

    [Fact]
    public async Task Activity_publish_handler_forwards_only_public_inputs_and_maps_the_success_view()
    {
        var publisher = new RecordingPublisher(AppliedReceipt());
        var handler = new PublishActivityDraftRequestHandler(publisher);

        var view = await handler.Handle(
            new("draft-1", 8, null, "1.0.0", "review-sha256", "publish-operation-1"),
            CancellationToken.None);

        Assert.Equal(
            new PublishActivityDefinitionRequest(
                "draft-1",
                8,
                null,
                "1.0.0",
                "review-sha256",
                "publish-operation-1"),
            publisher.Request);
        Assert.Equal("Applied", view.Status);
        Assert.Equal("version-1", view.Outcome?.DefinitionVersionId);
        Assert.Equal("sha256:template-1", view.Outcome?.TemplateHash);

        var invalid = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            handler.Handle(
                new("draft-1", 8, null, "not-semver", "review-sha256", "publish-operation-2"),
                CancellationToken.None));
        Assert.Equal("activity.request.invalid", invalid.ErrorCode);
        Assert.Equal("activity.version.invalid", Assert.Single(invalid.Diagnostics).Code);
    }

    [Fact]
    public async Task Activity_publish_endpoint_returns_201_location_and_rfc7807_conflicts()
    {
        var responseView = ActivityPublicationReceiptView.From(AppliedReceipt());
        await using var successHost = await PublishingMinimalApiHost.StartAsync(
            _ => new ValueSender(responseView));
        using var success = await SendAsync(
            successHost,
            HttpMethod.Post,
            "/design/activities/drafts/draft-1/publish",
            "{\"expectedDraftRevision\":8,\"version\":\"1.0.0\",\"reviewToken\":\"review-sha256\",\"idempotencyKey\":\"publish-operation-1\"}");

        Assert.Equal(HttpStatusCode.Created, success.StatusCode);
        Assert.Equal(
            "/design/activities/publications/publish-operation-1",
            success.Headers.Location?.OriginalString);

        await using var conflictHost = await PublishingMinimalApiHost.StartAsync(
            _ => new ExceptionSender(new ActivityPublicationRejectedException(
                "activity.draft.stale-revision",
                "The activity draft revision is stale.",
                [],
                true)));
        using var conflict = await SendAsync(
            conflictHost,
            HttpMethod.Post,
            "/design/activities/drafts/draft-1/publish",
            "{\"expectedDraftRevision\":7,\"version\":\"1.0.0\",\"reviewToken\":\"review-sha256\",\"idempotencyKey\":\"publish-operation-2\"}");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"errorCode\":\"activity.draft.stale-revision\"", await conflict.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publishing_problem_details_order_diagnostics_and_serialize_string_severity_and_empty_metadata()
    {
        var subject = new ActivityDiagnosticSubject("ActivityDraft", "draft-1", "definition-1", Revision: 8);
        await using var host = await PublishingMinimalApiHost.StartAsync(
            _ => new ExceptionSender(new ActivityPublicationRejectedException(
                "activity.publication.invalid",
                "Publication was rejected.",
                [
                    new("z.warning", ActivityDiagnosticSeverity.Warning, "Warning", subject),
                    new("a.error", ActivityDiagnosticSeverity.Error, "Error", subject)
                ])));

        using var response = await SendAsync(
            host,
            HttpMethod.Post,
            "/design/activities/drafts/draft-1/publish",
            "{\"expectedDraftRevision\":8,\"version\":\"1.0.0\",\"reviewToken\":\"review-sha256\",\"idempotencyKey\":\"publish-operation-1\"}");

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var diagnostics = body.RootElement.GetProperty("diagnostics");
        Assert.Equal("a.error", diagnostics[0].GetProperty("code").GetString());
        Assert.Equal("Error", diagnostics[0].GetProperty("severity").GetString());
        Assert.Equal(JsonValueKind.Object, diagnostics[0].GetProperty("metadata").ValueKind);
        Assert.Empty(diagnostics[0].GetProperty("metadata").EnumerateObject());
        Assert.Equal("z.warning", diagnostics[1].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Test_run_not_found_is_404_and_unexpected_failures_are_safe_generic_500s()
    {
        var sender = new ExceptionSender(new ActivityPublicationRejectedException(
            "activity.draft.not-found",
            "The requested activity draft was not found.",
            []));
        await using var notFoundHost = await PublishingMinimalApiHost.StartAsync(_ => sender);
        using var notFound = await SendAsync(
            notFoundHost,
            HttpMethod.Post,
            "/publishing/activity-drafts/missing/test-runs",
            "{\"expectedRevision\":1,\"idempotencyKey\":\"run-1\"}");

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Contains("\"errorCode\":\"activity.draft.not-found\"", await notFound.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var unexpectedHost = await PublishingMinimalApiHost.StartAsync(
            _ => new ExceptionSender(new InvalidOperationException("secret provider payload")));
        using var unexpected = await SendAsync(
            unexpectedHost,
            HttpMethod.Post,
            "/publishing/activity-drafts/draft-1/test-runs",
            "{\"expectedRevision\":1,\"idempotencyKey\":\"run-1\"}");

        Assert.Equal(HttpStatusCode.InternalServerError, unexpected.StatusCode);
        var body = await unexpected.Content.ReadAsStringAsync();
        Assert.Contains("\"errorCode\":\"activity.operation.failed\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret provider payload", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activity_test_run_endpoints_bind_every_route_identity_explicitly()
    {
        var sender = new CapturingExceptionSender();
        await using var host = await PublishingMinimalApiHost.StartAsync(_ => sender);

        using (var start = await SendAsync(
                   host,
                   HttpMethod.Post,
                   "/publishing/activity-drafts/draft-routed/test-runs",
                   "{\"draftId\":\"draft-body\",\"expectedRevision\":7,\"idempotencyKey\":\"operation-1\"}"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, start.StatusCode);
            Assert.Equal("draft-routed", Assert.IsType<StartActivityDraftTestRun>(sender.Request).DraftId);
        }

        using (var get = await SendAsync(host, HttpMethod.Get, "/publishing/activity-test-runs/test-run-routed"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, get.StatusCode);
            Assert.Equal("test-run-routed", Assert.IsType<GetActivityDraftTestRun>(sender.Request).TestRunId);
        }

        using (var receipt = await SendAsync(
                   host,
                   HttpMethod.Get,
                   "/publishing/activity-drafts/draft-routed/test-runs/idempotency/operation-routed"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, receipt.StatusCode);
            var request = Assert.IsType<GetActivityDraftTestRunByIdempotencyKey>(sender.Request);
            Assert.Equal("draft-routed", request.DraftId);
            Assert.Equal("operation-routed", request.IdempotencyKey);
        }

        using (var cancel = await SendAsync(
                   host,
                   HttpMethod.Post,
                   "/publishing/activity-test-runs/test-run-routed/cancel"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, cancel.StatusCode);
            Assert.Equal("test-run-routed", Assert.IsType<CancelActivityDraftTestRun>(sender.Request).TestRunId);
        }
    }

    [Fact]
    public async Task Activity_publication_endpoints_bind_every_route_identity_explicitly()
    {
        var sender = new CapturingExceptionSender();
        await using var host = await PublishingMinimalApiHost.StartAsync(_ => sender);

        using (var preflight = await SendAsync(
                   host,
                   HttpMethod.Post,
                   "/design/activities/drafts/draft-routed/publication-preflight",
                   "{\"draftId\":\"draft-body\",\"expectedDraftRevision\":7}"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, preflight.StatusCode);
            Assert.Equal("draft-routed", Assert.IsType<PreflightActivityDraftPublication>(sender.Request).DraftId);
        }

        using (var publish = await SendAsync(
                   host,
                   HttpMethod.Post,
                   "/design/activities/drafts/draft-routed/publish",
                   "{\"draftId\":\"draft-body\",\"expectedDraftRevision\":7,\"version\":\"1.0.0\",\"reviewToken\":\"review-sha256\",\"idempotencyKey\":\"operation-1\"}"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, publish.StatusCode);
            Assert.Equal("draft-routed", Assert.IsType<PublishActivityDraft>(sender.Request).DraftId);
        }

        using (var receipt = await SendAsync(
                   host,
                   HttpMethod.Get,
                   "/design/activities/publications/operation-routed"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, receipt.StatusCode);
            Assert.Equal("operation-routed", Assert.IsType<GetActivityPublicationReceipt>(sender.Request).IdempotencyKey);
        }
    }

    [Fact]
    public async Task Foreign_exact_activity_reference_maps_to_non_disclosing_rfc7807_403()
    {
        await using var host = await PublishingMinimalApiHost.StartAsync(
            _ => new ExceptionSender(new ActivityPublicationRejectedException(
                "activity.tenant.reference-denied",
                "The requested activity identity is outside the caller's authorized scope.",
                [])));

        using var response = await SendAsync(
            host,
            HttpMethod.Post,
            "/design/activities/drafts/foreign-id/publish",
            "{\"expectedDraftRevision\":8,\"version\":\"1.0.0\",\"reviewToken\":\"review-sha256\",\"idempotencyKey\":\"publish-operation-1\"}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"errorCode\":\"activity.tenant.reference-denied\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preflight_route_request_handler_and_response_match_the_reviewed_contract()
    {
        using var provider = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new PublishingTestEndpointRouteBuilder(provider);
        WorkflowsPublishingApi.MapWorkflowsPublishingApi(routes);
        AssertRoute(routes, "RuntimeRequirementPreflightEndpoint", "POST", "/publishing/preflight");

        var request = new RunRuntimeRequirementPreflight(
            RuntimeRequirementPreflight.ActiveRetainedArtifactsScope,
            ["artifact-1"]);
        Assert.Equal(
            "{\"scope\":\"ActiveRetainedArtifacts\",\"artifactIds\":[\"artifact-1\"]}",
            JsonSerializer.Serialize(request, JsonOptions));

        var service = new RuntimeRequirementPreflight(
            new InMemoryWorkflowExecutableSourceReferenceStore(),
            new InMemoryWorkflowExecutableStore(),
            new InMemoryExecutableActivityTemplateStore(),
            new RuntimeRequirementChecker(
                [],
                new RuntimeDurableValueStorageDriverRegistry([]),
                new WellKnownTypeRegistry(),
                new JsonPayloadSerializer(new JsonPayloadConverterRegistry())),
            TimeProvider.System);
        var handler = new RunRuntimeRequirementPreflightRequestHandler(service);
        var result = await handler.Handle(request, CancellationToken.None);

        Assert.Equal(1, result.CheckedArtifactCount);
        Assert.False(result.IsReady);
        Assert.Empty(result.Requirements);
        Assert.Equal("activity.preflight.artifact-not-retained", Assert.Single(result.Diagnostics).Code);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        Assert.Contains("\"checkedArtifactCount\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"isReady\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"requirements\":[]", json, StringComparison.Ordinal);
        Assert.Contains("\"diagnostics\":[", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Executable_export_route_matches_the_reviewed_contract()
    {
        // FR-B-010a. Route and verb are pinned verbatim for elsa-foundation-studio#493, and the versioned-route
        // constraint is the shared one, so the reserved 'drafts' literal can never bind as a version id.
        using var provider = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new PublishingTestEndpointRouteBuilder(provider);
        WorkflowsPublishingApi.MapWorkflowsPublishingApi(routes);

        AssertRoute(routes, "ExportWorkflowExecutableClosureEndpoint", "GET",
            "/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/executable-export");
        Assert.Equal(
            "{\"versionId\":\"version-1\"}",
            JsonSerializer.Serialize(new ExportWorkflowExecutableClosure("version-1"), JsonOptions));
    }

    [Fact]
    public async Task Preflight_endpoint_maps_invalid_selection_to_rfc7807_400()
    {
        await using var host = await PublishingMinimalApiHost.StartAsync(
            _ => new ExceptionSender(new RuntimeRequirementPreflightRequestException("Invalid scope.")));
        using var response = await SendAsync(
            host,
            HttpMethod.Post,
            "/publishing/preflight",
            "{\"scope\":\"Everything\",\"artifactIds\":null}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"errorCode\":\"activity.request.invalid\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static void AssertRoute(PublishingTestEndpointRouteBuilder routes, string name, string method, string route)
    {
        var endpoint = routes.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ==
                                $"ElsaWorkflowsPublishingApiEndpoints{name}");
        Assert.Equal(method, Assert.Single(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods));
        Assert.Equal(route, endpoint.RoutePattern.RawText);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        PublishingMinimalApiHost host,
        HttpMethod method,
        string path,
        string? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await host.Client.SendAsync(request);
    }

    private static ActivityPublicationReceipt AppliedReceipt()
    {
        var publishedAt = new DateTimeOffset(2026, 7, 15, 12, 20, 0, TimeSpan.Zero);
        return new(
            null,
            "publish-operation-1",
            "sha256:request",
            ActivityPublicationReceiptStatus.Applied,
            "draft-1",
            8,
            null,
            "review-sha256",
            "1.0.0",
            new(
                "definition-1",
                "version-1",
                "draft-1",
                "1.0.0",
                "template-1",
                "sha256:template-1",
                "source-ref-1",
                publishedAt),
            null,
            [],
            publishedAt);
    }

    private static PublishActivityDefinitionResult PublishResult()
    {
        var publishedAt = new DateTimeOffset(2026, 7, 15, 12, 20, 0, TimeSpan.Zero);
        var root = new ExecutableNode(
            "root",
            "root",
            "test.consumer",
            "1",
            new("test.consumer", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>());
        var template = new ExecutableActivityTemplate(
            "template-1",
            "sha256:template-1",
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [],
            [],
            [new("test.consumer", "1")],
            "test.provider/compiler/1",
            new Dictionary<string, string>(),
            publishedAt);
        var reference = new WorkflowExecutableSourceReference(
            "source-ref-1",
            template.TemplateId,
            "ActivityDefinitionVersion",
            "version-1",
            "1.0.0",
            "definition-1",
            "version-1",
            "1.0.0",
            publishedAt,
            publishedAt,
            WorkflowExecutableReferenceScope.Published);
        var publication = new ActivityDefinitionVersionPublication
        {
            Id = "version-1",
            DefinitionId = "definition-1",
            DefinitionVersionId = "version-1",
            Version = "1.0.0",
            ActivityTypeKey = "test.activity",
            Contract = new("1", [], [], []),
            Provider = new("test.provider", "1", JsonSerializer.SerializeToElement(new { })),
            TemplateId = template.TemplateId,
            TemplateHash = template.TemplateHash,
            SourceReferenceId = reference.SourceReferenceId,
            ProviderFingerprint = template.ProviderFingerprint,
            DirectDependencyCount = 0,
            ClosedTemplateCount = 0,
            RuntimeRequirements = [new("test.consumer", "1")],
            PublishedAt = publishedAt
        };
        return new(
            new ActivityPublicationResult("definition-1", "version-1", "draft-1", template.TemplateId, reference.SourceReferenceId, publishedAt),
            publication,
            template,
            reference,
            new(1, 1, 0, 1, 1, 0, 0),
            null,
            []);
    }

    private sealed class RecordingPublisher(ActivityPublicationReceipt receipt) : IActivityDefinitionPublisher
    {
        public PublishActivityDefinitionRequest? Request { get; private set; }

        public Task<ActivityPublicationPreflightView> PreflightAsync(
            PreflightActivityDefinitionPublicationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ActivityPublicationReceipt> PublishReviewedAsync(
            PublishActivityDefinitionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(receipt);
        }

        public ValueTask<ActivityPublicationReceipt> GetReceiptAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(receipt);
    }

    private sealed class ValueSender(object value) : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            Task.FromResult((T)value);
    }

    private sealed class ExceptionSender(Exception exception) : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            Task.FromException<T>(exception);
    }

    private sealed class CapturingExceptionSender : IRequestSender
    {
        public object? Request { get; private set; }

        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            Request = request;
            return Task.FromException<T>(new InvalidOperationException("Captured endpoint request."));
        }
    }
}
