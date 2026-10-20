using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Etl.Design.Api.Models;
using Elsa.Primitives.Exceptions;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api;

internal sealed class EtlProblemWriter : IEndpointProblemWriter
{
    public Task WriteAsync(HttpContext context, EndpointProblem problem) => WriteAsync(context, problem.StatusCode, "etl.request-invalid");
    internal static Task WriteAsync(HttpContext context, int status, string code) =>
        Results.Json(new EtlApiProblem(status, code), EtlDesignJsonContext.WireContext.EtlApiProblem, statusCode: status).ExecuteAsync(context);
}

internal sealed class EtlFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        if (exception is SchemaWriteRefusedException refusal)
        {
            // Preserve the framework's actionable schema/dormancy metadata and stable refusal code.
            await new ElsaFallbackEndpointProblemWriter().WriteAsync(context, SchemaWriteRefusalProblem.For(refusal));
            return true;
        }
        var (status, code) = exception switch
        {
            EtlApiException known => (known.Status, known.Code),
            ArgumentException or JsonException => (400, "etl.request-invalid"),
            _ => (500, "etl.operation-failed")
        };
        await EtlProblemWriter.WriteAsync(context, status, code);
        return true;
    }
}
