using System.Text;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Http.Services;

/// <summary>Projects a bounded live HTTP request into the durable request model shared by production and test ingress.</summary>
internal static class HttpEndpointRequestModelReader
{
    /// <summary>Reads the request body up to the byte cap; returns null before dispatch when the cap is exceeded.</summary>
    public static async ValueTask<HttpRequestModel?> ReadAsync(
        HttpContext context,
        string endpointPath,
        IReadOnlyDictionary<string, string> routeValues,
        long maxBodyBytes,
        IReadOnlyCollection<string>? excludedHeaders = null)
    {
        if (maxBodyBytes < 0 || context.Request.ContentLength is { } declaredLength && declaredLength > maxBodyBytes)
            return null;

        string? body = null;
        if (context.Request.Body.CanRead)
        {
            body = await ReadBodyBoundedAsync(context.Request.Body, maxBodyBytes, context.RequestAborted);
            if (body is null)
                return null; // Content-Length may be absent or inaccurate.
            if (body.Length == 0)
                body = null;
        }

        var headers = context.Request.Headers
            .Where(header => excludedHeaders is null || !excludedHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(header => header.Key, header => header.Value.Select(value => value ?? string.Empty).ToArray(), StringComparer.OrdinalIgnoreCase);
        var query = context.Request.Query
            .ToDictionary(item => item.Key, item => item.Value.Select(value => value ?? string.Empty).ToArray(), StringComparer.OrdinalIgnoreCase);

        // Parsed content is derived later by HttpEndpoint from Body + Content-Type, so only the raw body travels.
        return new HttpRequestModel(
            Path: HttpEndpointStimulus.NormalizeTemplate(endpointPath),
            Method: context.Request.Method.ToUpperInvariant(),
            Headers: headers,
            Query: query,
            Body: body,
            RouteData: new Dictionary<string, string>(routeValues, StringComparer.OrdinalIgnoreCase))
        { ResponseExchangeId = Guid.NewGuid().ToString("N") };
    }

    private static async ValueTask<string?> ReadBodyBoundedAsync(Stream body, long maxBodyBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBodyBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
