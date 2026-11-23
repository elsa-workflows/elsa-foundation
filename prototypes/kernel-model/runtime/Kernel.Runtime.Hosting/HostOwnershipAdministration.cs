namespace Kernel.Runtime.Hosting;

/// <summary>Host-wide ownership commands for the built-in node in this process.</summary>
/// <param name="node">The local node that owns the work.</param>
/// <param name="authorize">The trusted host's authorization check for the current caller's ownership administration permission.</param>
public sealed class HostOwnershipAdministration(RuntimeNode node, Func<bool> authorize)
{
    private readonly RuntimeNode _node = node ?? throw new ArgumentNullException(nameof(node));
    private readonly Func<bool> _authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));

    /// <summary>Returns false on refusal, or true when the authorized command has waited for the node to stop.</summary>
    public async ValueTask<bool> DrainAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_authorize()) return false;
        cancellationToken.ThrowIfCancellationRequested();

        // The runtime's stop token is a kill deadline. Once admitted, an ownership command drains even if its caller leaves.
        await _node.StopAsync();
        return true;
    }
}
