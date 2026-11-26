using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeActivityExecutionInspectionAccumulator
{
    /// <summary>Whether actual owner provenance and an immutable checkpoint delta are supported.</summary>
    bool SupportsCheckpointValueCapture => false;

    /// <summary>Optional actual-owner path; legacy implementations preserve their behavior without invoking the source factory.</summary>
    /// <exception cref="RuntimeSchedulerWorkUnavailableException">A qualified owner cannot establish required policy/evidence storage; the original cause is retained.</exception>
    /// <exception cref="RuntimeHistoryIntegrityException">A qualified owner's retained policy or inspection evidence is malformed; the original cause is retained as a hard failure.</exception>
    ValueTask<ActivityExecutionInspectionProjection> BuildProjectionWithSourceAsync(
        ActivityExecutionState state, string checkpointId, DateTimeOffset committedAt, Func<ActivityValueCaptureOrigin> source,
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
