using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Replacement contract for projecting an effective final post-commit delivery failure into one owning feature's safe durable state.
/// </summary>
public interface IPostCommitFailureProjector
{
    ValueTask<PostCommitFailureProjection?> ProjectAsync(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxDeliveryResult finalResult,
        CancellationToken cancellationToken = default);
}
