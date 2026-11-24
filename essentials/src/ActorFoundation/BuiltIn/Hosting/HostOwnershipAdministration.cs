namespace Kernel.Runtime.Hosting;

/// <summary>Host-wide ownership commands for the built-in node in this process.</summary>
/// <param name="node">The local node that owns the work.</param>
/// <param name="authorize">The trusted host's authorization check for the current caller's ownership administration permission.</param>
public sealed class HostOwnershipAdministration(RuntimeNode node, Func<bool> authorize)
{
    private readonly RuntimeNode _node = node ?? throw new ArgumentNullException(nameof(node));
    private readonly Func<bool> _authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));

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

        // The runtime's stop token is a kill deadline. Only cancel this caller's wait.
        await _node.StopAsync().AsTask().WaitAsync(cancellationToken);
        return true;
    }
}
