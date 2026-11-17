using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// Spec 1221 T003 (ADR 0048 D-9: frames and rows follow one set of rules): the versioned view of a closed scope's
/// variable rows that a capture into a row needs. The read frame is the one a body node reads the rows through, so a
/// capture is judged against the same versions its read set recorded.
/// </summary>
public static class ClosedScopeVariableRows
{
    /// <summary>The read frame of <paramref name="boundary"/>'s variable rows, with the versions they carry.</summary>
    public static VariableFrameState ReadFrame(ActivityExecutionState boundary, IEnumerable<DurableValueState> rows) =>
        RuntimeClosedScope.CreateFrame(boundary, rows);

    /// <summary><paramref name="row"/> carrying <paramref name="version"/> (its version, digest and writer).</summary>
    public static DurableValueState WithVersion(DurableValueState row, VariableVersion version, bool hidesValue = false)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(version);
        return new DurableValueState(
            row.DurableValueId,
            row.WorkflowExecutionId,
            row.ValueId,
            row.Type,
            row.Lifecycle,
            row.Storage,
            row.InlineValue,
            row.ExternalReference,
            row.SourceActivityExecutionId,
            row.CapturedAt,
            RuntimeClosedScope.WithRowVersion(row.Metadata, version, hidesValue));
    }
}
