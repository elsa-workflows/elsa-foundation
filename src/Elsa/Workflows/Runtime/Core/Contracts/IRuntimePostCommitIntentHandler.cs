using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Handles one already-committed runtime post-commit intent.</summary>
public interface IRuntimePostCommitIntentHandler
{
    ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default);
}
