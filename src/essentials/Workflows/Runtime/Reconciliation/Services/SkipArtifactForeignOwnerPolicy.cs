using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Models;

namespace Elsa.Workflows.Runtime.Reconciliation.Services;

/// <summary>
/// The built-in <see cref="IArtifactForeignOwnerPolicy"/>: whoever owns the slot, skip and say so by name.
/// </summary>
/// <remarks>
/// T118's default, unchanged and still the right one — a mount that loses a definition to an explicit publish is
/// a tolerated condition, not a broken deploy, and the rest of the mounted set must still import. Named for what
/// it does rather than for what it tolerates.
/// </remarks>
public sealed class SkipArtifactForeignOwnerPolicy : IArtifactForeignOwnerPolicy
{
    public ValueTask<ArtifactForeignOwnerDecision> DecideAsync(
        ArtifactForeignOwnerContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ArtifactForeignOwnerDecision.Skip);
    }
}
