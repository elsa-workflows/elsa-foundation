using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Services;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublishingHttpContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Activity_preflight_publish_and_receipt_routes_match_the_reviewed_contract()
    {
        var preflightEndpoint = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PreflightActivityDraftPublicationEndpoint",
            new ThrowingSender());
        preflightEndpoint.Configure();

        Assert.Equal("POST", Assert.Single(preflightEndpoint.Definition.Verbs));
        Assert.Equal(
            "design/activities/drafts/{draftId}/publication-preflight",
            Assert.Single(preflightEndpoint.Definition.Routes));
        Assert.Equal(
            "{\"expectedDraftRevision\":8,\"expectedDefinitionHeadVersionId\":\"activity-ver-1\"}",
            JsonSerializer.Serialize(
                new PreflightActivityDraftPublication("draft-1", 8, "activity-ver-1"),
                JsonOptions));

        var publishEndpoint = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishActivityDraftEndpoint",
            new ThrowingSender());
        publishEndpoint.Configure();

        Assert.Equal("POST", Assert.Single(publishEndpoint.Definition.Verbs));
        Assert.Equal("design/activities/drafts/{draftId}/publish", Assert.Single(publishEndpoint.Definition.Routes));
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

        var receiptEndpoint = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.GetActivityPublicationReceiptEndpoint",
            new ThrowingSender());
        receiptEndpoint.Configure();
        Assert.Equal("GET", Assert.Single(receiptEndpoint.Definition.Verbs));
        Assert.Equal(
            "design/activities/publications/{idempotencyKey}",
            Assert.Single(receiptEndpoint.Definition.Routes));
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
        var response = ActivityPublicationReceiptView.From(AppliedReceipt());
        var success = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishActivityDraftEndpoint",
            new ValueSender(response));

        await InvokeHandleAsync(
            success,
            new PublishActivityDraft("draft-1", 8, null, "1.0.0", "review-sha256", "publish-operation-1"));

        Assert.Equal(StatusCodes.Status201Created, success.HttpContext.Response.StatusCode);
        Assert.Equal(
            "/design/activities/publications/publish-operation-1",
            success.HttpContext.Response.Headers.Location);

        var conflict = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishActivityDraftEndpoint",
            new ExceptionSender(new ActivityPublicationRejectedException(
                "activity.draft.stale-revision",
                "The activity draft revision is stale.",
                [],
                true)));
        await InvokeHandleAsync(
            conflict,
            new PublishActivityDraft("draft-1", 7, null, "1.0.0", "review-sha256", "publish-operation-2"));

        Assert.Equal(StatusCodes.Status409Conflict, conflict.HttpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", conflict.HttpContext.Response.ContentType);
        Assert.Contains("\"errorCode\":\"activity.draft.stale-revision\"", await ResponseBodyAsync(conflict), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publishing_problem_details_order_diagnostics_and_serialize_string_severity_and_empty_metadata()
    {
        var subject = new ActivityDiagnosticSubject("ActivityDraft", "draft-1", "definition-1", Revision: 8);
        var endpoint = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishActivityDraftEndpoint",
            new ExceptionSender(new ActivityPublicationRejectedException(
                "activity.publication.invalid",
                "Publication was rejected.",
                [
                    new("z.warning", ActivityDiagnosticSeverity.Warning, "Warning", subject),
                    new("a.error", ActivityDiagnosticSeverity.Error, "Error", subject)
                ])));

        await InvokeHandleAsync(
            endpoint,
            new PublishActivityDraft("draft-1", 8, null, "1.0.0", "review-sha256", "publish-operation-1"));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, endpoint.HttpContext.Response.StatusCode);
        using var body = JsonDocument.Parse(await ResponseBodyAsync(endpoint));
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
        var notFound = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.ActivityDraftTestRunEndpoint",
            new ExceptionSender(new ActivityPublicationRejectedException(
                "activity.draft.not-found",
                "The requested activity draft was not found.",
                [])));
        notFound.HttpContext.Request.RouteValues["draftId"] = "missing";

        await InvokeHandleAsync(notFound, new StartActivityDraftTestRun("missing", 1, "run-1"));

        Assert.Equal(StatusCodes.Status404NotFound, notFound.HttpContext.Response.StatusCode);
        Assert.Contains("\"errorCode\":\"activity.draft.not-found\"", await ResponseBodyAsync(notFound), StringComparison.Ordinal);

        var unexpected = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.ActivityDraftTestRunEndpoint",
            new ExceptionSender(new InvalidOperationException("secret provider payload")));
        unexpected.HttpContext.Request.RouteValues["draftId"] = "draft-1";
        await InvokeHandleAsync(unexpected, new StartActivityDraftTestRun("draft-1", 1, "run-1"));

        Assert.Equal(StatusCodes.Status500InternalServerError, unexpected.HttpContext.Response.StatusCode);
        var body = await ResponseBodyAsync(unexpected);
        Assert.Contains("\"errorCode\":\"activity.operation.failed\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret provider payload", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activity_test_run_endpoints_bind_every_route_identity_explicitly()
    {
        var startSender = new CapturingExceptionSender();
        var start = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.ActivityDraftTestRunEndpoint",
            startSender);
        start.HttpContext.Request.RouteValues["draftId"] = "draft-routed";
        await InvokeHandleAsync(start, new StartActivityDraftTestRun(null!, 7, "operation-1"));
        Assert.Equal("draft-routed", Assert.IsType<StartActivityDraftTestRun>(startSender.Request).DraftId);

        var getSender = new CapturingExceptionSender();
        var get = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.GetActivityDraftTestRunEndpoint",
            getSender);
        get.HttpContext.Request.RouteValues["testRunId"] = "test-run-routed";
        await InvokeHandleWithoutRequestAsync(get);
        Assert.Equal("test-run-routed", Assert.IsType<GetActivityDraftTestRun>(getSender.Request).TestRunId);

        var receiptSender = new CapturingExceptionSender();
        var receipt = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.GetActivityDraftTestRunByIdempotencyKeyEndpoint",
            receiptSender);
        receipt.HttpContext.Request.RouteValues["draftId"] = "draft-routed";
        receipt.HttpContext.Request.RouteValues["idempotencyKey"] = "operation-routed";
        await InvokeHandleWithoutRequestAsync(receipt);
        var receiptRequest = Assert.IsType<GetActivityDraftTestRunByIdempotencyKey>(receiptSender.Request);
        Assert.Equal("draft-routed", receiptRequest.DraftId);
        Assert.Equal("operation-routed", receiptRequest.IdempotencyKey);

        var cancelSender = new CapturingExceptionSender();
        var cancel = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.CancelActivityDraftTestRunEndpoint",
            cancelSender);
        cancel.HttpContext.Request.RouteValues["testRunId"] = "test-run-routed";
        await InvokeHandleWithoutRequestAsync(cancel);
        Assert.Equal("test-run-routed", Assert.IsType<CancelActivityDraftTestRun>(cancelSender.Request).TestRunId);
    }

    [Fact]
    public async Task Activity_publication_endpoints_bind_every_route_identity_explicitly()
    {
        var preflightSender = new CapturingExceptionSender();
        var preflight = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PreflightActivityDraftPublicationEndpoint",
            preflightSender);
        preflight.HttpContext.Request.RouteValues["draftId"] = "draft-routed";
        await InvokeHandleAsync(preflight, new PreflightActivityDraftPublication(null!, 7, null));
        Assert.Equal(
            "draft-routed",
            Assert.IsType<PreflightActivityDraftPublication>(preflightSender.Request).DraftId);

        var publishSender = new CapturingExceptionSender();
        var publish = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishActivityDraftEndpoint",
            publishSender);
        publish.HttpContext.Request.RouteValues["draftId"] = "draft-routed";
        await InvokeHandleAsync(
            publish,
            new PublishActivityDraft(
                "draft-body",
                7,
                null,
                "1.0.0",
                "review-sha256",
                "operation-1"));
        Assert.Equal(
            "draft-routed",
            Assert.IsType<PublishActivityDraft>(publishSender.Request).DraftId);

        var receiptSender = new CapturingExceptionSender();
        var receipt = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.GetActivityPublicationReceiptEndpoint",
            receiptSender);
        receipt.HttpContext.Request.RouteValues["idempotencyKey"] = "operation-routed";
        await InvokeHandleWithoutRequestAsync(receipt);
        Assert.Equal(
            "operation-routed",
            Assert.IsType<GetActivityPublicationReceipt>(receiptSender.Request).IdempotencyKey);
    }

    [Fact]
    public async Task Foreign_exact_activity_reference_maps_to_non_disclosing_rfc7807_403()
    {
        var endpoint = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishActivityDraftEndpoint",
            new ExceptionSender(new ActivityPublicationRejectedException(
                "activity.tenant.reference-denied",
                "The requested activity identity is outside the caller's authorized scope.",
                [])));

        await InvokeHandleAsync(
            endpoint,
            new PublishActivityDraft("foreign-id", 8, null, "1.0.0", "review-sha256", "publish-operation-1"));

        Assert.Equal(StatusCodes.Status403Forbidden, endpoint.HttpContext.Response.StatusCode);
        var body = await ResponseBodyAsync(endpoint);
        Assert.Contains("\"errorCode\":\"activity.tenant.reference-denied\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preflight_route_request_handler_and_response_match_the_reviewed_contract()
    {
        var endpoint = CreateEndpoint("Elsa.Workflows.Publishing.Api.Endpoints.RuntimeRequirementPreflightEndpoint", new ThrowingSender());
        endpoint.Configure();

        Assert.Equal("POST", Assert.Single(endpoint.Definition.Verbs));
        Assert.Equal("publishing/preflight", Assert.Single(endpoint.Definition.Routes));
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
            [],
            new RuntimeDurableValueStorageDriverRegistry([]),
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
    public async Task Preflight_endpoint_maps_invalid_selection_to_rfc7807_400()
    {
        var endpoint = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.RuntimeRequirementPreflightEndpoint",
            new ExceptionSender(new RuntimeRequirementPreflightRequestException("Invalid scope.")));

        await InvokeHandleAsync(endpoint, new RunRuntimeRequirementPreflight("Everything", null));

        Assert.Equal(StatusCodes.Status400BadRequest, endpoint.HttpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", endpoint.HttpContext.Response.ContentType);
        Assert.Contains("\"errorCode\":\"activity.request.invalid\"", await ResponseBodyAsync(endpoint), StringComparison.Ordinal);
    }

    private static BaseEndpoint CreateEndpoint(string typeName, IRequestSender sender)
    {
        var endpointType = typeof(PublishActivityDraft).Assembly.GetType(typeName, throwOnError: true)!;
        var loggerType = typeof(NullLogger<>).MakeGenericType(endpointType);
        var logger = loggerType.GetProperty(nameof(NullLogger<object>.Instance))?.GetValue(null)
                     ?? loggerType.GetField(nameof(NullLogger<object>.Instance))!.GetValue(null)!;
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) && method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var second] &&
                              first.ParameterType == typeof(Action<DefaultHttpContext>) && second.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        Action<DefaultHttpContext> configure = context => context.Response.Body = new MemoryStream();
        return (BaseEndpoint)create.Invoke(null, [configure, new object[] { sender, logger }])!;
    }

    private static Task InvokeHandleAsync<TRequest>(BaseEndpoint endpoint, TRequest request)
    {
        if (!endpoint.HttpContext.Request.RouteValues.ContainsKey("draftId"))
        {
            endpoint.HttpContext.Request.RouteValues["draftId"] = request switch
            {
                PreflightActivityDraftPublication preflight => preflight.DraftId,
                PublishActivityDraft publish => publish.DraftId,
                StartActivityDraftTestRun testRun => testRun.DraftId,
                _ => null
            };
        }

        return (Task)endpoint.GetType().GetMethod("HandleAsync", [typeof(TRequest), typeof(CancellationToken)])!
            .Invoke(endpoint, [request, CancellationToken.None])!;
    }

    private static Task InvokeHandleWithoutRequestAsync(BaseEndpoint endpoint) =>
        (Task)endpoint.GetType().GetMethod("HandleAsync", [typeof(CancellationToken)])!
            .Invoke(endpoint, [CancellationToken.None])!;

    private static async Task<string> ResponseBodyAsync(BaseEndpoint endpoint)
    {
        endpoint.HttpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(endpoint.HttpContext.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
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

    private sealed class ThrowingSender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            throw new InvalidOperationException("Configuration-only test.");
    }
}
