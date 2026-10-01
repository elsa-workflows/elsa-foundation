using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Elsa.Workbench;

/// <summary>
/// Prunes Workbench's OpenIddict store. Every access token and every refresh token is a row (access-token validation reads it), so
/// without this the table only grows: nothing else deletes a token or an authorization.
/// </summary>
/// <remarks>
/// A root hosted service rather than an <c>IRecurringTask</c>: the store is host-owned and registered once for the process (CShells
/// copies the root descriptors into shell providers, so the vendor initializer is a root hosted service for the same reason), and
/// a recurring task belongs to a shell's Tasks feature, which would prune once per shell that runs it and only in a host that
/// enables the feature. A prune is idempotent, so every node runs it and none claims it: a row a sibling has already deleted is
/// not an error to this node, and the next interval prunes whatever a failed one left.
/// </remarks>
internal static class WorkbenchOpenIddictPruning
{
    internal static IServiceCollection AddWorkbenchOpenIddictPruning(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WorkbenchOpenIddictPruningOptions>()
            .Bind(configuration.GetSection(WorkbenchOpenIddictPruningOptions.SectionPath))
            .Validate(options => options.Interval > TimeSpan.Zero, "The OpenIddict prune interval must be greater than zero.")
            .Validate(options => options.MinimumAge >= TimeSpan.Zero, "The OpenIddict prune minimum age cannot be negative.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<WorkbenchOpenIddictPruningService>();
        return services;
    }
}

/// <summary>When the OpenIddict store is pruned, and how old an entry must be before it is.</summary>
internal sealed class WorkbenchOpenIddictPruningOptions
{
    internal const string SectionPath = "CShells:Shells:default:Features:FoundationIdentityOpenIddict:Prune";

    /// <summary>Whether this node prunes. On by default; every node may, because a prune is idempotent.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The time between prunes. The first runs when the host starts.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Only entries created longer ago than this are pruned, and then only the ones that are expired, redeemed or revoked, or
    /// whose authorization is no longer valid: a token still valid is never removed. Two weeks is OpenIddict's own default, and the
    /// refresh-token lifetime's, so a redeemed refresh token stays long enough to be recognised if it is presented again.
    /// </summary>
    public TimeSpan MinimumAge { get; set; } = TimeSpan.FromDays(14);
}

/// <summary>Prunes the OpenIddict tokens and then the authorizations on the configured interval, for as long as the host runs.</summary>
internal sealed class WorkbenchOpenIddictPruningService(
    IServiceProvider services,
    IOptions<WorkbenchOpenIddictPruningOptions> options,
    TimeProvider time,
    ILogger<WorkbenchOpenIddictPruningService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;

        // Hand the host's start back first: the first prune can be a long one on a store that has never been pruned.
        await Task.Yield();
        using var timer = new PeriodicTimer(settings.Interval, time);
        do
        {
            await PruneAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Prunes once. A failure is logged and left for the next interval, never thrown: the store may not be migrated yet, or a
    /// sibling node's prune may have deleted what this one meant to, and neither is a reason to stop the host.
    /// </summary>
    internal async Task PruneAsync(CancellationToken cancellationToken)
    {
        try
        {
            var threshold = time.GetUtcNow() - options.Value.MinimumAge;
            await using var scope = services.CreateAsyncScope();
            // Tokens first, so an ad hoc authorization whose last token this pass prunes is pruned in the same pass.
            var tokens = await scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>().PruneAsync(threshold, cancellationToken);
            var authorizations = await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().PruneAsync(threshold, cancellationToken);
            logger.LogDebug("Pruned {Tokens} OpenIddict tokens and {Authorizations} authorizations created before {Threshold:u}.", tokens, authorizations, threshold);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogWarning(failure, "Pruning the OpenIddict store failed; it is tried again in {Interval}.", options.Value.Interval);
        }
    }
}
