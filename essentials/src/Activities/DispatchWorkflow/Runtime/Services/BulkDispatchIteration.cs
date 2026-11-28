using System.Globalization;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>
/// Item-indexed iteration identities of a <c>BulkDispatchWorkflows</c> execution. Kept local rather than referencing
/// <c>Elsa.Activities.ControlFlow</c>'s <c>StructuredExecutionIdentity</c> ("duplication beats dependency", spec 1242
/// FR-024); spec 1243 may later move both to a shared runtime project.
/// </summary>
internal static class BulkDispatchIteration
{
    public static string Id(string parentActivityExecutionId, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return $"{Prefix(parentActivityExecutionId)}{index.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryReadIndex(string parentActivityExecutionId, string? iterationId, out int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        index = 0;
        var prefix = Prefix(parentActivityExecutionId);
        return iterationId is not null &&
               iterationId.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(iterationId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
               index >= 0;
    }

    private static string Prefix(string parentActivityExecutionId) => $"{parentActivityExecutionId}:bulk-dispatch-item:";
}
