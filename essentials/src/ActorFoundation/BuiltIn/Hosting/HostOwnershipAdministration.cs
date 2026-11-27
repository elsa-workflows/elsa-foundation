namespace Kernel.Runtime.Hosting;

/// <summary>Host-wide ownership commands for the built-in node in this process: release some partitions, or drain the node.</summary>
/// <param name="node">The local node that owns the work.</param>
/// <param name="authorize">The trusted host's authorization check for the current caller's ownership administration permission.</param>
/// <param name="drainDeadline">How long the graceful stop may drain before it becomes a kill and what is still owned runs
/// out by its lease. <see cref="DefaultDrainDeadline"/> when null.</param>
public sealed class HostOwnershipAdministration(RuntimeNode node, Func<bool> authorize, TimeSpan? drainDeadline = null)
{
    /// <summary>The drain deadline when none is given: the default application shutdown timeout.</summary>
    public static readonly TimeSpan DefaultDrainDeadline = TimeSpan.FromSeconds(30);

    private readonly RuntimeNode _node = node ?? throw new ArgumentNullException(nameof(node));
    private readonly Func<bool> _authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));
    private readonly TimeSpan _drainDeadline = drainDeadline is { } given && given > TimeSpan.Zero ? given
        : drainDeadline is null ? DefaultDrainDeadline
        : throw new ArgumentOutOfRangeException(nameof(drainDeadline), "The drain deadline must be positive.");

    /// <summary>
    /// Releases the listed partitions that the local node holds, through the runtime's own release. Each one is free at its
    /// epoch, and placement decides who takes it next: the next balancing round may give it back to this node when it is
    /// below its share. That take counts no death. It reads no tenant row.
    /// </summary>
    /// <param name="partitions">The virtual partitions to release, each in range for the node once authorized.</param>
    /// <param name="cancellationToken">Cancels the release; a partition already given up stays released.</param>
    /// <returns>
    /// Not authorized on refusal, before the partitions are checked. Otherwise the partitions given up, those
    /// whose fence the store released (<see cref="PartitionRuntime.ReleaseAsync"/>): a listed partition that the node does
    /// not hold, or is taking, giving up or passing, is skipped, as is one the cancellation stopped before, and none is given
    /// up once a stop or kill has begun. The result does not say which of these left a partition out.
    /// </returns>
    public async ValueTask<PartitionRelease> ReleaseAsync(IReadOnlyCollection<int> partitions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_authorize()) return new(false, []);
        foreach (var partition in partitions)
            if ((uint)partition >= (uint)_node.Runtime.Partitions)
                throw new ArgumentOutOfRangeException(nameof(partitions), partition, "A partition is outside the node's range.");
        cancellationToken.ThrowIfCancellationRequested();
        return new(true, await _node.ReleaseAsync(partitions, cancellationToken));
    }

    /// <summary>Requests a graceful stop of the local node.</summary>
    /// <param name="cancellationToken">Cancels this call's wait. A stop already requested keeps running.</param>
    /// <returns>
    /// False on refusal. True when the authorized command has waited for the local stop: it completed, or its deadline
    /// made it a kill and what is still owned runs out by its lease. When a stop was already under way, the command joins
    /// it: its drain deadline then bounds only this wait, never the other caller's, and true can mean that this wait ended
    /// first. An earlier or concurrent host kill, or a store release failure, can leave ownership to expire by lease.
    /// </returns>
    public async ValueTask<bool> DrainAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_authorize()) return false;
        cancellationToken.ThrowIfCancellationRequested();

        // When this call starts the stop, its token is the kill deadline: the drain renews ownership until it, never past
        // it. It belongs to the stop, which may outlive this caller's wait, so it is disposed when the stop ends.
        var deadline = new CancellationTokenSource(_drainDeadline);
        var stop = _node.StopAsync(deadline.Token).AsTask();
        _ = stop.ContinueWith(static (_, source) => ((CancellationTokenSource)source!).Dispose(), deadline,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        // A joined stop's own deadline ends only this wait; the stop it joined drains on.
        try { await stop.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
        return true;
    }
}

/// <summary>The answer to a host-admin release: whether the caller was authorized, and the partitions given up.</summary>
public sealed record PartitionRelease(bool Authorized, IReadOnlyList<int> Released);
