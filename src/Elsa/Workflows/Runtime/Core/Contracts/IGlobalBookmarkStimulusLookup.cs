using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Finds every waiting bookmark across all workflow executions that matches an external stimulus (W7,
/// E3-5). This is the cross-execution counterpart of <see cref="IBookmarkStimulusLookup"/> and is what
/// lets a single stimulus fan in to N waiting instances.
/// </summary>
public interface IGlobalBookmarkStimulusLookup
{
    ValueTask<GlobalBookmarkStimulusLookupResult> FindWaitingAsync(
        GlobalBookmarkStimulusLookupRequest request,
        CancellationToken cancellationToken = default);
}
