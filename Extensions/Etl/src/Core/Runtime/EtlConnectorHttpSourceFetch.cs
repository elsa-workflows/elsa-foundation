namespace Elsa.Etl.Core.Runtime;

public enum EtlConnectorHttpSourceFetchPhase { InFlight, Waiting, Failed, Captured }
public enum EtlConnectorHttpSourceFetchFailure { None, AttemptsExhausted, DeadlineExceeded, ResponseRejected, InvalidPage }

/// <summary>A physical source request attempt bound to one immutable HTTP request identity.</summary>
/// <remarks>RequestId is a caller-generated lowercase 32-digit GUID. ExpiresAt bounds the attempt, not physical termination.</remarks>
public sealed record EtlHttpSourceFetchAttempt(string RequestId, int Number, long Fence, string RequestHash,
    DateTimeOffset StartedAt, DateTimeOffset ExpiresAt);

/// <summary>Safe response metadata without the response body, URL, headers or exception text.</summary>
/// <remarks>
/// A null status represents an uncertain transport outcome. InvalidPage is only meaningful for HTTP 200.
/// ResponseReceivedAtUtc is the complete-response receipt time and is only used for a known-invalid HTTP 200 page.
/// </remarks>
public sealed record EtlHttpSourceFetchOutcome(int? StatusCode, DateTimeOffset? RetryAfter, bool InvalidPage,
    DateTimeOffset? ResponseReceivedAtUtc = null)
{
    // Preserve source compatibility for callers that deconstruct the original three-field outcome.
    public void Deconstruct(out int? StatusCode, out DateTimeOffset? RetryAfter, out bool InvalidPage)
    {
        StatusCode = this.StatusCode;
        RetryAfter = this.RetryAfter;
        InvalidPage = this.InvalidPage;
    }
}

/// <summary>One issued attempt with optional reconciliation metadata; a null outcome means its response is unknown.</summary>
/// <remarks>CompletedAt closes the journal entry; it does not prove that the physical request has terminated.</remarks>
public sealed record EtlHttpSourceFetchAttemptRecord(EtlHttpSourceFetchAttempt Attempt,
    EtlHttpSourceFetchOutcome? Outcome, DateTimeOffset? CompletedAt);

/// <summary>A verified page captured from the exact original request attempt and its receive time.</summary>
public sealed record EtlHttpSourceFetchCapture(EtlRetainedHttpPage Page, string RequestId, long Fence,
    DateTimeOffset ResponseReceivedAtUtc);

/// <summary>Bounded retry journal for one immutable HTTP source request and page sequence.</summary>
public sealed record EtlConnectorHttpSourceFetch(string RequestHash, long PageSequence, DateTimeOffset Deadline,
    IReadOnlyList<EtlHttpSourceFetchAttemptRecord> Attempts, EtlConnectorHttpSourceFetchPhase Phase,
    DateTimeOffset? NotBefore, EtlConnectorHttpSourceFetchFailure Failure, DateTimeOffset UpdatedAt,
    EtlHttpSourceFetchCapture? Capture);

/// <summary>Only a non-null permit authorizes a physical source request.</summary>
public sealed record EtlConnectorHttpSourceFetchResult(EtlStoreStatus Status,
    EtlConnectorHttpSourceFetch? Fetch, EtlHttpSourceFetchAttempt? Permit);
