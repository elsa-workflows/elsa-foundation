using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

/// <summary>The published alteration failure tuple.</summary>
internal static class AlterationProblemWriting
{
    /// <summary>The fixed alteration problem tuple, served as plain JSON — deliberately not problem+json.</summary>
    public static async Task AlterationProblemAsync(HttpContext context, string code, string message, int status = StatusCodes.Status400BadRequest)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, new WorkflowAlterationProblemView(code, message, status), WorkflowsRuntimeJsonContext.Default.WorkflowAlterationProblemView, context.RequestAborted);
    }
}
