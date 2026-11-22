using System.Buffers;
using System.Text.Json;
using Elsa.Serialization.Core;
using Microsoft.AspNetCore.Http;
using MinimalEndpoints;

namespace Elsa.Api.AspNetCore;

/// <summary>Checks owned temporal query parameters before the package's lenient scalar binder runs.</summary>
internal static class ElsaJsonTemporalQueryBoundary
{
    internal static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var parameters = context.GetEndpoint()?.Metadata.GetOrderedMetadata<EndpointParameterMetadata>() ?? [];
        foreach (var parameter in parameters.Where(parameter => parameter.Source == EndpointBindingSource.Query))
        {
            var type = Nullable.GetUnderlyingType(parameter.Type) ?? parameter.Type;
            if (type.IsArray) type = type.GetElementType()!;
            else if (type.IsGenericType && type.GetInterfaces().Append(type).FirstOrDefault(item =>
                item.IsGenericType && item.GetGenericTypeDefinition() == typeof(IEnumerable<>)) is { } enumerable)
                type = enumerable.GetGenericArguments()[0];
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type != typeof(DateTime) && type != typeof(DateTimeOffset)) continue;
            if (!context.Request.Query.TryGetValue(parameter.Name, out var values)) continue;
            foreach (var value in values)
            {
                try
                {
                    var json = JsonSerializer.SerializeToElement(value, JsonBaseline.Options);
                    _ = JsonConversionContract.For(type).Read(json, type);
                }
                catch (JsonException error)
                {
                    var code = error is JsonConversionException refusal ? refusal.Code : "invalid-query";
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json";
                    var buffer = new ArrayBufferWriter<byte>();
                    using (var writer = new Utf8JsonWriter(buffer, new() { Encoder = JsonBaseline.Options.Encoder }))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("code", code);
                        writer.WriteString("path", $"$.{parameter.Name}");
                        writer.WriteString("targetType", type.FullName);
                        writer.WriteString("message", $"Query parameter '{parameter.Name}' requires an ISO 8601 {type.Name} with Z or an explicit offset.");
                        writer.WriteEndObject();
                    }
                    await context.Response.Body.WriteAsync(buffer.WrittenMemory, context.RequestAborted);
                    return;
                }
            }
        }
        await next(context);
    }
}
