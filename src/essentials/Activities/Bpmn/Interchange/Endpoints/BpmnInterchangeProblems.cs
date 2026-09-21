using Elsa.Api.AspNetCore;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;
using System.Text.Json;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints;

/// <summary>The owner's published legacy error envelope, written exactly as the mapper wrote it.</summary>
internal static class BpmnInterchangeProblemWriting
{
    public static Task WriteLegacyErrorAsync(HttpContext context, string message, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        var error = new BpmnInterchangeError(
            new Dictionary<string, string[]> { ["generalErrors"] = [message] },
            "One or more errors occurred!",
            statusCode);
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

/// <summary>Maps interchange failures to the owner's legacy 400, exactly as the catch ladders did.</summary>
internal sealed class BpmnInterchangeFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        if (exception is not BpmnInterchangeException interchange)
            return false;

        await BpmnInterchangeProblemWriting.WriteLegacyErrorAsync(context, interchange.Message, StatusCodes.Status400BadRequest);
        return true;
    }
}
