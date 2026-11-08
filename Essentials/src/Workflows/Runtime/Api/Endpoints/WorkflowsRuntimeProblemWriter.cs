using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

/// <summary>
/// Writes binder-produced problems in the Runtime owner's published shapes: body serializer
/// failures keep the FastEndpoints-era validation envelope, and anything else falls back to the
/// generic runtime problem. Dispatch failures never reach this writer — the fault renderer owns them.
/// </summary>
internal sealed class WorkflowsRuntimeProblemWriter : IEndpointProblemWriter
{
    public Task WriteAsync(HttpContext context, EndpointProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        if (problem.StatusCode == StatusCodes.Status400BadRequest && problem.Errors.ContainsKey("serializerErrors"))
            return RuntimeProblemWriting.ValidationProblemAsync(context, problem.Errors);

        var detail = problem.Errors.Values.SelectMany(messages => messages).FirstOrDefault() ?? "Unexpected error occurred.";
        return RuntimeProblemWriting.ProblemAsync(context, problem.StatusCode, detail);
    }
}
