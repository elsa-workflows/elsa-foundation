using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

/// <summary>
/// The Runtime owner's published failure shapes, written exactly as the hand-written mapper wrote
/// them. Shared by the owner's fault renderer (dispatch failures) and problem writer (binder failures).
/// </summary>
internal static class RuntimeProblemWriting
{
    private const string Json = "application/json";
    private const string ProblemJson = "application/problem+json";

    /// <summary>The generic runtime problem: RFC shape, no trace id, problem+json with charset.</summary>
    public static async Task ProblemAsync(HttpContext context, int status, string detail)
    {
        var problem = new RuntimeProblemDetails("https://elsa.dev/problems/runtime-request", "Runtime request failed", status, detail, null);
        context.Response.StatusCode = status;
        context.Response.ContentType = $"{ProblemJson}; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, WorkflowsRuntimeJsonContext.Default.RuntimeProblemDetails, context.RequestAborted);
    }

    /// <summary>The FastEndpoints-era validation envelope the binder failures are published in.</summary>
    public static async Task ValidationProblemAsync(HttpContext context, IReadOnlyDictionary<string, string[]> errors)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = $"{ProblemJson}; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, new RuntimeValidationProblemDetails(errors, "One or more errors occurred!", StatusCodes.Status400BadRequest), WorkflowsRuntimeJsonContext.Default.RuntimeValidationProblemDetails, context.RequestAborted);
    }

    /// <summary>The fixed alteration problem tuple, served as plain JSON — deliberately not problem+json.</summary>
    public static async Task AlterationProblemAsync(HttpContext context, string code, string message, int status = StatusCodes.Status400BadRequest)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = $"{Json}; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, new WorkflowAlterationProblemView(code, message, status), WorkflowsRuntimeJsonContext.Default.WorkflowAlterationProblemView, context.RequestAborted);
    }
}
