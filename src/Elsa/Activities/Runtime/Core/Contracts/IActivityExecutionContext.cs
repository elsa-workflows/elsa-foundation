using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

public interface IActivityExecutionContext
{
    TService GetRequiredService<TService>()
        where TService : notnull;

    IActivity Activity { get; }

    IActivityExecutionContext ParentActivityExecutionContext { get; }

    IAsyncEnumerable<ActivityOutputs> GetActivityOutputs();

    CancellationToken CancellationToken { get; }

    void SetOutcomes(string[] outcomes);

    IEnumerable<string> GetOutcomes();

    void CreateBookmark(ActivityBookmarkRequest request);

    IReadOnlyCollection<ActivityBookmarkRequest> GetBookmarkRequests();
}
