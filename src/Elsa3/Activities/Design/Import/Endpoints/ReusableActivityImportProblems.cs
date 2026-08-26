using Elsa.Api.AspNetCore;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa3.Activities.Design.Import.Endpoints;

/// <summary>Publishes binder failures as the owner's payload-invalid problem document.</summary>
public sealed class ReusableActivityImportProblemWriter : IEndpointProblemWriter
{
    public Task WriteAsync(HttpContext context, EndpointProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var message = problem.Errors.Values.SelectMany(messages => messages).FirstOrDefault()
                      ?? "The Elsa 3 import request could not be read.";
        return ReusableActivityImportHttp.WriteProblemAsync(
            context, new ReusableActivityImportPayloadException(message), context.RequestAborted);
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
