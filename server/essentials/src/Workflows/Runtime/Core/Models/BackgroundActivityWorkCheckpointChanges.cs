using System.Collections.ObjectModel;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Exact ready receipt accepted by a native terminal checkpoint.</summary>
public sealed record BackgroundActivityWorkAcceptance
{
    public BackgroundActivityWorkAcceptance(string workflowExecutionId, string workId, string expectedReceiptDigest, DateTimeOffset acceptedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedReceiptDigest);
        WorkflowExecutionId = workflowExecutionId;
        WorkId = workId;
        ExpectedReceiptDigest = expectedReceiptDigest;
        AcceptedAt = acceptedAt;
    }

    public string WorkflowExecutionId { get; }
    public string WorkId { get; }
    public string ExpectedReceiptDigest { get; }
    public DateTimeOffset AcceptedAt { get; }
}

/// <summary>
/// Provider-resolved cancellation of all pending, claimed, or ready work in a workflow, or only the explicitly named
/// activity execution IDs. The runtime cancellation planner supplies the already-resolved descendant set.
/// </summary>
public sealed record BackgroundActivityWorkCancellationRequest
{
    public BackgroundActivityWorkCancellationRequest(string workflowExecutionId, IReadOnlyCollection<string>? activityExecutionIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        WorkflowExecutionId = workflowExecutionId;

        if (activityExecutionIds is null)
        {
            ActivityExecutionIds = null;
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in activityExecutionIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(activityExecutionIds));
            if (!ids.Add(id))
                throw new ArgumentException($"Activity execution ID '{id}' occurs more than once.", nameof(activityExecutionIds));
        }

        if (ids.Count == 0)
            throw new ArgumentException("An explicit activity cancellation scope must name at least one activity execution.", nameof(activityExecutionIds));

        ActivityExecutionIds = new ReadOnlyCollection<string>(ids.Order(StringComparer.Ordinal).ToArray());
    }

    public string WorkflowExecutionId { get; }

    /// <summary>Null means all background work in the workflow; otherwise contains an explicit activity/subtree ID set.</summary>
    public IReadOnlyCollection<string>? ActivityExecutionIds { get; }
}

/// <summary>Durable background-work participants committed in the same unit as native workflow state.</summary>
public sealed class BackgroundActivityWorkCheckpointChanges
{
    public static BackgroundActivityWorkCheckpointChanges Empty { get; } = new([], [], []);

    public BackgroundActivityWorkCheckpointChanges(
        IReadOnlyCollection<BackgroundActivityWorkRequest>? admissions = null,
        IReadOnlyCollection<BackgroundActivityWorkAcceptance>? acceptances = null,
        IReadOnlyCollection<BackgroundActivityWorkCancellationRequest>? cancellations = null)
    {
        Admissions = Snapshot(admissions);
        Acceptances = Snapshot(acceptances);
        Cancellations = Snapshot(cancellations);
    }

    public IReadOnlyCollection<BackgroundActivityWorkRequest> Admissions { get; }
    public IReadOnlyCollection<BackgroundActivityWorkAcceptance> Acceptances { get; }
    public IReadOnlyCollection<BackgroundActivityWorkCancellationRequest> Cancellations { get; }

    public bool IsEmpty => Admissions.Count == 0 && Acceptances.Count == 0 && Cancellations.Count == 0;

    private static IReadOnlyCollection<T> Snapshot<T>(IReadOnlyCollection<T>? values)
    {
        if (values is null || values.Count == 0)
            return Array.Empty<T>();
        if (values.Any(value => value is null))
            throw new ArgumentException("Background activity checkpoint participants cannot contain null values.", nameof(values));
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}
