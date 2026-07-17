namespace Elsa.Activities.Http.Constants;

/// <summary>
/// Completion outcome names emitted by outbound HTTP activities. These are the branch labels an author wires
/// downstream nodes to. Outcomes are a finite part of the published activity contract; response status is data
/// on the atomic result and is never synthesized into an unpinned outcome name.
/// </summary>
public static class HttpActivityOutcomes
{
    /// <summary>A response was received and its status was present in <c>ExpectedStatusCodes</c>.</summary>
    public const string Matched = "Matched";

    /// <summary>The request failed at the transport layer (DNS, connection, TLS, protocol) — no response was received.</summary>
    public const string Failed = "Failed";

    /// <summary>The request exceeded its configured timeout before a response was received.</summary>
    public const string Timeout = "Timeout";

    /// <summary>A response was received but its status code was not in the caller's <c>ExpectedStatusCodes</c> set.</summary>
    public const string Unmatched = "Unmatched";
}
