using System.Security.Cryptography;
using System.Text;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Host.Shells;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Reconciliation;
using Nuplane.Reconciliation.Models;

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
        // that cycle added live in the running shells is the optional CShells.Nuplane integration, which refreshes the
        // runtime feature catalog and reloads active shells after Nuplane's auto-loader has loaded the new assemblies
        // (see docs/foundation-host-feeds.md, "Hot reload after a package change").
        //
        // The operations come from the host's root provider, never from the request's. The path-less shell resolves this
        // request, so the request's provider is that shell's, and CShells copies every root registration into every shell:
        // Nuplane's singletons there are second instances. A reconcile enqueued on a shell's copy of the trigger queue is read
        // by no dispatcher, so it waits for ever (#2159); only the root's queue has the dispatcher that runs it.
        var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Elsa.Foundation.Host.ModuleManagement");
        group.MapPost("/reconcile", async (CancellationToken ct) =>
            ReconcileResult(await endpoints.ServiceProvider.GetRequiredService<INuplaneAdminOperations>().TriggerReconcileAsync(ct), logger));

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

    /// <summary>The reason code of a 503: the service failed, and what it reported is logged with the correlation id, not returned.</summary>
    private const string ReconcileServiceFailed = "reconcile-service-failed";

    /// <summary>The reason code of a 500 for an outcome code this host does not know, one a newer Nuplane added.</summary>
    private const string ReconcileOutcomeUnrecognized = "reconcile-outcome-unrecognized";

    /// <summary>
    /// The answer to a reconcile, by the outcome Nuplane reports: 200 when the cycle ran (completed, or accepted and still
    /// running), 409 when it was rejected because another cycle or another process already holds the store, so the request is
    /// well formed and repeating it once that has cleared is what 409 says, 503 when the reconcile service could not run it, and
    /// 500 for an outcome code this host does not know. The outcome code travels as its name, never its number, except an unknown code, which has no name. Refusals use the
    /// same problem document as a refused reload, carrying the outcome, a fixed reason code and the correlation id that ties the request to the cycle's log lines. The message of a failure the
    /// service reported can hold paths, feed addresses or connection details, so it is logged with the correlation id and never
    /// returned; nor is any package's install path.
    /// </summary>
    private static IResult ReconcileResult(ManualReconcileOutcome outcome, ILogger logger)
    {
        var name = outcome.OutcomeCode.ToString();
        switch (outcome.OutcomeCode)
        {
            case ManualReconcileOutcomeCode.Completed or ManualReconcileOutcomeCode.Accepted:
                return Results.Ok(new
                {
                    outcomeCode = name,
                    correlationId = outcome.CorrelationId,
                    reasonCode = outcome.ReasonCode,
                    runResult = outcome.RunResult is { } run ? ReconcileRun.From(run) : null
                });
            case ManualReconcileOutcomeCode.Rejected:
                return ReconcileProblem(outcome, "Reconcile rejected", StatusCodes.Status409Conflict, outcome.ReasonCode, outcome.ReasonCode switch
                {
                    "single-flight-active" => "Another reconcile cycle is already running in this host, so this request did nothing. Repeat it once that cycle has finished.",
                    "store-lock-unavailable" => "Another process owns the package store, so this request did nothing. Repeat it once that process has released it.",
                    _ => "The reconcile was rejected, so this request did nothing."
                });
            case ManualReconcileOutcomeCode.Unavailable:
                logger.LogError("The reconcile service failed to run the cycle. CorrelationId={CorrelationId}, Reason={Reason}", outcome.CorrelationId, outcome.ReasonCode);
                return ReconcileProblem(outcome, "Reconcile unavailable", StatusCodes.Status503ServiceUnavailable, ReconcileServiceFailed,
                    "The reconcile service failed to run the cycle. The host logged the failure under the correlation id.");
            default:
                logger.LogError("Nuplane reported reconcile outcome {OutcomeCode}, which this host does not map to a status. CorrelationId={CorrelationId}, Reason={Reason}", (int)outcome.OutcomeCode, outcome.CorrelationId, outcome.ReasonCode);
                return ReconcileProblem(outcome, "Reconcile outcome not recognized", StatusCodes.Status500InternalServerError, ReconcileOutcomeUnrecognized,
                    $"Nuplane reported reconcile outcome {(int)outcome.OutcomeCode}, which this host does not map to a status, so it cannot say whether the cycle ran. The host logged it under the correlation id.");
        }
    }

    private static IResult ReconcileProblem(ManualReconcileOutcome outcome, string title, int status, string? reasonCode, string detail) =>
        Results.Problem(
            title: title,
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["outcomeCode"] = outcome.OutcomeCode.ToString(),
                ["reasonCode"] = reasonCode,
                ["correlationId"] = outcome.CorrelationId
            });

    /// <summary>
    /// What a cycle did, without where it put it: Nuplane's run result names each package's install path, an absolute path on
    /// this host, which no client needs to know what changed. A package is its id, version and the feed and source it came from.
    /// </summary>
    private sealed record ReconcileRun(bool Skipped, int SkipReason, ReconcileChangeSet ChangeSet, IReadOnlyList<string> FailedPackages, bool IsDegraded)
    {
        public static ReconcileRun From(ReconciliationRunResult run) => new(
            run.Skipped,
            (int)run.SkipReason,
            new ReconcileChangeSet(
                [.. run.ChangeSet.Added.Select(ReconciledPackage.From)],
                [.. run.ChangeSet.Updated.Select(ReconciledPackage.From)],
                run.ChangeSet.Removed,
                run.ChangeSet.CorrelationId,
                run.ChangeSet.Timestamp),
            run.FailedPackages,
            run.IsDegraded);
    }

    private sealed record ReconcileChangeSet(
        IReadOnlyList<ReconciledPackage> Added,
        IReadOnlyList<ReconciledPackage> Updated,
        IReadOnlyList<string> Removed,
        string CorrelationId,
        DateTimeOffset Timestamp);

    private sealed record ReconciledPackage(string Id, string Version, string FeedName, string SourceName, DateTimeOffset InstalledAt)
    {
        public static ReconciledPackage From(ResolvedPackage package) => new(package.Id, package.Version, package.FeedName, package.SourceName, package.InstalledAt);
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
