using Elsa.Api.AspNetCore;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Primitives.Exceptions;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;
using System.Text.Json;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints;

/// <summary>The owner's published legacy error envelope, written exactly as the mapper wrote it.</summary>
internal static class BpmnInterchangeProblemWriting
{
    public static Task WriteLegacyErrorAsync(HttpContext context, string message, int statusCode) =>
        WriteLegacyErrorAsync(context, EndpointProblem.General(statusCode, message));

    public static Task WriteLegacyErrorAsync(HttpContext context, EndpointProblem problem)
    {
        context.Response.StatusCode = problem.StatusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        var error = new BpmnInterchangeError(
            new Dictionary<string, string[]>(problem.Errors),
            "One or more errors occurred!",
            problem.StatusCode);
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(error, BpmnInterchangeJsonContext.Default.BpmnInterchangeError),
            context.RequestAborted);
    }
}

/// <summary>Publishes binder failures in the owner's legacy general-errors envelope.</summary>
internal sealed class BpmnInterchangeProblemWriter : IEndpointProblemWriter
{
    public Task WriteAsync(HttpContext context, EndpointProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var message = problem.Errors.Values.SelectMany(messages => messages).FirstOrDefault() ?? "Unexpected error occurred";
        return BpmnInterchangeProblemWriting.WriteLegacyErrorAsync(context, message, problem.StatusCode);
    }
}

/// <summary>
/// Maps interchange failures to the owner's legacy 400, exactly as the catch ladders did, and a schema write refusal to
/// a 409 in the same envelope (spec 180, FR-016a). The refusal is answered here rather than left to translation because
/// <see cref="BpmnInterchangeProblemWriter"/> keeps only a problem's first message, which would drop its code.
/// </summary>
internal sealed class BpmnInterchangeFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        switch (exception)
        {
            case BpmnInterchangeException interchange:
                await BpmnInterchangeProblemWriting.WriteLegacyErrorAsync(context, interchange.Message, StatusCodes.Status400BadRequest);
                return true;
            case SchemaWriteRefusedException refusal:
                await BpmnInterchangeProblemWriting.WriteLegacyErrorAsync(context, SchemaWriteRefusalProblem.For(refusal));
                return true;
            default:
                return false;
        }
    }
}
