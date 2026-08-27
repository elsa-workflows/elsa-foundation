using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints;

/// <summary>
/// Writes design API failures in the established error shape.
/// </summary>
/// <remarks>
/// The shape is part of the published HTTP contract and is pinned by the module's compatibility
/// baselines, so it is owned here rather than inherited from the shared endpoint layer.
/// </remarks>
internal sealed class WorkflowDesignProblemWriter : IEndpointProblemWriter
{
    private const string ProblemJsonContentType = "application/problem+json; charset=utf-8";

    public Task WriteAsync(HttpContext context, EndpointProblem problem)
    {
        context.Response.StatusCode = problem.StatusCode;
        var error = new WorkflowDesignError(problem.Errors, "One or more errors occurred!", problem.StatusCode);
        var typeInfo = WorkflowsDesignJsonContext.Default.GetTypeInfo(typeof(WorkflowDesignError))
                       ?? throw new InvalidOperationException($"No source-generated JSON metadata exists for '{typeof(WorkflowDesignError).FullName}'.");
        return Results.Json(error, typeInfo, ProblemJsonContentType).ExecuteAsync(context);
    }
}
