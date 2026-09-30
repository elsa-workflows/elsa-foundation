using System.Security.Cryptography;
using System.Text;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Host.Shells;
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
public static class ModuleManagementEndpoints
{
    public static IEndpointRouteBuilder MapModuleManagementApi(this IEndpointRouteBuilder endpoints, ModuleManagementOptions options)
    {
        var group = endpoints.MapGroup("/_module-management");
        group.WithHostOwner("Elsa.Foundation.Host")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .WithSecurityDisposition(EndpointSecurityDispositionMetadata.HostCredential(
                ModuleManagementOptions.ApiKeyHeader,
                "Elsa.Foundation.Host"))
            .WithHostCredentialEnforcement(ModuleManagementOptions.ApiKeyHeader, "Elsa.Foundation.Host");
        group.AddEndpointFilter(async (context, next) =>
            Authorized(context.HttpContext, options) ? await next(context) : Results.Unauthorized());

        // Inline hot-apply. This is the race-free way to make an added/updated feed package go live without a
        // restart. TriggerReconcileAsync runs the FULL reconcile cycle and returns only once it has completed
        // — including the completion phase where Nuplane's auto-loader loads any new package's assemblies into
        // their load contexts. ONLY THEN do we refresh the runtime feature catalog (so feature ids rebind to
        // the now-loaded assemblies) and reload the active shells (so they compose the new endpoints from the
        // refreshed catalog). Doing the refresh/reload from a package-change observer instead would run too
        // early (assembly not loaded yet) and would re-enter and stall the cycle.
        //
        // The operations come from the host's root provider, never from the request's. The path-less shell resolves this
        // request, so the request's provider is that shell's, and CShells copies every root registration into every shell:
        // Nuplane's singletons there are second instances. A reconcile enqueued on the shell's copy of the trigger queue is
        // read by no dispatcher, so it waits for ever (#2159). Only the root's queue has the dispatcher that runs it.
        group.MapPost("/reconcile", async (CancellationToken ct) =>
            Results.Ok(await endpoints.ServiceProvider.GetRequiredService<INuplaneAdminOperations>().TriggerReconcileAsync(ct)));

        group.MapPost("/reload", async (IRuntimeFeatureCatalog runtimeFeatureCatalog, IShellRegistry registry, CancellationToken ct) =>
        {
            var snapshot = await runtimeFeatureCatalog.RefreshAsync(ct);
            var results = await registry.ReloadActiveAsync(null, ct);
            // CShells reports a shell that could not activate in its ReloadResult and keeps the previous generation
            // serving, so the count of results is not the count of reloads.
            var failures = ShellReloadFailure.From(results, ShellReloadFailure.HostDirectory);
            return failures.Count == 0
                ? Results.Ok(new { features = snapshot.FeatureDescriptors.Count, reloaded = results.Count })
                : FailedReload(snapshot.FeatureDescriptors.Count, results.Count - failures.Count, failures);
        });

        return endpoints;
    }

    /// <summary>
    /// A reload that left at least one shell on its previous generation. 409 when every such shell was refused by an EF module,
    /// typically a database whose migrations were not applied to the schema the new package version needs: the request is well
    /// formed and the host is healthy, but the state it asks for conflicts with the state of the world, and the operator
    /// resolves that outside this request and then repeats it, which is what 409 says. 500 as soon as one shell failed for any
    /// other reason, which is a fault of the host or its packages that repeating the request does not clear. The body is the
    /// same problem document either way.
    /// </summary>
    private static IResult FailedReload(int features, int reloaded, IReadOnlyList<ShellReloadFailure> failures)
    {
        var refused = failures.All(failure => failure.Refusal is not null);
        return Results.Problem(
            title: refused ? "Shell reload refused" : "Shell reload failed",
            detail: string.Join(
                " ",
                failures.Select(failure => $"Shell '{failure.Shell}' was not reloaded and its previous generation is still active: {failure.Error}")),
            statusCode: refused ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError,
            extensions: new Dictionary<string, object?>
            {
                ["features"] = features,
                ["reloaded"] = reloaded,
                ["shells"] = failures.Select(failure => new
                {
                    shell = failure.Shell,
                    error = failure.Error,
                    code = failure.Refusal?.Code,
                    module = failure.Refusal?.Module,
                    pendingMigrations = failure.Refusal?.PendingMigrations,
                    command = failure.Refusal?.Command
                }).ToArray()
            });
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
