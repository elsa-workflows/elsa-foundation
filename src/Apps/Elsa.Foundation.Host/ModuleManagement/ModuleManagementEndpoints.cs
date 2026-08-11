using System.Security.Cryptography;
using System.Text;
using CShells.Lifecycle;
using Nuplane.Admin;

namespace Elsa.Foundation.Host.ModuleManagement;

/// <summary>
/// Optional extra #1 — the module-management endpoints. Two operations, both behind a static API-key filter:
/// <list type="bullet">
///   <item><c>POST /_module-management/reconcile</c> — trigger a Nuplane package reconcile (refresh the
///   package/assembly catalog from the feed). Same effect as the directory folder listener, on demand.</item>
///   <item><c>POST /_module-management/reload</c> — reload the active CShells shells (and their per-shell
///   middleware) for the current assembly set. Use after a reconcile to make running shells pick up new
///   features without a restart.</item>
/// </list>
/// This is mapped only when <see cref="ModuleManagementOptions.Enabled"/> is true at startup.
/// </summary>
internal static class ModuleManagementEndpoints
{
    public static IEndpointRouteBuilder MapModuleManagementApi(this IEndpointRouteBuilder endpoints, ModuleManagementOptions options)
    {
        var group = endpoints.MapGroup("/_module-management");
        group.AddEndpointFilter(async (context, next) =>
            Authorized(context.HttpContext, options) ? await next(context) : Results.Unauthorized());

        group.MapPost("/reconcile", async (INuplaneAdminOperations admin, CancellationToken ct) =>
            Results.Ok(await admin.TriggerReconcileAsync(ct)));

        group.MapPost("/reload", async (IShellRegistry registry, CancellationToken ct) =>
        {
            var results = await registry.ReloadActiveAsync(null, ct);
            return Results.Ok(new { reloaded = results.Count });
        });

        return endpoints;
    }

    private static bool Authorized(HttpContext context, ModuleManagementOptions options)
    {
        // A blank configured key makes the surface unreachable even though it was mapped.
        if (string.IsNullOrEmpty(options.ApiKey))
            return false;
        if (!context.Request.Headers.TryGetValue(ModuleManagementOptions.ApiKeyHeader, out var provided))
            return false;

        // Constant-time comparison; FixedTimeEquals returns false for length mismatches without leaking timing.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided.ToString()),
            Encoding.UTF8.GetBytes(options.ApiKey));
    }
}
