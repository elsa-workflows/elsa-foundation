using Elsa.Api.AspNetCore;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

/// <summary>
/// Renders failures for the shared Runtime HTTP endpoints, reproducing the hand-written
/// mapper's per-endpoint catch ladders: endpoint signals first, then the family the endpoint's
/// shape marker names. Cancellation never reaches this renderer — the pipeline rethrows it. Every
/// shape answers a schema write refusal with a 409 carrying its code.
/// </summary>
internal sealed class RuntimeHttpFaultRenderer : IEndpointFaultRenderer
{
    public ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception) => WriteAsync(context, exception);

    internal static async ValueTask<bool> WriteAsync(HttpContext context, Exception exception,
        Func<HttpContext, Exception, ValueTask<bool>>? writeAdditionalProblem = null)
    {
        switch (exception)
        {
            case Elsa.Workflows.Runtime.Core.UserTasks.UserTaskException task:
                var status = task.Code switch { "unauthorized" => 401, "forbidden" => 403, "missing" => 404, "conflict" => 409, "validation" => 400, _ => 503 };
                context.Response.StatusCode = status;
                await context.Response.WriteAsJsonAsync(new UserTasks.UserTaskProblem(task.Code, task.Message, task.Errors is null ? null : System.Text.Json.JsonSerializer.SerializeToElement(task.Errors)), UserTasks.UserTaskJsonContext.Default.UserTaskProblem, cancellationToken: context.RequestAborted);
                return true;

            case RuntimeRunDeletionRejectedSignal deletion:
                await RuntimeProblemWriting.ProblemAsync(context, deletion.Status, deletion.Detail);
                return true;
            case RuntimeResourceMissingSignal:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
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
            if (exception is SchemaWriteRefusedException refusal)
            {
                await ActivityExecutionProblemDetails.SchemaWriteRefusedAsync(context, refusal, context.RequestAborted);
                return true;
            }
            if (inspection.History)
            {
                var problem = exception switch
                {
                    RuntimeHistoryUnsupportedSignal => (501, "runtime.history.unsupported", "Run history unsupported", "This host cannot provide the requested history detail."),
                    RuntimeHistoryIntegrityException => (409, "runtime.history.invalid", "Run history invalid", "History integrity could not be established."),
                    RuntimeHistoryUnavailableException => (503, "runtime.history.unavailable", "Run history unavailable", "Run history is temporarily unavailable. Try again."),
                    ArgumentException => (400, "runtime.history.request-invalid", "Invalid run history request", "The history query or continuation is invalid."),
                    _ => (500, "runtime.history.failed", "Run history inspection failed", "The run history inspection operation failed.")
                };
                if (problem.Item1 == 500) LogUnexpected(context, exception, inspection.Operation);
                await ActivityExecutionProblemDetails.HistoryAsync(context, problem.Item1, problem.Item2, problem.Item3, problem.Item4);
                return true;
            }
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

        if (writeAdditionalProblem is not null && await writeAdditionalProblem(context, exception))
            return true;

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
                case WorkflowExecutableStartRejectedException { Category: WorkflowExecutableStartRejectionCategory.Input } refused when runtime.ExecutableArms:
                    await RuntimeProblemWriting.ProblemAsync(context, StatusCodes.Status400BadRequest, refused.Message);
                    return true;
                case EntityNotFoundException when runtime.NotFoundArms:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return true;
                case ArgumentException argument:
                    await RuntimeProblemWriting.ProblemAsync(context, StatusCodes.Status400BadRequest, runtime.ArgumentDetail ?? argument.Message);
                    return true;
                case SchemaWriteRefusedException refusal:
                    await RuntimeProblemWriting.SchemaWriteRefusedAsync(context, refusal);
                    return true;
                default:
                    LogUnexpected(context, exception, runtime.Operation);
                    await RuntimeProblemWriting.ProblemAsync(context, StatusCodes.Status500InternalServerError, "Unexpected error occurred.");
                    return true;
            }
        }

        return false;
    }

    internal static void LogUnexpected(HttpContext context, Exception exception, string operation) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(RuntimeHttpApi.OwnerId)
            .LogError(exception, "Unexpected error while {Operation}.", operation);
}
