using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Workbench.Readiness;
using Microsoft.Extensions.Options;

namespace Elsa.Workbench.Composition;

/// <summary>
/// Safe root-host observation of the configured default shell. No candidate identity or blueprint
/// configuration is available at this seam, so candidate match is always unverified.
/// </summary>
public static class CompositionActivationObservationEndpoints
{
    private const string CandidateMatch = "unverified";

    public static IEndpointRouteBuilder MapCompositionActivationObservation(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/_admin/composition/default-shell")
            .WithHostOwner("Elsa.Workbench")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .WithSecurityDisposition(EndpointSecurityDispositionMetadata.HostCredential(
                ManagementApiKeyAuthentication.HeaderName, "Elsa.Workbench"))
            .WithHostCredentialEnforcement(ManagementApiKeyAuthentication.HeaderName, "Elsa.Workbench")
            .AddEndpointFilter(ManagementApiKeyAuthentication.RequireAsync);

        group.MapGet("/observation", (IShellRegistry registry, ShellReadinessState readiness,
                IOptions<ShellReadinessOptions> options, WorkbenchProcessInstance process) =>
            Results.Json(Observe(registry, readiness, options.Value.DefaultShellName, process)));
        group.MapPost("/reload", ReloadAsync);
        return endpoints;
    }

    private static async Task<IResult> ReloadAsync(
        IShellRegistry registry,
        ShellReadinessState readiness,
        IOptions<ShellReadinessOptions> options,
        IHostApplicationLifetime lifetime,
        WorkbenchProcessInstance process)
    {
        var shellName = options.Value.DefaultShellName;
        var previousGeneration = Observe(registry, readiness, shellName, process).ActiveGeneration;
        int? reportedGeneration = null;
        var reloadFailed = false;
        try
        {
            // A lost HTTP response must not cancel a reload already underway. The caller can read
            // back the active generation, but cannot infer whether its candidate was activated.
            var result = await registry.ReloadAsync(shellName, lifetime.ApplicationStopping);
            reportedGeneration = result.NewShell?.Descriptor.Generation;
            reloadFailed = result.Error is not null || result.NewShell is null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException
                                         and not StackOverflowException
                                         and not AccessViolationException)
        {
            // The host cannot establish whether an interrupted/throwing reload promoted a shell.
            // Do not serialize provider exceptions or raw configuration into this response.
            var uncertain = Observe(registry, readiness, shellName, process);
            return Results.Json(new ReloadObservation("reload-uncertain", previousGeneration,
                reportedGeneration, uncertain.ActiveGeneration, uncertain.Ready, CandidateMatch,
                uncertain.ProcessInstanceId));
        }

        var current = Observe(registry, readiness, shellName, process);
        var outcome = reloadFailed
            ? current.ActiveGeneration == previousGeneration ? "reload-failed" : "reload-uncertain"
            : current.Ready && current.ActiveGeneration == reportedGeneration ? "ready" : "reload-uncertain";
        return Results.Json(new ReloadObservation(outcome, previousGeneration, reportedGeneration,
            current.ActiveGeneration, current.Ready, CandidateMatch, current.ProcessInstanceId));
    }

    private static ShellObservation Observe(IShellRegistry registry, ShellReadinessState readiness, string shellName,
        WorkbenchProcessInstance process)
    {
        var active = registry.GetActive(shellName);
        var isActive = active?.State == ShellLifecycleState.Active;
        var ready = isActive && readiness.Snapshot.Status is ShellReadinessStatus.Ready or ShellReadinessStatus.Disabled;
        return new ShellObservation(shellName, isActive ? active!.Descriptor.Generation : null, ready,
            CandidateMatch, process.Id);
    }

    private sealed record ShellObservation(string Shell, int? ActiveGeneration, bool Ready, string CandidateMatch,
        string ProcessInstanceId);

    private sealed record ReloadObservation(
        string Outcome,
        int? PreviousGeneration,
        int? ReportedGeneration,
        int? ActiveGeneration,
        bool Ready,
        string CandidateMatch,
        string ProcessInstanceId);
}

internal sealed class WorkbenchProcessInstance
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
}
