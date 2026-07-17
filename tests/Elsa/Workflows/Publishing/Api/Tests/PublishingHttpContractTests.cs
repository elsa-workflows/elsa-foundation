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
    public void Activity_publish_route_and_body_match_the_reviewed_contract()
    {
        var endpoint = CreateEndpoint("Elsa.Workflows.Publishing.Api.Endpoints.PublishActivityDraftEndpoint", new ThrowingSender());
        endpoint.Configure();

        Assert.Equal("POST", Assert.Single(endpoint.Definition.Verbs));
        Assert.Equal("design/activities/drafts/{draftId}/publish", Assert.Single(endpoint.Definition.Routes));
        var json = JsonSerializer.Serialize(new PublishActivityDraft("draft-1", 8, "activity-ver-1", "2.0.0"), JsonOptions);
        Assert.Equal(
            "{\"expectedDraftRevision\":8,\"expectedDefinitionHeadVersionId\":\"activity-ver-1\",\"version\":\"2.0.0\"}",
            json);
        Assert.DoesNotContain("providerFingerprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("directDependencies", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activity_publish_handler_forwards_only_public_inputs_and_maps_the_success_view()
    {
        var publisher = new RecordingPublisher(PublishResult());
        var handler = new PublishActivityDraftRequestHandler(publisher);

        var view = await handler.Handle(new("draft-1", 8, null, "1.0.0"), CancellationToken.None);

        Assert.Equal(new PublishActivityDefinitionRequest("draft-1", 8, null, "1.0.0"), publisher.Request);
        Assert.Equal("test.provider/compiler/1", view.Provider.Fingerprint);
        Assert.Equal("test.provider", view.Provider.ProviderKey);
        Assert.Equal("1", view.Provider.SchemaVersion);
        Assert.Equal("version-1", view.VersionId);
        Assert.Null(view.Diff);

        var invalid = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() =>
            handler.Handle(new("draft-1", 8, null, "not-semver"), CancellationToken.None));
        Assert.Equal("activity.request.invalid", invalid.ErrorCode);
        Assert.Equal("activity.version.invalid", Assert.Single(invalid.Diagnostics).Code);
    }

    [Fact]
    public async Task Activity_publish_endpoint_returns_201_location_and_rfc7807_conflicts()
    {
        var response = PublishedView();
        var success = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishActivityDraftEndpoint",
            new ValueSender(response));

        await InvokeHandleAsync(success, new PublishActivityDraft("draft-1", 8, null, "1.0.0"));

        Assert.Equal(StatusCodes.Status201Created, success.HttpContext.Response.StatusCode);
        Assert.Equal("/design/activities/versions/version-1", success.HttpContext.Response.Headers.Location);

        var conflict = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishActivityDraftEndpoint",
            new ExceptionSender(new ActivityPublicationRejectedException(
                "activity.draft.stale-revision",
                "The activity draft revision is stale.",
                [],
                true)));
        await InvokeHandleAsync(conflict, new PublishActivityDraft("draft-1", 7, null, "1.0.0"));

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

        await InvokeHandleAsync(endpoint, new PublishActivityDraft("draft-1", 8, null, "1.0.0"));

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

        await InvokeHandleAsync(notFound, new StartActivityDraftTestRun("missing", 1));

        Assert.Equal(StatusCodes.Status404NotFound, notFound.HttpContext.Response.StatusCode);
        Assert.Contains("\"errorCode\":\"activity.draft.not-found\"", await ResponseBodyAsync(notFound), StringComparison.Ordinal);

        var unexpected = CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.ActivityDraftTestRunEndpoint",
            new ExceptionSender(new InvalidOperationException("secret provider payload")));
        await InvokeHandleAsync(unexpected, new StartActivityDraftTestRun("draft-1", 1));

        Assert.Equal(StatusCodes.Status500InternalServerError, unexpected.HttpContext.Response.StatusCode);
        var body = await ResponseBodyAsync(unexpected);
        Assert.Contains("\"errorCode\":\"activity.operation.failed\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret provider payload", body, StringComparison.Ordinal);
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

        await InvokeHandleAsync(endpoint, new PublishActivityDraft("foreign-id", 8, null, "1.0.0"));

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

    private static Task InvokeHandleAsync<TRequest>(BaseEndpoint endpoint, TRequest request) =>
        (Task)endpoint.GetType().GetMethod("HandleAsync", [typeof(TRequest), typeof(CancellationToken)])!
            .Invoke(endpoint, [request, CancellationToken.None])!;

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

    private static PublishedActivityDefinitionView PublishedView() =>
        PublishedActivityDefinitionView.From(PublishResult());

    private sealed class RecordingPublisher(PublishActivityDefinitionResult result) : IActivityDefinitionPublisher
    {
        public PublishActivityDefinitionRequest? Request { get; private set; }

        public Task<PublishActivityDefinitionResult> PublishAsync(
            PublishActivityDefinitionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(result);
        }
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

    private sealed class ThrowingSender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            throw new InvalidOperationException("Configuration-only test.");
    }
}
