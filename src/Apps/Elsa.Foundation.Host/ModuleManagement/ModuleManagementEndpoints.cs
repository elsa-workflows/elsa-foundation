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
///   <item><c>GET  /_module-management/assemblies</c> — OBSERVABILITY: every live assembly load context and its
///   assemblies + versions (ground truth from the CLR), cross-referenced with Nuplane's active set, flagging
///   collectible contexts that are still resident but are no longer the active version (unload leak candidates).</item>
///   <item><c>POST /_module-management/collect</c> — CONTROL: force GC + finalizers (repeatable) to complete any
///   outstanding collectible-context unloads, and report which unloaded vs. are still pinned.</item>
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

        // ---- OBSERVABILITY --------------------------------------------------------------------------------
        // Ground truth comes from the CLR itself (AssemblyLoadContext.All), NOT from Nuplane's assembly
        // catalog (which reads empty/"stale" outside a load), so it shows what is *actually* resident. The
        // authoritative "should be active" set is Nuplane's active-package catalog (GetPackagesAsync). Nuplane
        // names each collectible context "nuplane:<PackageId>", so a collectible context whose package id is
        // NOT in the active set is a leak candidate — an old version still pinned after it should have unloaded.
        group.MapGet("/assemblies", async (INuplaneAdminOperations admin, CancellationToken ct) =>
        {
            var snapshot = await admin.GetPackagesAsync(ct);
            var activeIds = new HashSet<string>(snapshot.Packages.Select(p => p.PackageId), StringComparer.OrdinalIgnoreCase);

            var contexts = AssemblyLoadContext.All.Select(alc =>
            {
                var asms = SafeAssemblies(alc);
                var pkgId = PackageIdOf(alc);
                var isActive = (pkgId is not null && activeIds.Contains(pkgId))
                               || asms.Any(a => activeIds.Contains(a.GetName().Name ?? ""));
                return new
                {
                    name = alc.Name ?? "(default)",
                    isCollectible = alc.IsCollectible,
                    packageId = pkgId,
                    activeInNuplane = isActive,
                    assemblyCount = asms.Length,
                    assemblies = asms
                        .Select(a => new { name = a.GetName().Name ?? "", version = a.GetName().Version?.ToString() ?? "" })
                        .OrderBy(a => a.name, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            }).ToArray();

            var collectible = contexts.Where(c => c.isCollectible).ToArray();
            var lingering = collectible.Where(c => !c.activeInNuplane).ToArray();

            return Results.Ok(new
            {
                summary = new
                {
                    loadContexts = contexts.Length,
                    collectibleContexts = collectible.Length,
                    lingeringCollectibleContexts = lingering.Length, // resident but NOT an active package = leak candidates
                    activePackages = activeIds.Count
                },
                activePackages = snapshot.Packages
                    .Select(p => $"{p.PackageId} {p.Version}")
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                lingering,                                            // the collectible contexts that should be gone
                collectibleContexts = collectible
            });
        });

        // ---- CONTROL --------------------------------------------------------------------------------------
        // A collectible AssemblyLoadContext unloads only after (a) Nuplane called Unload() on it — it does, when
        // a reconcile removes the package — AND (b) no managed references remain AND (c) a GC actually runs.
        // This forces (c), repeatedly, on demand: if the references have since been released, the pending
        // unloads complete now. If a context is STILL resident afterwards, something is still pinning it — see
        // GET /assemblies -> lingering. (Pair with POST /reconcile to also re-drive Nuplane's own unload retry.)
        group.MapPost("/collect", (int? passes) =>
        {
            var n = Math.Clamp(passes ?? 3, 1, 20);
            string[] Collectible() => AssemblyLoadContext.All
                .Where(a => a.IsCollectible).Select(a => a.Name ?? "(unnamed)")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

            var before = Collectible();
            for (var i = 0; i < n; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            }
            var after = Collectible();

            return Results.Ok(new
            {
                passes = n,
                collectibleBefore = before.Length,
                collectibleAfter = after.Length,
                unloaded = before.Except(after, StringComparer.OrdinalIgnoreCase).ToArray(),
                stillResident = after
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
