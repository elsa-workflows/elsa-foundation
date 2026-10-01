using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Cluster.EntityFrameworkCore;

/// <summary>
/// Drives the host's member through the host's own lifecycle (spec 183, FR-005, FR-027, FR-030): it joins while the host
/// starts, becomes active once the host has started, heartbeats on a fixed interval, drains as the host begins to stop,
/// and leaves once it has stopped. Registered after the module's migrator, so the table exists before the join.
/// </summary>
/// <remarks>
/// <para>
/// The join waits while another incarnation of the host id is still live, one heartbeat interval between attempts, each
/// refusal logged as a warning naming the incarnation it waits on. If that incarnation renews in the meantime, it is a
/// live duplicate: the join throws, and so the host refuses to start rather than retrying forever (FR-004b, FR-039).
/// </para>
/// <para>
/// The heartbeat never backs off. A failed one is followed by the next one a heartbeat interval later, so a store outage
/// can never stretch the interval past the expiry period the way a widening backoff would (FR-027). The member keeps
/// heartbeating while it drains, so it stays counted until it has really stopped.
/// </para>
/// <para>
/// A member that finds itself displaced stops the host (FR-007). Another process now holds its host id, so it never
/// rejoins and is never counted again once its entry expires, yet every loop of the host would go on working under its
/// identity: claiming and renewing work, writing at write versions it no longer advances, and serving requests nobody in
/// the fleet accounts for. Stopping is what a restart would decide anyway: the new incarnation either joins once the
/// other has gone, or refuses to start as a live duplicate (FR-004b, FR-039). A member that only lapsed, and so rejoins
/// as a new incarnation, keeps the host running; what it must not do meanwhile, each loop checks for itself.
/// </para>
/// </remarks>
internal sealed class EfClusterMembershipLifecycle(
    EfClusterMembership member,
    TimeSpan heartbeatInterval,
    TimeSpan cleanupInterval,
    TimeProvider clock,
    IHostApplicationLifetime? lifetime,
    ILogger<EfClusterMembershipLifecycle> logger) : IHostedLifecycleService, IAsyncDisposable, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _heartbeats;

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await member.JoinAsync(cancellationToken);
                break;
            }
            catch (ClusterMembershipJoinRefusedException)
            {
                await Task.Delay(heartbeatInterval, clock, cancellationToken);
            }
        }

        _heartbeats = HeartbeatAsync(_stopping.Token);
    }

    public Task StartedAsync(CancellationToken cancellationToken) => member.ActivateAsync(cancellationToken);

    public Task StoppingAsync(CancellationToken cancellationToken) => member.DrainAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        await StopHeartbeatsAsync();
        await member.LeaveAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopHeartbeatsAsync();
        _stopping.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task HeartbeatAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(heartbeatInterval, clock);
        var lastCleanup = clock.GetUtcNow();
        try
        {
            while (await timer.WaitForNextTickAsync(stopping))
            {
                try
                {
                    await member.HeartbeatAsync(stopping);
                    if (StopIfDisplaced())
                        return;
                    if (clock.GetUtcNow() - lastCleanup >= cleanupInterval)
                    {
                        lastCleanup = clock.GetUtcNow();
                        await member.CleanupAsync(stopping);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !stopping.IsCancellationRequested)
                {
                    // Never let one failure end the loop: a member whose heartbeats stopped would lapse without a cause.
                    logger.LogWarning(exception, "Cluster membership heartbeat or cleanup failed; the next heartbeat follows in {Interval}.", heartbeatInterval);
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Cancellation on shutdown is the normal exit, not a failure.
            logger.LogDebug("Cluster membership heartbeat loop stopped because the host is stopping.");
        }
    }

    /// <summary>
    /// Stops the host, and with it this loop, once the member has concluded that another process holds its host id: it was
    /// displaced, or found a live duplicate when it tried to rejoin. Returns whether it did.
    /// </summary>
    private bool StopIfDisplaced()
    {
        var standing = member.GetLocalStanding();
        if (standing.Lapse is not { Reason: MemberLapseReason.Displaced or MemberLapseReason.DuplicateHostId } lapse)
            return false;

        logger.LogCritical(
            "Cluster member {Member} was displaced ({Reason}): another process holds host id {HostId}, so this one never rejoins the fleet. " +
            "The host stops, so nothing goes on working under an identity the fleet no longer counts; start it again under a host id of its own, " +
            "or after the other process has stopped (spec 183, FR-007).",
            standing.Identity, lapse.Reason, standing.Identity.HostId);
        if (lifetime is null)
            logger.LogCritical("Cluster member {Member} has no host lifetime to stop; its host keeps running until it is stopped.", standing.Identity);
        else
            lifetime.StopApplication();
        return true;
    }

    private async Task StopHeartbeatsAsync()
    {
        if (!_stopping.IsCancellationRequested)
            await _stopping.CancelAsync();
        if (_heartbeats is { } heartbeats)
            await heartbeats;
    }
}
