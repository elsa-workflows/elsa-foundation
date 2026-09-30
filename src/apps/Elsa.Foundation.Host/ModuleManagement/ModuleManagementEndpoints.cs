using System.Security.Cryptography;
using System.Text;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Host.Shells;
using Nuplane.Admin;
using Nuplane.Reconciliation;

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

        // The handler only triggers the cycle and answers with its outcome, once the cycle has finished. What makes a package
        // that cycle added live in the running shells is ShellReloadOnPackagesChanged, the Nuplane observer that refreshes the
        // runtime feature catalog and reloads the active shells at the reconciled phase, after the auto-loader has loaded the
        // new assemblies (see docs/foundation-host-feeds.md, "Hot reload is a Foundation.Host behavior, not a product one").
        //
        // The operations come from the host's root provider, never from the request's. The path-less shell resolves this
        // request, so the request's provider is that shell's, and CShells copies every root registration into every shell:
        // Nuplane's singletons there are second instances. A reconcile enqueued on a shell's copy of the trigger queue is read
        // by no dispatcher, so it waits for ever (#2159); only the root's queue has the dispatcher that runs it.
        group.MapPost("/reconcile", async (CancellationToken ct) =>
            ReconcileResult(await endpoints.ServiceProvider.GetRequiredService<INuplaneAdminOperations>().TriggerReconcileAsync(ct)));

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
    /// The answer to a reconcile, by the outcome Nuplane reports: 200 when the cycle ran (completed, or accepted and still
    /// running), 409 when it was rejected because another cycle or another process already holds the store, so the request is
    /// well formed and repeating it once that has cleared is what 409 says, and 503 when the reconcile service could not run it.
    /// The outcome code travels as its name, never its number. Refusals use the same problem document as a refused reload,
    /// carrying the outcome, Nuplane's reason and the correlation id that ties the request to the cycle's log lines.
    /// </summary>
    private static IResult ReconcileResult(ManualReconcileOutcome outcome)
    {
        var name = outcome.OutcomeCode.ToString();
        return outcome.OutcomeCode switch
        {
            ManualReconcileOutcomeCode.Completed or ManualReconcileOutcomeCode.Accepted => Results.Ok(new
            {
                outcomeCode = name,
                correlationId = outcome.CorrelationId,
                reasonCode = outcome.ReasonCode,
                runResult = outcome.RunResult
            }),
            ManualReconcileOutcomeCode.Rejected => ReconcileProblem(outcome, "Reconcile rejected", StatusCodes.Status409Conflict, outcome.ReasonCode switch
            {
                "single-flight-active" => "Another reconcile cycle is already running in this host, so this request did nothing. Repeat it once that cycle has finished.",
                "store-lock-unavailable" => "Another process owns the package store, so this request did nothing. Repeat it once that process has released it.",
                _ => "The reconcile was rejected, so this request did nothing."
            }),
            ManualReconcileOutcomeCode.Unavailable => ReconcileProblem(outcome, "Reconcile unavailable", StatusCodes.Status503ServiceUnavailable,
                "The reconcile service could not run the cycle. The reason is the failure it reported."),
            _ => throw new InvalidOperationException($"Nuplane reported a reconcile outcome, {name}, that this host does not map to a status.")
        };
    }

    private static IResult ReconcileProblem(ManualReconcileOutcome outcome, string title, int status, string detail) =>
        Results.Problem(
            title: title,
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["outcomeCode"] = outcome.OutcomeCode.ToString(),
                ["reasonCode"] = outcome.ReasonCode,
                ["correlationId"] = outcome.CorrelationId
            });

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
