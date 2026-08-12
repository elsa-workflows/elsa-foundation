using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using CShells.Features;
using CShells.Lifecycle;
using Nuplane.Admin;

namespace Elsa.Foundation.Host.ModuleManagement;

/// <summary>
/// Optional extra #1 — the module-management endpoints, all behind a static API-key filter:
/// <list type="bullet">
///   <item><c>POST /_module-management/reconcile</c> — trigger a Nuplane package reconcile from the feed.</item>
///   <item><c>POST /_module-management/reload</c> — refresh the runtime feature catalog + reload active shells.</item>
///   <item><c>GET  /_module-management/assemblies</c> — OBSERVABILITY (read-only): the resident collectible
///   package load contexts and their assemblies + versions (ground truth from the CLR), cross-referenced with
///   Nuplane's active set, flagging contexts still resident but no longer the active version (unload leak
///   candidates). The default/framework load context is counted only, never enumerated, so no host assembly
///   inventory is disclosed.</item>
/// </list>
/// Mapped only when <see cref="ModuleManagementOptions.Enabled"/> is true at startup.
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

        group.MapPost("/reload", async (IRuntimeFeatureCatalog runtimeFeatureCatalog, IShellRegistry registry, CancellationToken ct) =>
        {
            var snapshot = await runtimeFeatureCatalog.RefreshAsync(ct);
            var results = await registry.ReloadActiveAsync(null, ct);
            return Results.Ok(new { features = snapshot.FeatureDescriptors.Count, reloaded = results.Count });
        });

        // ---- OBSERVABILITY (read-only) --------------------------------------------------------------------
        // Ground truth comes from the CLR itself (AssemblyLoadContext.All), NOT from Nuplane's assembly
        // catalog (which reads empty/"stale" outside a load), so it shows what is *actually* resident. The
        // authoritative "should be active" set is Nuplane's active-package catalog (GetPackagesAsync). Nuplane
        // names each collectible context "nuplane:<PackageId>", so a collectible context whose package id is
        // NOT in the active set is a leak candidate — an old version still pinned after it should have unloaded.
        //
        // Only collectible contexts (the Nuplane package contexts — the operator's own feature packages) are
        // detailed. The default/framework context is counted, never enumerated, so this surface never discloses
        // the host's full assembly inventory.
        group.MapGet("/assemblies", async (INuplaneAdminOperations admin, CancellationToken ct) =>
        {
            var snapshot = await admin.GetPackagesAsync(ct);
            var activeIds = new HashSet<string>(snapshot.Packages.Select(p => p.PackageId), StringComparer.OrdinalIgnoreCase);

            var all = AssemblyLoadContext.All.ToArray();
            var collectible = all.Where(alc => alc.IsCollectible).Select(alc =>
            {
                var asms = SafeAssemblies(alc);
                var pkgId = PackageIdOf(alc);
                var isActive = (pkgId is not null && activeIds.Contains(pkgId))
                               || asms.Any(a => activeIds.Contains(a.GetName().Name ?? ""));
                return new
                {
                    name = alc.Name ?? "(unnamed)",
                    packageId = pkgId,
                    activeInNuplane = isActive,
                    assemblies = asms
                        .Select(a => new { name = a.GetName().Name ?? "", version = a.GetName().Version?.ToString() ?? "" })
                        .OrderBy(a => a.name, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            }).ToArray();

            var lingering = collectible.Where(c => !c.activeInNuplane).ToArray();

            return Results.Ok(new
            {
                summary = new
                {
                    loadContexts = all.Length,                        // total contexts (framework context counted, not listed)
                    collectibleContexts = collectible.Length,
                    lingeringCollectibleContexts = lingering.Length,  // resident but NOT an active package = leak candidates
                    activePackages = activeIds.Count
                },
                activePackages = snapshot.Packages
                    .Select(p => $"{p.PackageId} {p.Version}")
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                lingering,                                            // the collectible contexts that should be gone
                collectibleContexts = collectible
            });
        });

        return endpoints;
    }

    // Nuplane names each package load context "nuplane:<PackageId>".
    private static string? PackageIdOf(AssemblyLoadContext alc)
    {
        var n = alc.Name;
        return !string.IsNullOrEmpty(n) && n.StartsWith("nuplane:", StringComparison.OrdinalIgnoreCase)
            ? n["nuplane:".Length..]
            : null;
    }

    // A context mid-unload can throw when enumerated; treat that as "no assemblies" rather than failing the call.
    private static Assembly[] SafeAssemblies(AssemblyLoadContext alc)
    {
        try { return alc.Assemblies.ToArray(); }
        catch { return Array.Empty<Assembly>(); }
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
