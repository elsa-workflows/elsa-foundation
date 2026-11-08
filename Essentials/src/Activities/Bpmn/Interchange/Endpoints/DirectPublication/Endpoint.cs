using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NativeEndpoints;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints.DirectPublication;

internal static class BpmnDirectPublishingApi
{
    internal const string Owner = "Elsa.Activities.Bpmn.DirectPublishing";
    public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapEndpointGroup(Owner,
        BpmnDirectPublishingJsonContext.Default, jsonContentType: "application/json; charset=utf-8", tag: "BPMN")
        .MapEndpoint<Endpoint>();
}

internal sealed record PublishAuthoredBpmnRequest(string VersionId, string RequestId);

[Post("interchange/bpmn/versions/{versionId}/publish-direct")]
[RequirePermission(BpmnAuthoredDirectPublisher.PublishPermission)]
internal sealed class Endpoint(BpmnAuthoredDirectPublisher publisher) : ApiEndpoint<PublishAuthoredBpmnRequest, BpmnPublishedModel>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "PublishAuthoredDirectBpmn";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload;
    }

    public override Task<BpmnPublishedModel> HandleAsync(PublishAuthoredBpmnRequest request, CancellationToken cancellationToken) =>
        publisher.PublishAsync(HttpContext.Request.RouteValues["versionId"]?.ToString()
            ?? throw new ArgumentException("The authored version identity is missing."), request.RequestId, HttpContext.User, cancellationToken);
}

internal sealed class BpmnDirectPublishingFaultRenderer(IEndpointProblemWriter writer) : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        var problem = exception switch
        {
            BpmnModelResourceMissingException => EndpointProblem.General(StatusCodes.Status404NotFound, "The BPMN source is not available."),
            RuntimeCheckpointReplayConflictException => EndpointProblem.General(StatusCodes.Status409Conflict, "The publication request id was already used with different content."),
            SchemaWriteRefusedException refusal => SchemaWriteRefusalProblem.For(refusal),
            BpmnModelValidationException invalid => EndpointProblem.General(StatusCodes.Status422UnprocessableEntity, invalid.Message),
            NotSupportedException unsupported => EndpointProblem.General(StatusCodes.Status422UnprocessableEntity, unsupported.Message),
            BpmnInterchangeException invalid => EndpointProblem.General(StatusCodes.Status422UnprocessableEntity, invalid.Message),
            ArgumentException invalid => EndpointProblem.General(StatusCodes.Status400BadRequest, invalid.Message),
            JsonException => EndpointProblem.General(StatusCodes.Status400BadRequest, "The publication request is malformed."),
            _ => null
        };
        if (problem is null) return false;
        await writer.WriteAsync(context, problem);
        return true;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PublishAuthoredBpmnRequest))]
[JsonSerializable(typeof(BpmnPublishedModel))]
internal partial class BpmnDirectPublishingJsonContext : JsonSerializerContext;
