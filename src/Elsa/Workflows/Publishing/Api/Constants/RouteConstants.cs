namespace Elsa.Workflows.Publishing.Api.Constants;

internal static class RouteConstants
{
    internal const string DomainPrefix = "publishing";

    /// <summary>
    /// The reserved literal segment used by the draft test-run route. It must never be bound as a
    /// <c>{versionId}</c> value, otherwise the versioned and draft test-run routes collide.
    /// </summary>
    internal const string DraftsSegment = "drafts";

    /// <summary>
    /// Inline route constraint applied to <c>{versionId}</c> on the versioned test-run route so it can
    /// never match the reserved <see cref="DraftsSegment"/> literal. Endpoint routing compares candidate
    /// endpoints by <c>Order</c> before route precedence, so a literal segment is not guaranteed to win
    /// over a parameter when the host assigns the endpoints differing orders (which can vary between
    /// process starts). Excluding the reserved word here removes the overlap entirely: exactly one
    /// endpoint matches <c>workflows/drafts/test-runs</c>, making selection deterministic in every host.
    /// </summary>
    private static string VersionIdConstraint => $@"regex(^(?!{DraftsSegment}$).+$)";

    internal static string GetRoute(string path) => string.Join('/', DomainPrefix, path.TrimStart('/'));

    internal static string Activities => GetRoute("activities");
    internal static string WorkflowSnapshotPreflight => GetRoute("workflows/preflight");
    internal static string WorkflowPreflight => GetRoute($"workflows/{{versionId:{VersionIdConstraint}}}/preflight");
    internal static string WorkflowPublish => GetRoute($"workflows/{{versionId:{VersionIdConstraint}}}/publish");
    internal static string WorkflowSlots => GetRoute("workflows/{definitionId}/slots");
    internal static string WorkflowSlot => GetRoute("workflows/{definitionId}/slots/{slotName}");
    internal static string WorkflowSlotRestore => GetRoute("workflows/{definitionId}/slots/{slotName}/restore");
    internal static string WorkflowPolicy => GetRoute("workflows/{definitionId}/policy");
    internal static string VersionedWorkflowTestRuns => GetRoute($"workflows/{{versionId:{VersionIdConstraint}}}/test-runs");
    internal static string WorkflowDraftTestRuns => GetRoute($"workflows/{DraftsSegment}/test-runs");
}
