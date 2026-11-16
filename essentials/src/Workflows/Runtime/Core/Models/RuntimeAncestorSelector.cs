namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Which committed ancestor an ancestor notification addresses (<see cref="RuntimeAncestorNotificationRequest"/>).
/// Exactly one of <see cref="ConsumerKey"/> or <see cref="ActivityTypeKey"/> is set; the nearest ancestor that matches
/// is the target.
/// </summary>
/// <param name="ConsumerKey">Matches the ancestor node's runtime consumer key, e.g. <c>elsa.graph-activity</c>.</param>
/// <param name="ActivityTypeKey">Matches the ancestor node's activity type key, e.g. a CLR activity's type name.</param>
/// <param name="AuthoredActivityId">With <see cref="ActivityTypeKey"/>: only the ancestor of that authored node matches.</param>
/// <param name="StopAtGraphBoundary">A non-matching ancestor with consumer <c>elsa.graph-activity</c> (a reusable activity) ends the walk.</param>
/// <param name="Optional">When nothing matches, send nothing instead of faulting the requester.</param>
public sealed record RuntimeAncestorSelector(
    string? ConsumerKey = null,
    string? ActivityTypeKey = null,
    string? AuthoredActivityId = null,
    bool StopAtGraphBoundary = false,
    bool Optional = false)
{
    /// <summary>A short description for diagnostics.</summary>
    public string Describe() =>
        ConsumerKey is not null
            ? $"consumer '{ConsumerKey}'"
            : AuthoredActivityId is null
                ? $"type '{ActivityTypeKey}'"
                : $"type '{ActivityTypeKey}' and authored id '{AuthoredActivityId}'";
}
