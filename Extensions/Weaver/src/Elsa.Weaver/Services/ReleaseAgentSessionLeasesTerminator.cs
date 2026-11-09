using CShells.Lifecycle;
using Elsa.Weaver.Core.Contracts;

namespace Elsa.Weaver.Services;

/// <summary>
/// #3441 story 2.1: a graceful host stop, or the drain of a reloaded shell generation, gives back the lease of every
/// session the default owner holds while the shell's container can still reach the record store, so the next process or
/// generation takes those sessions over at once instead of answering <c>agent.session.owned_elsewhere</c> until the
/// leases lapse. A replaced session service owns its own shutdown.
/// </summary>
internal sealed class ReleaseAgentSessionLeasesTerminator(IAgentSessionService sessions) : IShellTerminator
{
    /// <inheritdoc />
    public Task TerminateAsync(CancellationToken cancellationToken = default)
        => sessions is InMemoryAgentSessionService owner ? owner.ShutDownAsync() : Task.CompletedTask;
}
