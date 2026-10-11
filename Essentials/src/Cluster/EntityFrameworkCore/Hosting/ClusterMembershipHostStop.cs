using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Cluster.Hosting;

/// <summary>
/// Whether the membership provider stopped this host because another process holds its host id: its member was
/// displaced, or found a live duplicate when it tried to rejoin (spec 183, FR-007 and its 2026-10-01 note). The provider
/// records the lapse before it asks the host to stop, and never sets the process's exit code itself.
/// </summary>
/// <remarks>
/// A host app runs its host with <see cref="ClusterMembershipHostExtensions.RunWithMembershipExitCode"/>, which ends the
/// process non-zero when this says the provider stopped it, so a supervisor that restarts only failed processes, such as
/// Docker's <c>restart: on-failure</c>, restarts it. Only the app's own entry point does that, so a host run inside another
/// process, as the tests run theirs, leaves that process's exit code alone. The EF provider registers one instance per
/// host, shared with every shell.
/// </remarks>
public sealed class ClusterMembershipHostStop
{
    private MemberLapse? _lapse;

    /// <summary>The lapse the provider stopped the host for, or null while it has not stopped it.</summary>
    public MemberLapse? Lapse => Volatile.Read(ref _lapse);

    /// <summary>Whether the provider stopped the host.</summary>
    public bool Stopped => Lapse is not null;

    /// <summary>Records that the provider stops the host for <paramref name="lapse"/>; the first record stands.</summary>
    public void Record(MemberLapse lapse)
    {
        ArgumentNullException.ThrowIfNull(lapse);
        Interlocked.CompareExchange(ref _lapse, lapse, null);
    }
}

/// <summary>How a host app ends when its cluster member stopped it.</summary>
public static class ClusterMembershipHostExtensions
{
    /// <summary>
    /// Runs <paramref name="host"/> until it stops, then sets the process's exit code to 1 if the membership provider
    /// stopped it because another process holds its host id (<see cref="ClusterMembershipHostStop"/>), so a supervisor that
    /// restarts failed processes restarts it. A host that composes no provider able to stop it ends as it always did. Call
    /// it from an app's entry point only: it sets the exit code of the whole process.
    /// </summary>
    public static void RunWithMembershipExitCode(this IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        // Resolved before the host runs, since running it disposes the container.
        var stop = host.Services.GetService<ClusterMembershipHostStop>();
        host.Run();
        if (stop is { Stopped: true })
            Environment.ExitCode = 1;
    }
}
