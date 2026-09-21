using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Commits the named checkpoint that consumes a durable bookmark after a successful resume.
/// </summary>
public interface IBookmarkConsumptionCheckpointService
{
    ValueTask<BookmarkConsumptionCheckpointResult> CommitAsync(
        BookmarkConsumptionCheckpointRequest request,
        CancellationToken cancellationToken = default);
}
