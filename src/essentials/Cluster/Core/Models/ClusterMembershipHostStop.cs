namespace Elsa.Cluster.Core.Models;

/// <summary>
/// Whether the membership provider stopped this host because another process holds its host id: its member was
/// displaced, or found a live duplicate when it tried to rejoin (spec 183, FR-007 and its 2026-10-01 note). The provider
/// records the lapse before it asks the host to stop, and never sets the process's exit code itself.
/// </summary>
/// <remarks>
/// A host app reads it once its host has stopped and ends non-zero, so a supervisor that restarts only failed processes,
/// such as Docker's <c>restart: on-failure</c>, restarts it. Only the app's own entry point reads it, so a host run inside
/// another process, as the tests run theirs, leaves that process's exit code alone. A provider that can be displaced
/// registers one instance per host, shared with every shell, which the app resolves before it runs the host.
/// </remarks>
public sealed class ClusterMembershipHostStop
{
    private MemberLapse? _lapse;

    /// <summary>The lapse the provider stopped the host for, or null while it has not stopped it.</summary>
    public MemberLapse? Lapse => Volatile.Read(ref _lapse);

    /// <summary>The exit code the host app ends with: 1 once the provider stopped the host, otherwise 0.</summary>
    public int ExitCode => Lapse is null ? 0 : 1;

    /// <summary>Records that the provider stops the host for <paramref name="lapse"/>; the first record stands.</summary>
    public void Record(MemberLapse lapse)
    {
        ArgumentNullException.ThrowIfNull(lapse);
        Interlocked.CompareExchange(ref _lapse, lapse, null);
    }
}
