using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>Supplies the exact run and its committed activity collection to the authorized browse endpoint.</summary>
public interface IActivityExecutionBrowseSource
{
    bool SupportsExecutionBrowsing { get; }

    ValueTask<ActivityExecutionBrowseRun?> FindWorkflowAsync(string run, CancellationToken cancellationToken);
}

/// <summary>An exact run and its browse operation. The endpoint authorizes the run before calling <see cref="BrowseAsync"/>.</summary>
public sealed record ActivityExecutionBrowseRun(
    WorkflowExecutionState State,
    bool SupportsExecutionBrowsing,
    Func<ActivityExecutionBrowseQuery, long?, CancellationToken, ValueTask<ActivityExecutionBrowseResult?>> BrowseAsync);

/// <summary>A browse page and the committed journal revision that fixes its result set across continuations, when available.</summary>
public sealed record ActivityExecutionBrowseResult(ActivityExecutionBrowsePage Page, long? JournalHeadRevision = null);
