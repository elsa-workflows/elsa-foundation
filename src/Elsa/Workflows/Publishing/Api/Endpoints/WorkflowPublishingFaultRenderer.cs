using Elsa.Api.AspNetCore;
using Elsa.Activities.Design.Core.Services;
using Elsa.Primitives.Diagnostics;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Publishing.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>Marks an endpoint whose failures render in the activity-publishing problem shape.</summary>
/// <remarks>
/// A rejection carries the endpoint's own title; every other failure renders as the shape's
/// sanitized unexpected problem, exactly as the hand-written catch ladders did.
/// </remarks>
internal sealed class ActivityProblemEndpointMetadata(string rejectionTitle)
{
    public string RejectionTitle { get; } = rejectionTitle;
}

/// <summary>Marks the runtime-requirement preflight endpoint, whose failures use its own problem shape.</summary>
internal sealed class RuntimePreflightProblemEndpointMetadata;

/// <summary>
/// Marks the workflow publication endpoints on which conversion — and, for publish, expression
/// validation — failures render in their structured problem shapes.
/// </summary>
internal sealed class WorkflowPublicationProblemEndpointMetadata(bool expressionValidation)
{
    public bool ExpressionValidation { get; } = expressionValidation;
}

/// <summary>
/// Renders the Publishing owner's structured failure families — activity publication rejections,
/// runtime preflight problems, expression validation and value conversion rejections — scoped by
/// endpoint metadata so each shape applies exactly where the hand-written handlers applied it.
/// </summary>
internal sealed class WorkflowPublishingFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        var metadata = context.GetEndpoint()?.Metadata;
        if (metadata?.GetMetadata<ActivityProblemEndpointMetadata>() is { } activity)
        {
            if (exception is ActivityPublicationRejectedException rejected)
            {
                await ActivityPublishingProblems.WriteAsync(context.Response,
                    ActivityPublishingProblems.Rejected(rejected, context, activity.RejectionTitle), context.RequestAborted);
                return true;
            }

            LogUnexpected(context, exception);
            await ActivityPublishingProblems.WriteAsync(context.Response,
                ActivityPublishingProblems.Unexpected(context), context.RequestAborted);
            return true;
        }

        if (metadata?.GetMetadata<RuntimePreflightProblemEndpointMetadata>() is not null)
        {
            if (exception is RuntimeRequirementPreflightRequestException invalid)
            {
                await WriteRuntimePreflightAsync(context, new RuntimePreflightProblemDetails(
                    "https://elsa.dev/problems/activity-request-invalid", "Runtime requirement preflight request is invalid",
                    StatusCodes.Status400BadRequest, invalid.Message, context.Request.Path, ActivityErrorCodes.RequestInvalid,
                    context.TraceIdentifier, []));
                return true;
            }

            LogUnexpected(context, exception);
            await WriteRuntimePreflightAsync(context, new RuntimePreflightProblemDetails(
                "https://elsa.dev/problems/activity-operation-failed", "Activity operation failed",
                StatusCodes.Status500InternalServerError, "The Runtime requirement preflight failed.", context.Request.Path,
                ActivityErrorCodes.OperationFailed, context.TraceIdentifier, []));
            return true;
        }

        if (metadata?.GetMetadata<WorkflowPublicationProblemEndpointMetadata>() is { } publication)
        {
            if (publication.ExpressionValidation && exception is ExpressionPublicationValidationException expression)
            {
                await ExpressionPublicationValidationProblems.WriteAsync(context.Response,
                    ExpressionPublicationValidationProblems.Create(expression, context), context.RequestAborted);
                return true;
            }

            if (ValueConversionPublicationProblems.TryFind(exception, out var conversion))
            {
                var versionId = context.Request.RouteValues.TryGetValue("versionId", out var value) ? value?.ToString() : null;
                await ValueConversionPublicationProblems.WriteAsync(context.Response,
                    ValueConversionPublicationProblems.Create(conversion, context, versionId), context.RequestAborted);
                return true;
            }
        }

        return false;
    }

    private static async Task WriteRuntimePreflightAsync(HttpContext context, RuntimePreflightProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status;
        context.Response.ContentType = "application/problem+json";
        await System.Text.Json.JsonSerializer.SerializeAsync(
            context.Response.Body,
            problem,
            WorkflowsPublishingJsonContext.Default.RuntimePreflightProblemDetails,
            context.RequestAborted);
    }

    private static void LogUnexpected(HttpContext context, Exception exception) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Elsa.Workflows.Publishing.Api")
            .LogError(exception, "Unexpected Publishing operation failure for {Path}", context.Request.Path);
}
