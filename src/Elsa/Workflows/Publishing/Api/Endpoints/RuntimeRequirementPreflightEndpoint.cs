using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using System.Text.Json;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class RuntimeRequirementPreflightEndpoint(
    IRequestSender requestSender,
    ILogger<RuntimeRequirementPreflightEndpoint> logger)
    : ElsaEndpoint<RunRuntimeRequirementPreflight, RuntimeRequirementPreflightView>
{
    private static readonly JsonSerializerOptions ProblemJsonOptions = new(JsonSerializerDefaults.Web);

    public override void Configure()
    {
        Post(RouteConstants.GetRoute("preflight"));
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }

    public override async Task HandleAsync(RunRuntimeRequirementPreflight request, CancellationToken cancellationToken)
    {
        try
        {
            await Send.OkAsync(await requestSender.Send(request, cancellationToken), cancellationToken);
        }
        catch (RuntimeRequirementPreflightRequestException exception)
        {
            await WriteProblemAsync(new RuntimePreflightProblemDetails(
                "https://elsa.dev/problems/activity-request-invalid",
                "Runtime requirement preflight request is invalid",
                StatusCodes.Status400BadRequest,
                exception.Message,
                HttpContext.Request.Path,
                ActivityErrorCodes.RequestInvalid,
                HttpContext.TraceIdentifier,
                []), StatusCodes.Status400BadRequest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected Runtime requirement preflight failure");
            await WriteProblemAsync(new RuntimePreflightProblemDetails(
                "https://elsa.dev/problems/activity-operation-failed",
                "Activity operation failed",
                StatusCodes.Status500InternalServerError,
                "The Runtime requirement preflight failed.",
                HttpContext.Request.Path,
                ActivityErrorCodes.OperationFailed,
                HttpContext.TraceIdentifier,
                []), StatusCodes.Status500InternalServerError, cancellationToken);
        }
    }

    private async Task WriteProblemAsync(
        RuntimePreflightProblemDetails problem,
        int statusCode,
        CancellationToken cancellationToken)
    {
        HttpContext.Response.StatusCode = statusCode;
        HttpContext.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(HttpContext.Response.Body, problem, ProblemJsonOptions, cancellationToken);
    }

    private sealed record RuntimePreflightProblemDetails(
        string Type,
        string Title,
        int Status,
        string Detail,
        string Instance,
        string ErrorCode,
        string TraceId,
        IReadOnlyList<object> Diagnostics);
}
