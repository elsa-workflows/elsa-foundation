using System.Collections.Frozen;
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
        : this(parentContext, completedChildActivityExecutionId, completedChildExecutableNodeId, outcomeNames,
            completedChildIterationId, FrozenDictionary<string, string>.Empty)
    {
    }

    public ActivityChildCompletedContext(
        IActivityExecutionContext parentContext,
        string completedChildActivityExecutionId,
        string completedChildExecutableNodeId,
        IReadOnlyCollection<string> outcomeNames,
        string? completedChildIterationId,
        IReadOnlyDictionary<string, string> childSchedulingMetadata)
    {
        ArgumentNullException.ThrowIfNull(parentContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(completedChildActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(completedChildExecutableNodeId);
        ArgumentNullException.ThrowIfNull(outcomeNames);
        ArgumentNullException.ThrowIfNull(childSchedulingMetadata);

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
        ChildSchedulingMetadata = childSchedulingMetadata.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Immutable metadata from the completed child's committed scheduling provenance. This belongs to the
    /// child invocation; metadata on a later resume or callback command is a separate delivery concern.
    /// </summary>
    public IReadOnlyDictionary<string, string> ChildSchedulingMetadata { get; }

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
