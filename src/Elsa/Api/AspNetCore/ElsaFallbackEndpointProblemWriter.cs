using Microsoft.AspNetCore.Http;
using NativeEndpoints;
using System.Buffers;
using System.Text.Json;

namespace Elsa.Api.AspNetCore;

/// <summary>
/// The last-resort problem shape for Elsa owners that publish no error contract of their own.
/// </summary>
/// <remarks>
/// Registered unkeyed by <see cref="ElsaEndpointsServiceCollectionExtensions.AddElsaEndpoints"/>,
/// ahead of the package's own default. That ordering is the point: the package falls back to an
/// RFC 9457 <c>ProblemDetails</c> document, while the shape below — a status and a keyed set of
/// messages — is what most of Elsa's owners already publish on a binding failure. Only seven owners
/// register a writer keyed by owner id; leaving the package's fallback in place would silently
/// change the failure bodies of every other one.
/// <para>
/// Written by hand so no serializer cache is involved: a failure path that needs a serializer
/// context is a failure path that can fail.
/// </para>
/// </remarks>
public sealed class ElsaFallbackEndpointProblemWriter : IEndpointProblemWriter
{
    /// <inheritdoc />
    public async Task WriteAsync(HttpContext context, EndpointProblem problem)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(problem);

        context.Response.StatusCode = problem.StatusCode;
        context.Response.ContentType = "application/problem+json";
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("status", problem.StatusCode);
            writer.WriteStartObject("errors");
            foreach (var (key, messages) in problem.Errors)
            {
                writer.WriteStartArray(key);
                foreach (var message in messages)
                    writer.WriteStringValue(message);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        await context.Response.Body.WriteAsync(buffer.WrittenMemory, context.RequestAborted);
    }
}
