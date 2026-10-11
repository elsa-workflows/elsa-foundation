namespace Elsa.Studio.Web;

/// <summary>
/// Module entry points (<c>module.js</c>, <c>module*.css</c>) and the shell's own bundles have stable, unhashed URLs,
/// while the chunks they import are content-hashed and deleted on every rebuild. Without a <c>Cache-Control</c>
/// header the browser reuses a heuristically cached entry point — even across a normal reload — and imports chunks
/// that no longer exist. Every static asset Studio serves — host files, the SPA document and Nuplane package assets —
/// is therefore marked <c>no-cache</c>: still cached, but revalidated (a cheap 304) before each reuse.
/// </summary>
internal static class StudioAssetCaching
{
    private static readonly StaticFileOptions RevalidatingStaticFiles = new()
    {
        OnPrepareResponse = context => ApplyRevalidation(context.Context.Response)
    };

    public static void ApplyRevalidation(HttpResponse response) => response.Headers.CacheControl = "no-cache";

    public static IApplicationBuilder UseStudioStaticFiles(this IApplicationBuilder app) => app.UseStaticFiles(RevalidatingStaticFiles);

    public static IEndpointRouteBuilder MapStudioFallback(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFallbackToFile("studio/index.html", RevalidatingStaticFiles);
        return endpoints;
    }
}
