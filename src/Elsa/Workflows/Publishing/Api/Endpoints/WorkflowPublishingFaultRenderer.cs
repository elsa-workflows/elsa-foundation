using Elsa.Api.AspNetCore;
using Elsa.Activities.Design.Core.Services;
using Elsa.Primitives.Diagnostics;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
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
/// Renders the Publishing owner's structured failure families. Activity publication rejections,
/// runtime preflight problems, and expression validation/value conversion rejections come first,
/// scoped by endpoint metadata so each of those shapes applies exactly where the hand-written
/// handlers applied it. Coded publishing failures (issue #1699) render last and are deliberately
/// unscoped: every Publishing endpoint gets the module's established problem shape with an
/// additive <c>errorCode</c>, whichever endpoint raised the failure.
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

        // Not scoped by endpoint metadata: every Publishing failure below carries a code (issue #1699), so it
        // renders here regardless of which endpoint raised it, ahead of WorkflowPublishingExceptionTranslator,
        // which handles only the codeless arms (404, generic 400).
        if (ClassifyCodedFailure(exception) is { } coded)
        {
            var problem = WorkflowPublishingLegacyProblems.Build(
                context, EndpointProblem.General(coded.Status, exception.Message), coded.Code);
            await WorkflowPublishingLegacyProblems.WriteAsync(context, problem);
            return true;
        }

        return false;
    }

    private static (int Status, string Code)? ClassifyCodedFailure(Exception exception) => exception switch
    {
        PublicationActivationException activation => (StatusCodes.Status409Conflict, activation.Code),
        PublicationPreflightConflictException preflight => (StatusCodes.Status409Conflict, preflight.Code),
        PublicationSnapshotReviewException review => (StatusCodes.Status409Conflict, review.Code),
        PublicationPolicyRevisionConflictException policyRevision => (StatusCodes.Status409Conflict, policyRevision.Code),
        PublicationPolicyResolutionException policy => (
            policy.Code == PublicationFailureCodes.ExpectedPublicationMismatch
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest,
            policy.Code),
        _ => null,
    };

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
