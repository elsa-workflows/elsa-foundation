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
        IReadOnlyCollection<string> outcomeNames,
        string? completedChildIterationId = null)
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
        CompletedChildIterationId = completedChildIterationId;
    }

    public IActivityExecutionContext ParentContext { get; }
    public string CompletedChildActivityExecutionId { get; }
    public string CompletedChildExecutableNodeId { get; }
    public IReadOnlyCollection<string> OutcomeNames { get; }

    /// <summary>
    /// The engine iteration identity (<c>ActivitySchedulingProvenance.IterationId</c>) the completed
    /// child execution carried, or <c>null</c> when the child was not scheduled as a loop iteration.
    /// Counted/collection loop activities (<c>For</c>/<c>ForEach</c>/<c>While</c>/<c>Do</c>, #264–#267)
    /// read this to recover which pass just completed without holding mutable activity state across the
    /// stateless child-completion re-construction.
    /// </summary>
    public string? CompletedChildIterationId { get; }
}
