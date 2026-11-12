using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeActivityExecutionInspectionAccumulator
{
    /// <summary>Whether actual owner provenance and an immutable checkpoint delta are supported.</summary>
    bool SupportsCheckpointValueCapture => false;

    /// <summary>Optional actual-owner path; legacy implementations preserve their behavior without invoking the source factory.</summary>
    ValueTask<ActivityExecutionInspectionProjection> BuildProjectionWithSourceAsync(
        ActivityExecutionState state, string checkpointId, DateTimeOffset committedAt, Func<ActivityValueCaptureSource> source,
        IReadOnlyCollection<string>? outcomeNames = null,
        IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks = null,
        IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents = null,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default) =>
        BuildProjectionAsync(state, checkpointId, committedAt, outcomeNames, bookmarks, incidents, valueSnapshots, metadata, cancellationToken);

    ValueTask<ActivityExecutionInspectionProjection> BuildProjectionAsync(
        ActivityExecutionState state,
        string checkpointId,
        DateTimeOffset committedAt,
        IReadOnlyCollection<string>? outcomeNames = null,
        IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks = null,
        IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents = null,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);
}
