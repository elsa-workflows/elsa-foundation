using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// Runtime callback payload for an activity-owned child completion.
/// </summary>
public sealed class ActivityChildCompletedContext
{
    public ActivityChildCompletedContext(
        IActivityExecutionContext parentContext,
        string completedChildActivityExecutionId,
        string completedChildExecutableNodeId,
        IReadOnlyCollection<string> outcomeNames)
    {
        ArgumentNullException.ThrowIfNull(parentContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(completedChildActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(completedChildExecutableNodeId);
        ArgumentNullException.ThrowIfNull(outcomeNames);

        var outcomeSnapshot = outcomeNames.ToArray();
        if (outcomeSnapshot.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Outcome names cannot contain blank values.", nameof(outcomeNames));

        if (outcomeSnapshot.Distinct(StringComparer.Ordinal).Count() != outcomeSnapshot.Length)
            throw new ArgumentException("Outcome names cannot contain duplicates.", nameof(outcomeNames));

        ParentContext = parentContext;
        CompletedChildActivityExecutionId = completedChildActivityExecutionId;
        CompletedChildExecutableNodeId = completedChildExecutableNodeId;
        OutcomeNames = outcomeSnapshot;
    }

    public IActivityExecutionContext ParentContext { get; }
    public string CompletedChildActivityExecutionId { get; }
    public string CompletedChildExecutableNodeId { get; }
    public IReadOnlyCollection<string> OutcomeNames { get; }
}
