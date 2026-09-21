using Elsa.Api.AspNetCore;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Handlers.Alterations;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NativeEndpoints;
using System.Globalization;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

/// <summary>
/// Renders every dispatch failure for the Runtime owner's endpoints, reproducing the hand-written
/// mapper's per-endpoint catch ladders: endpoint signals first, then the family the endpoint's
/// shape marker names. Cancellation never reaches this renderer — the pipeline rethrows it.
/// </summary>
internal sealed class WorkflowsRuntimeFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        switch (exception)
        {
            case RuntimeResourceMissingSignal:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return true;
            case RuntimeAlterationRequestRejectedSignal rejected:
                await RuntimeProblemWriting.AlterationProblemAsync(context, rejected.Code, rejected.ProblemMessage);
                return true;
            case ActivityExecutionMissingSignal:
                await ActivityExecutionProblemDetails.NotFoundAsync(context, context.RequestAborted);
                return true;
            case ActivityValuePayloadDeniedSignal:
                await ActivityExecutionProblemDetails.ForbiddenAsync(context, context.RequestAborted);
                return true;
            case ActivityValuePayloadUnavailableSignal:
                await ActivityExecutionProblemDetails.ValueUnavailableAsync(context, context.RequestAborted);
                return true;
        }

        var metadata = context.GetEndpoint()?.Metadata;

        if (metadata?.GetMetadata<ActivityInspectionProblemShapeMetadata>() is { } inspection)
        {
            switch (exception)
            {
                case ActivityExecutionHierarchyCursorException cursor:
                    await ActivityExecutionProblemDetails.CursorAsync(context, cursor, context.RequestAborted);
                    return true;
                case ArgumentException argument:
                    await ActivityExecutionProblemDetails.InvalidRequestAsync(context, argument.Message, context.RequestAborted);
                    return true;
                default:
                    LogUnexpected(context, exception, inspection.Operation);
                    await ActivityExecutionProblemDetails.UnexpectedAsync(context, context.RequestAborted);
                    return true;
            }
        }

        if (metadata?.GetMetadata<AlterationProblemShapeMetadata>() is { } alteration)
        {
            switch (exception)
            {
                // The admission and conflict exceptions derive from InvalidOperationException, so
                // their arms must precede the submission ladder's 422 arm.
                case WorkflowAlterationAdmissionRejectedException admission when alteration.Submit:
                    context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling((admission.RetryAfter ?? TimeSpan.FromSeconds(1)).TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    await RuntimeProblemWriting.AlterationProblemAsync(context, "AlterationAdmissionBackpressure", "Runtime alteration admission is temporarily at capacity.", StatusCodes.Status429TooManyRequests);
                    return true;
                case WorkflowAlterationIdempotencyConflictException when alteration.Submit:
                    await RuntimeProblemWriting.AlterationProblemAsync(context, "AlterationIdempotencyConflict", "The idempotency key is already associated with a different alteration request.", StatusCodes.Status409Conflict);
                    return true;
                case WorkflowAlterationResourceNotFoundException when !alteration.Submit:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return true;
                case EntityNotFoundException when alteration.EntityNotFoundArm:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return true;
                case ArgumentOutOfRangeException when alteration.Submit:
                    await RuntimeProblemWriting.AlterationProblemAsync(context, "InvalidIdempotencyKey", "The alteration request is invalid.");
                    return true;
                case InvalidOperationException when alteration.Submit:
                case ArgumentException when alteration.Submit:
                    await RuntimeProblemWriting.AlterationProblemAsync(context, "InvalidAlterationRequest", "The alteration request is invalid.", StatusCodes.Status422UnprocessableEntity);
                    return true;
                case ArgumentException:
                    await RuntimeProblemWriting.AlterationProblemAsync(context, alteration.ArgumentCode, alteration.ArgumentMessage);
                    return true;
                default:
                    LogUnexpected(context, exception, alteration.Operation);
                    await RuntimeProblemWriting.AlterationProblemAsync(context, "UnexpectedError", "Unexpected error occurred.", StatusCodes.Status500InternalServerError);
                    return true;
            }
        }

        if (metadata?.GetMetadata<RuntimeProblemShapeMetadata>() is { } runtime)
        {
            switch (exception)
            {
                case WorkflowExecutableNotFoundException notFound when runtime.ExecutableArms:
                    await RuntimeProblemWriting.ProblemAsync(context, StatusCodes.Status400BadRequest, notFound.Message);
                    return true;
                case WorkflowExecutableReferenceRejectedException rejected when runtime.ExecutableArms:
                    await RuntimeProblemWriting.ProblemAsync(context, StatusCodes.Status409Conflict, rejected.Message);
                    return true;
                case WorkflowAlterationResourceNotFoundException when runtime.NotFoundArms:
                case EntityNotFoundException when runtime.NotFoundArms:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return true;
                case ArgumentException argument:
                    await RuntimeProblemWriting.ProblemAsync(context, StatusCodes.Status400BadRequest, runtime.ArgumentDetail ?? argument.Message);
                    return true;
                default:
                    LogUnexpected(context, exception, runtime.Operation);
                    await RuntimeProblemWriting.ProblemAsync(context, StatusCodes.Status500InternalServerError, "Unexpected error occurred.");
                    return true;
            }
        }

        return false;
    }

    private static void LogUnexpected(HttpContext context, Exception exception, string operation) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(WorkflowsRuntimeApi.OwnerId)
            .LogError(exception, "Unexpected error while {Operation}.", operation);
}
