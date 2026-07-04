namespace Elsa.Activities.Http.Constants;

/// <summary>
/// Completion outcome names emitted by outbound HTTP activities. These are the branch labels an author wires
/// downstream nodes to. Successful responses branch on the numeric status code (e.g. <c>"200"</c>) or the
/// default <c>Done</c> outcome; the named outcomes below cover the non-response terminal paths so a workflow
/// can react to failures without the activity throwing and faulting the run.
/// </summary>
public static class HttpActivityOutcomes
{
    /// <summary>The request failed at the transport layer (DNS, connection, TLS, protocol) — no response was received.</summary>
    public const string Failed = "Failed";

    /// <summary>The request exceeded its configured timeout before a response was received.</summary>
    public const string Timeout = "Timeout";

    /// <summary>A response was received but its status code was not in the caller's <c>ExpectedStatusCodes</c> set.</summary>
    public const string Unmatched = "Unmatched";
}
