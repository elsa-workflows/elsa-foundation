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
public static class WorkbenchOpenIddictPruning
{
    public static IServiceCollection AddWorkbenchOpenIddictPruning(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WorkbenchOpenIddictPruningOptions>()
            .Bind(configuration.GetSection(WorkbenchOpenIddictPruningOptions.SectionPath))
            .Validate(options => options.Interval > TimeSpan.Zero, "The OpenIddict prune interval must be greater than zero.")
            .Validate(options => options.MinimumAge >= TimeSpan.Zero, "The OpenIddict prune minimum age cannot be negative.")
            .Validate(options => options.Timeout > TimeSpan.Zero, "The OpenIddict prune timeout must be greater than zero.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<WorkbenchOpenIddictPruningService>();
        return services;
    }
}

/// <summary>When the OpenIddict store is pruned, and how old an entry must be before it is.</summary>
public sealed class WorkbenchOpenIddictPruningOptions
{
    /// <summary>The prune's settings, under the default shell's OpenIddict settings.</summary>
    public const string SectionPath = "CShells:Shells:default:Features:FoundationIdentityOpenIddict:Prune";

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

    /// <summary>
    /// How long one prune call (the tokens', then the authorizations') may take before it is cancelled and left for the next
    /// interval, so a prune that hangs cannot hold every later prune of the node. A prune deletes in batches and a cancelled one
    /// keeps what its finished batches deleted, so the default is generous: a store that has never been pruned takes a while.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Prunes the OpenIddict tokens and then the authorizations on the configured interval, for as long as the host runs.</summary>
public sealed class WorkbenchOpenIddictPruningService(
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
    /// Prunes once: the tokens, then the authorizations, each under <see cref="WorkbenchOpenIddictPruningOptions.Timeout"/> and
    /// independent of the other. A failure is logged and left for the next interval, never thrown: the store may not be migrated
    /// yet, or a sibling node's prune may have deleted what this one meant to, and neither is a reason to stop the host.
    /// </summary>
    public async Task PruneAsync(CancellationToken cancellationToken)
    {
        try
        {
            var threshold = time.GetUtcNow() - options.Value.MinimumAge;
            await using var scope = services.CreateAsyncScope();
            // Tokens first, so an ad hoc authorization whose last token this pass prunes is pruned in the same pass.
            await RunAsync("tokens", scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>().PruneAsync, threshold, cancellationToken);
            await RunAsync("authorizations", scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().PruneAsync, threshold, cancellationToken);
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

    private async Task RunAsync(string kind, Func<DateTimeOffset, CancellationToken, ValueTask<long>> prune, DateTimeOffset threshold, CancellationToken stopping)
    {
        using var timeout = new CancellationTokenSource(options.Value.Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, timeout.Token);
        try
        {
            var pruned = await prune(threshold, linked.Token);
            logger.LogDebug("Pruned {Count} OpenIddict {Kind} created before {Threshold:u}.", pruned, kind, threshold);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            logger.LogWarning("Pruning the OpenIddict {Kind} did not finish in {Timeout}; it is cancelled and tried again in {Interval}.", kind, options.Value.Timeout, options.Value.Interval);
        }
        catch (Exception failure)
        {
            logger.LogWarning(failure, "Pruning the OpenIddict {Kind} failed; it is tried again in {Interval}.", kind, options.Value.Interval);
        }
    }
}
