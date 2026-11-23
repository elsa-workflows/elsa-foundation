using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Finds durable bookmark state for one workflow execution and external stimulus identity.
/// </summary>
public interface IBookmarkStimulusLookup
{
    ValueTask<BookmarkStimulusLookupResult> FindAsync(BookmarkStimulusLookupRequest request, CancellationToken cancellationToken = default);
}
