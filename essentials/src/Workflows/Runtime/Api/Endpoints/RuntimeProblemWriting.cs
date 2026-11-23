using Elsa.Api.AspNetCore;
using Elsa.Primitives.Exceptions;
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
    public static Task ProblemAsync(HttpContext context, int status, string detail) =>
        ProblemAsync(context, new RuntimeProblemDetails("https://elsa.dev/problems/runtime-request", "Runtime request failed", status, detail, null));

    /// <summary>The generic runtime problem with a machine-readable code (for example a refused comparison's reason).</summary>
    public static Task ProblemAsync(HttpContext context, int status, string code, string detail) =>
        ProblemAsync(context, new RuntimeProblemDetails("https://elsa.dev/problems/runtime-request", "Runtime request failed", status, detail, null, Code: code));

    public static Task InputProblemAsync(HttpContext context, string code, string detail, string? path) =>
        ProblemAsync(context, new RuntimeProblemDetails("https://elsa.dev/problems/runtime-request", "Runtime request failed", StatusCodes.Status400BadRequest, detail, null,
            path is null ? null : new Dictionary<string, string[]> { [path] = [detail] }, code));

    /// <summary>
    /// A schema write refusal in the generic runtime problem: a 409 carrying the refusal's code, and
    /// its entries, the family and both versions among them, as errors.
    /// </summary>
    public static Task SchemaWriteRefusedAsync(HttpContext context, SchemaWriteRefusedException refusal)
    {
        var problem = SchemaWriteRefusalProblem.For(refusal);
        return ProblemAsync(context, new RuntimeProblemDetails(
            "https://elsa.dev/problems/runtime-request", "Runtime request failed", problem.StatusCode, refusal.Message, null, problem.Errors, refusal.Code));
    }

    private static async Task ProblemAsync(HttpContext context, RuntimeProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status;
        context.Response.ContentType = $"{ProblemJson}; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, WorkflowsRuntimeJsonContext.WireContext.RuntimeProblemDetails, context.RequestAborted);
    }

    /// <summary>The FastEndpoints-era validation envelope the binder failures are published in.</summary>
    public static async Task ValidationProblemAsync(HttpContext context, IReadOnlyDictionary<string, string[]> errors)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = $"{ProblemJson}; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, new RuntimeValidationProblemDetails(errors, "One or more errors occurred!", StatusCodes.Status400BadRequest), WorkflowsRuntimeJsonContext.WireContext.RuntimeValidationProblemDetails, context.RequestAborted);
    }

    /// <summary>The fixed alteration problem tuple, served as plain JSON — deliberately not problem+json.</summary>
    public static async Task AlterationProblemAsync(HttpContext context, string code, string message, int status = StatusCodes.Status400BadRequest)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = $"{Json}; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, new WorkflowAlterationProblemView(code, message, status), WorkflowsRuntimeJsonContext.WireContext.WorkflowAlterationProblemView, context.RequestAborted);
    }
}
