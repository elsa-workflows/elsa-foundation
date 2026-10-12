namespace Elsa.Activities.Http.Constants;

/// <summary>
/// Completion outcome names emitted by outbound HTTP activities. These are the branch labels an author wires
/// downstream nodes to. Outcomes are a finite part of the published activity contract: when expected status codes
/// are authored, each becomes an outcome port named after the numeric code (pinned per node at publish time), so a
/// response status is a routing branch, not just data on the atomic result (issue #926).
/// </summary>
public static class HttpActivityOutcomes
{
    /// <summary>The request failed at the transport layer (DNS, connection, TLS, protocol) — no response was received.</summary>
    public const string Failed = "Failed";

    /// <summary>The request exceeded its configured timeout before a response was received.</summary>
    public const string Timeout = "Timeout";

    /// <summary>
    /// A response was received but its status code was not one of the caller's authored <c>ExpectedStatusCodes</c>.
    /// When expected codes are configured a matching response instead branches on the status-code outcome named after
    /// the numeric code (e.g. <c>"200"</c>, <c>"404"</c>); this is the catch-all for everything else (issue #926).
    /// </summary>
    public const string UnmatchedStatusCode = "Unmatched status code";
}
