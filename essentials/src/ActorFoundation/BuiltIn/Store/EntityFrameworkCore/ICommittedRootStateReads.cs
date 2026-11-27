using Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Hosting;

/// <summary>Reads a committed root checkpoint for host-owned isolated TestRun controls.</summary>
/// <remarks>The caller must authorize the scope and verify the returned start against the exact TestRun.
/// Ordinary scopes and providers without the isolated TestRun profile return no checkpoint.</remarks>
public interface ICommittedRootStateReads
{
    /// <summary>Reads the exact checkpoint addressed by the supplied scope and actor reference.</summary>
    ValueTask<(string TypeData, string? State, ActorStatus Status)?> ReadCommittedStateAsync(BoundScope scope,
        ActorRef actor, CancellationToken cancellationToken);
}
