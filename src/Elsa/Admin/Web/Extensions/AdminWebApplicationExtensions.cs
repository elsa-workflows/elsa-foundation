using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Admin.Web.Extensions;

public static class AdminWebApplicationExtensions
{
    private const string AdminIndexFile = "_content/Elsa.Admin.Web/admin/index.html";

    public static IEndpointRouteBuilder MapElsaAdminWeb(this IEndpointRouteBuilder endpoints, string pathPrefix = "")
    {
        var normalizedPrefix = NormalizePathPrefix(pathPrefix);

        if (normalizedPrefix.Length == 0)
        {
            endpoints.MapFallbackToFile(AdminIndexFile);
            return endpoints;
        }

        endpoints.MapFallbackToFile(normalizedPrefix, AdminIndexFile);
        endpoints.MapFallbackToFile($"{normalizedPrefix}/{{*path:nonfile}}", AdminIndexFile);
        return endpoints;
    }

    private static string NormalizePathPrefix(string pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix) || pathPrefix == "/")
            return "";

        var trimmed = pathPrefix.TrimEnd('/');
        return trimmed.StartsWith('/') ? trimmed : $"/{trimmed}";
    }
}
