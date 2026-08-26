using Elsa.Api.AspNetCore;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa3.Activities.Design.Import.Endpoints;

/// <summary>Publishes binder failures exactly as the hand-written body reader answered them.</summary>
public sealed class ReusableActivityImportProblemWriter : IEndpointProblemWriter
{
    public Task WriteAsync(HttpContext context, EndpointProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var message = problem.Errors.Values.SelectMany(messages => messages).FirstOrDefault();

        // The legacy reader answered an absent payload with a bare 400 and treated an unreadable
        // one as the unexpected failure; both published behaviors survive verbatim. The message is
        // the binder's fixed missing-body sentence, not caller input.
        if (message == "A request body is required.")
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }

        return ReusableActivityImportHttp.WriteProblemAsync(
            context,
            new InvalidDataException(message ?? "The Elsa 3 import request could not be read."),
            context.RequestAborted);
    }
}

/// <summary>
/// Renders every dispatch failure through the owner's problem ladder, exactly as the hand-written
/// handlers' shared catch-all did. Cancellation never reaches this renderer — the pipeline rethrows it.
/// </summary>
public sealed class ReusableActivityImportFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        await ReusableActivityImportHttp.WriteProblemAsync(context, exception, context.RequestAborted);
        return true;
    }
}
