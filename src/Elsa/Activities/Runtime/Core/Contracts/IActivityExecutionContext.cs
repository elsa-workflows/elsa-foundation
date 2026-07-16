using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

public interface IActivityExecutionContext
{
    IActivity Activity { get; }
    CancellationToken CancellationToken { get; }
}

/// <summary>
/// Engine-owned, read-only outcome selected by a structural activity through its orchestration API.
/// It is separate from the author-facing typed activity context and cannot publish mutable outcomes.
/// </summary>
public interface IActivityTransitionContext
{
    string? RequestedOutcomeName { get; }
}
