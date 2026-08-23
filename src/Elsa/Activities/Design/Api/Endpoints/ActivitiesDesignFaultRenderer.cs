using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Primitives.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints;

/// <summary>
/// Renders every dispatch failure for both of the owner's published shapes, selected by the
/// endpoint's <see cref="ActivityDesignProblemShapeMetadata"/>.
/// </summary>
/// <remarks>
/// Legacy endpoints keep the historical mediator transport: entity-not-found is 404, argument
/// failures are 400, and anything unexpected is a sanitized 500 whose detail is
/// <c>"Unexpected error occurred."</c> — with the trailing period the pipeline's generic message
/// lacks, which is why the generic fallback cannot serve these routes. Authoring endpoints render
/// <see cref="ActivityProblemDetailsView"/> exactly as the hand-written catch ladders did.
/// </remarks>
internal sealed class ActivitiesDesignFaultRenderer : IEndpointFaultRenderer
{
    private const string AuthoringProblemContentType = "application/json; charset=utf-8";

    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        var shape = context.GetEndpoint()?.Metadata.GetMetadata<ActivityDesignProblemShapeMetadata>();
        if (shape is null)
            return false;

        if (shape.IsLegacy)
        {
            var problem = exception switch
            {
                EntityNotFoundException => ActivitiesDesignLegacyProblem.Create(context, exception.Message, StatusCodes.Status404NotFound),
                ArgumentException => ActivitiesDesignLegacyProblem.Create(context, exception.Message, StatusCodes.Status400BadRequest),
                _ => Unexpected(context, exception)
            };
            await problem.WriteAsync(context);
            return true;
        }

        if (exception is not ActivityAuthoringException authoring)
        {
            LogUnexpected(context, exception);
            await WriteAuthoringAsync(context, ActivityProblemDetails.Unexpected(context));
            return true;
        }

        await WriteAuthoringAsync(context, ActivityProblemDetails.From(authoring, context));
        return true;
    }

    private static ActivitiesDesignLegacyProblem Unexpected(HttpContext context, Exception exception)
    {
        LogUnexpected(context, exception);
        return ActivitiesDesignLegacyProblem.Create(context, "Unexpected error occurred.", StatusCodes.Status500InternalServerError);
    }

    private static Task WriteAuthoringAsync(HttpContext context, ActivityProblemDetailsView problem) =>
        Results.Json(problem, ActivitiesDesignJsonOptions.WireContext.ActivityProblemDetailsView,
                statusCode: problem.Status, contentType: AuthoringProblemContentType)
            .ExecuteAsync(context);

    private static void LogUnexpected(HttpContext context, Exception exception) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Elsa.Activities.Design.Api")
            .LogError(exception, "Unexpected activity design operation failure for {Path}", context.Request.Path);
}
