using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Elsa.Api.AspNetCore;

/// <summary>Installed before authorization so every response of a marked Elsa JSON API operation gets its wire headers.</summary>
internal sealed class ElsaJsonResponseHeadersStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, continuation) =>
        {
            context.Response.OnStarting(() =>
            {
                if (context.GetEndpoint()?.Metadata.GetMetadata<ElsaJsonResponseHeadersMetadata>() is { } policy)
                {
                    context.Response.Headers.XContentTypeOptions = "nosniff";
                    var contentType = context.Response.ContentType;
                    if (policy.NormalizeContentType && contentType is not null)
                    {
                        var separator = contentType.IndexOf(';');
                        var mediaType = (separator < 0 ? contentType : contentType[..separator]).Trim();
                        if (mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
                            context.Response.ContentType = "application/json" + (separator < 0 ? "" : contentType[separator..]);
                    }
                }
                return Task.CompletedTask;
            });
            return continuation();
        });
        next(app);
    };
}

internal sealed class ElsaJsonResponseHeadersMetadata(bool normalizeContentType)
{
    internal static readonly ElsaJsonResponseHeadersMetadata Instance = new(true);
    internal static readonly ElsaJsonResponseHeadersMetadata PreserveContentType = new(false);
    internal bool NormalizeContentType { get; } = normalizeContentType;
}
