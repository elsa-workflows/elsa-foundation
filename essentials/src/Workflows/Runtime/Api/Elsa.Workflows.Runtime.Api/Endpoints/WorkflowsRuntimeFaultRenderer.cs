using System.Globalization;
using Elsa.Api.AspNetCore;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Handlers.Alterations;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.AspNetCore.Http;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

/// <summary>Supplies the classic user-task and alteration failures alongside the shared Runtime HTTP response shapes.</summary>
internal sealed class WorkflowsRuntimeFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        switch (exception)
        {
            case RuntimeAlterationRequestRejectedSignal rejected:
                await AlterationProblemWriting.AlterationProblemAsync(context, rejected.Code, rejected.ProblemMessage);
                return true;
        }

        return await RuntimeHttpFaultRenderer.WriteAsync(context, exception, WriteAlterationProblemAsync);
    }

    private static async ValueTask<bool> WriteAlterationProblemAsync(HttpContext context, Exception exception)
    {
        var metadata = context.GetEndpoint()?.Metadata;
        if (metadata?.GetMetadata<AlterationProblemShapeMetadata>() is { } alteration)
        {
            switch (exception)
            {
                // The admission and conflict exceptions derive from InvalidOperationException, so
                // their arms must precede the submission ladder's 422 arm.
                case WorkflowAlterationAdmissionRejectedException admission when alteration.Submit:
                    context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling((admission.RetryAfter ?? TimeSpan.FromSeconds(1)).TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    await AlterationProblemWriting.AlterationProblemAsync(context, "AlterationAdmissionBackpressure", "Runtime alteration admission is temporarily at capacity.", StatusCodes.Status429TooManyRequests);
                    return true;
                case WorkflowAlterationIdempotencyConflictException when alteration.Submit:
                    await AlterationProblemWriting.AlterationProblemAsync(context, "AlterationIdempotencyConflict", "The idempotency key is already associated with a different alteration request.", StatusCodes.Status409Conflict);
                    return true;
                case WorkflowAlterationResourceNotFoundException when !alteration.Submit:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return true;
                case EntityNotFoundException when alteration.EntityNotFoundArm:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return true;
                case ArgumentOutOfRangeException when alteration.Submit:
                    await AlterationProblemWriting.AlterationProblemAsync(context, "InvalidIdempotencyKey", "The alteration request is invalid.");
                    return true;
                case InvalidOperationException when alteration.Submit:
                case ArgumentException when alteration.Submit:
                    await AlterationProblemWriting.AlterationProblemAsync(context, "InvalidAlterationRequest", "The alteration request is invalid.", StatusCodes.Status422UnprocessableEntity);
                    return true;
                case ArgumentException:
                    await AlterationProblemWriting.AlterationProblemAsync(context, alteration.ArgumentCode, alteration.ArgumentMessage);
                    return true;
                case SchemaWriteRefusedException refusal:
                    await AlterationProblemWriting.AlterationProblemAsync(context, refusal.Code, refusal.Message, SchemaWriteRefusalProblem.StatusCode);
                    return true;
                default:
                    RuntimeHttpFaultRenderer.LogUnexpected(context, exception, alteration.Operation);
                    await AlterationProblemWriting.AlterationProblemAsync(context, "UnexpectedError", "Unexpected error occurred.", StatusCodes.Status500InternalServerError);
                    return true;
            }
        }

        if (exception is WorkflowAlterationResourceNotFoundException &&
            metadata?.GetMetadata<RuntimeProblemShapeMetadata>() is { NotFoundArms: true })
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return true;
        }

        return false;
    }
}
