using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Admin.Web.Extensions;

public static class AdminWebApplicationExtensions
{
    public static IEndpointRouteBuilder MapElsaAdminWeb(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFallbackToFile("/admin", "/_content/Elsa.Admin.Web/admin/index.html");
        endpoints.MapFallbackToFile("/admin/{*path:nonfile}", "/_content/Elsa.Admin.Web/admin/index.html");
        return endpoints;
    }
}
