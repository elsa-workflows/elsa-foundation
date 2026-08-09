using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PublishWorkflowCommand = Elsa.Workflows.Publishing.Core.Requests.PublishWorkflow;
using PublishWorkflowRequest = Elsa.Workflows.Publishing.Api.Requests.PublishWorkflowRequest;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

[SuccessStatus(201, 200,
    Condition = "201 Created when this publish creates the publication; 200 OK when it updates an existing publication in place (republishing a definition into the slot it already occupies).")]
internal sealed class PublishWorkflowEndpoint(IRequestSender requestSender, ILogger<PublishWorkflowEndpoint> logger)
    : ElsaEndpoint<PublishWorkflowRequest, PublishedWorkflowView>
{
    public override void Configure()
    {
        Post(RouteConstants.WorkflowPublish);
        ConfigurePermissions(PermissionNames.WorkflowPublishingManage);
    }

    public override async Task HandleAsync(PublishWorkflowRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await requestSender.Send(
                new PublishWorkflowCommand(
                    request.VersionId,
                    request.Action is { } action ? PublicationIntentContract.ToModel(action) : null,
                    request.SlotName,
                    request.ExpectedPublicationId,
                    request.PreflightToken,
                    PublicationRequestTenant.Resolve(User)),
                cancellationToken);
            await Send.ResponseAsync(response, response.WasCreated ? 201 : 200, cancellationToken);
        }
        catch (EntityNotFoundException exception)
        {
            ThrowError(exception.Message, 404);
        }
        catch (PublicationPreflightConflictException exception)
        {
            ThrowError(exception.Message, 409);
        }
        catch (PublicationSnapshotReviewException exception)
        {
            ThrowError(exception.Message, 409);
        }
        catch (PublicationActivationException exception)
        {
            ThrowError(exception.Message, 409);
        }
        catch (PublicationPolicyResolutionException exception)
        {
            ThrowError(exception.Message, exception.Code == "expected_publication_mismatch" ? 409 : 400);
        }
        catch (ExpressionPublicationValidationException exception)
        {
            await ExpressionPublicationValidationProblems.WriteAsync(
                HttpContext.Response,
                ExpressionPublicationValidationProblems.Create(exception, HttpContext),
                cancellationToken);
        }
        catch (Exception exception) when (ValueConversionPublicationProblems.TryFind(exception, out var conversion))
        {
            await ValueConversionPublicationProblems.WriteAsync(
                HttpContext.Response,
                ValueConversionPublicationProblems.Create(conversion, HttpContext, request.VersionId),
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            ThrowError(exception.Message, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected workflow publication failure for version '{VersionId}'.", request.VersionId);
            ThrowError("Unexpected error occurred", 500);
        }
    }
}

internal sealed record ExpressionPublicationValidationProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string ErrorCode,
    string TraceId,
    string ValidationState,
    IReadOnlyList<ExpressionPublicationValidationDiagnostic> Diagnostics);

internal static class ExpressionPublicationValidationProblems
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static ExpressionPublicationValidationProblemDetails Create(
        ExpressionPublicationValidationException exception,
        HttpContext context) => new(
        $"https://elsa.dev/problems/{exception.Code}",
        "Workflow publication expression validation was rejected",
        StatusCode(exception.State),
        exception.Message,
        context.Request.Path.Value ?? string.Empty,
        exception.Code,
        context.TraceIdentifier,
        exception.State.ToString().ToLowerInvariant(),
        exception.Diagnostics);

    public static async Task WriteAsync(
        HttpResponse response,
        ExpressionPublicationValidationProblemDetails problem,
        CancellationToken cancellationToken)
    {
        response.StatusCode = problem.Status;
        response.ContentType = "application/problem+json";
        await System.Text.Json.JsonSerializer.SerializeAsync(response.Body, problem, JsonOptions, cancellationToken);
    }

    private static int StatusCode(ExpressionDraftValidationState state) => state switch
    {
        ExpressionDraftValidationState.Errors => StatusCodes.Status422UnprocessableEntity,
        ExpressionDraftValidationState.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status409Conflict
    };
}
