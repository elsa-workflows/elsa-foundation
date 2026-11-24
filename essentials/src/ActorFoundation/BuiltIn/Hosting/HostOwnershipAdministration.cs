namespace Kernel.Runtime.Hosting;

/// <summary>Host-wide ownership commands for the built-in node in this process.</summary>
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

    /// <summary>Requests a graceful stop of the local node.</summary>
    /// <param name="cancellationToken">Cancels this call's wait. A stop already requested keeps running.</param>
    /// <returns>
    /// False on refusal, or true when the authorized command has waited for the local stop to complete.
    /// An earlier or concurrent host kill, or a store release failure, can leave ownership to expire by lease.
    /// </returns>
    public async ValueTask<bool> DrainAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_authorize()) return false;
        cancellationToken.ThrowIfCancellationRequested();

        // The runtime's stop token is the kill deadline: the drain renews ownership until it, never past it. It belongs to
        // the stop, which may outlive this caller's wait, so it is disposed when the stop ends.
        var deadline = new CancellationTokenSource(_drainDeadline);
        var stop = _node.StopAsync(deadline.Token).AsTask();
        _ = stop.ContinueWith(static (_, source) => ((CancellationTokenSource)source!).Dispose(), deadline,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        await stop.WaitAsync(cancellationToken);
        return true;
    }
}
