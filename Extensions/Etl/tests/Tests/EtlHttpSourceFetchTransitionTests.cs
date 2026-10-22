using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Etl.Tests;

public sealed class EtlHttpSourceFetchTransitionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-20T18:00:00Z");
    private const string RequestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BodyHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Theory]
    [InlineData(408, EtlConnectorHttpSourceFetchPhase.Waiting, EtlConnectorHttpSourceFetchFailure.None)]
    [InlineData(429, EtlConnectorHttpSourceFetchPhase.Waiting, EtlConnectorHttpSourceFetchFailure.None)]
    [InlineData(500, EtlConnectorHttpSourceFetchPhase.Waiting, EtlConnectorHttpSourceFetchFailure.None)]
    [InlineData(502, EtlConnectorHttpSourceFetchPhase.Waiting, EtlConnectorHttpSourceFetchFailure.None)]
    [InlineData(503, EtlConnectorHttpSourceFetchPhase.Waiting, EtlConnectorHttpSourceFetchFailure.None)]
    [InlineData(504, EtlConnectorHttpSourceFetchPhase.Waiting, EtlConnectorHttpSourceFetchFailure.None)]
    [InlineData(201, EtlConnectorHttpSourceFetchPhase.Failed, EtlConnectorHttpSourceFetchFailure.ResponseRejected)]
    [InlineData(202, EtlConnectorHttpSourceFetchPhase.Failed, EtlConnectorHttpSourceFetchFailure.ResponseRejected)]
    [InlineData(204, EtlConnectorHttpSourceFetchPhase.Failed, EtlConnectorHttpSourceFetchFailure.ResponseRejected)]
    [InlineData(206, EtlConnectorHttpSourceFetchPhase.Failed, EtlConnectorHttpSourceFetchFailure.ResponseRejected)]
    [InlineData(302, EtlConnectorHttpSourceFetchPhase.Failed, EtlConnectorHttpSourceFetchFailure.ResponseRejected)]
    [InlineData(400, EtlConnectorHttpSourceFetchPhase.Failed, EtlConnectorHttpSourceFetchFailure.ResponseRejected)]
    [InlineData(501, EtlConnectorHttpSourceFetchPhase.Failed, EtlConnectorHttpSourceFetchFailure.ResponseRejected)]
    public void Completion_applies_only_the_source_fetch_status_rules(int statusCode,
        EtlConnectorHttpSourceFetchPhase expectedPhase, EtlConnectorHttpSourceFetchFailure expectedFailure)
    {
        var (fetch, attempt) = Begin();

        var result = EtlHttpSourceFetchTransitions.Complete(fetch, attempt, new(statusCode, null, false),
            attempt.Fence, Start.AddSeconds(1), Policy());

        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        Assert.Equal(expectedPhase, result.Fetch!.Phase);
        Assert.Equal(expectedFailure, result.Fetch.Failure);
        EtlHttpSourceFetchTransitions.Validate(result.Fetch, Policy());
    }

    [Fact]
    public void A_bare_success_is_refused_and_invalid_200_is_terminal()
    {
        var (fetch, attempt) = Begin();
        var bareSuccess = Complete(fetch, attempt, new(200, null, false), Start.AddSeconds(1));
        var invalidPage = Complete(fetch, attempt, new(200, null, true), Start.AddSeconds(1));

        Assert.Equal(EtlStoreStatus.Invalid, bareSuccess.Status);
        Assert.Same(fetch, bareSuccess.Fetch);
        Assert.Equal(EtlStoreStatus.Applied, invalidPage.Status);
        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Failed, invalidPage.Fetch!.Phase);
        Assert.Equal(EtlConnectorHttpSourceFetchFailure.InvalidPage, invalidPage.Fetch.Failure);
        EtlHttpSourceFetchTransitions.Validate(invalidPage.Fetch, Policy());
    }

    [Fact]
    public void Timely_invalid_200_receipt_can_be_reconciled_after_attempt_and_source_deadlines()
    {
        var policy = Policy(deadlineSeconds: 10, requestTimeoutSeconds: 10);
        var (fetch, attempt) = Begin(policy: policy);
        var receivedAt = Start.AddSeconds(9);
        var reconciledAt = Start.AddSeconds(11);

        var result = Complete(fetch, attempt, new(200, null, true, receivedAt), reconciledAt, policy);

        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Failed, result.Fetch!.Phase);
        Assert.Equal(EtlConnectorHttpSourceFetchFailure.InvalidPage, result.Fetch.Failure);
        Assert.Equal(reconciledAt, result.Fetch.Attempts[0].CompletedAt);
        Assert.Equal(receivedAt, result.Fetch.Attempts[0].Outcome!.ResponseReceivedAtUtc);
        EtlHttpSourceFetchTransitions.Validate(result.Fetch, policy);
    }

    [Theory]
    [InlineData("future")]
    [InlineData("before-attempt")]
    [InlineData("attempt-expiry")]
    [InlineData("source-deadline")]
    [InlineData("non-utc")]
    public void Invalid_200_receipts_outside_the_complete_response_window_are_refused(string kind)
    {
        var policy = kind == "source-deadline"
            ? Policy(deadlineSeconds: 7, requestTimeoutSeconds: 7, maxDelaySeconds: 7)
            : Policy();
        var (fetch, attempt) = Begin(policy: policy);
        var (receivedAt, now) = kind switch
        {
            "future" => (Start.AddSeconds(2), Start.AddSeconds(1)),
            "before-attempt" => (Start.AddTicks(-1), Start.AddSeconds(1)),
            "attempt-expiry" => (attempt.ExpiresAt, attempt.ExpiresAt.AddSeconds(1)),
            "source-deadline" => (fetch.Deadline, fetch.Deadline.AddSeconds(1)),
            "non-utc" => (Start.ToOffset(TimeSpan.FromHours(1)), Start.AddSeconds(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var result = Complete(fetch, attempt, new(200, null, true, receivedAt), now, policy);

        Assert.Equal(EtlStoreStatus.Invalid, result.Status);
        Assert.Same(fetch, result.Fetch);
    }

    [Fact]
    public void Legacy_invalid_200_without_receipt_time_still_expires_at_the_attempt_deadline()
    {
        var (fetch, attempt) = Begin();

        var result = Complete(fetch, attempt, new(200, null, true), attempt.ExpiresAt, Policy());

        Assert.Equal(EtlStoreStatus.Invalid, result.Status);
        Assert.Same(fetch, result.Fetch);
    }

    [Fact]
    public void Replaced_uncertain_attempt_can_capture_after_later_failure_without_rewriting_history()
    {
        var (initial, firstAttempt) = Begin();
        var replacement = EtlHttpSourceFetchTransitions.Begin(initial, RequestHash, 0, RequestId(2), 8,
            Start.AddSeconds(1), Policy());
        var second = EtlHttpSourceFetchTransitions.Begin(replacement.Fetch!, RequestHash, 0, RequestId(2), 8,
            Start.AddSeconds(2), Policy());
        var staleCompletion = EtlHttpSourceFetchTransitions.Complete(second.Fetch!, firstAttempt,
            new(500, null, false), firstAttempt.Fence, Start.AddSeconds(2), Policy());
        var laterFailure = Complete(second.Fetch!, second.Permit!, new(400, null, false), Start.AddSeconds(3));

        var captured = EtlHttpSourceFetchTransitions.Capture(laterFailure.Fetch!, firstAttempt, Page(),
            Start.AddMilliseconds(500), Start.AddSeconds(3), Policy());

        Assert.Equal(EtlStoreStatus.Applied, replacement.Status);
        Assert.Equal(EtlStoreStatus.Applied, second.Status);
        Assert.Equal(EtlStoreStatus.Conflict, staleCompletion.Status);
        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Failed, laterFailure.Fetch!.Phase);
        Assert.Null(replacement.Fetch!.Attempts[0].Outcome);
        Assert.Equal(Start.AddSeconds(1), replacement.Fetch.Attempts[0].CompletedAt);
        Assert.Equal(EtlStoreStatus.Applied, captured.Status);
        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Captured, captured.Fetch!.Phase);
        Assert.Equal(RequestId(1), captured.Fetch.Capture!.RequestId);
        Assert.Equal(7, captured.Fetch.Capture.Fence);
        Assert.Equal(Start.AddMilliseconds(500), captured.Fetch.Capture.ResponseReceivedAtUtc);
        Assert.Equal(replacement.Fetch.Attempts[0], captured.Fetch.Attempts[0]);
        Assert.Equal(second.Permit, captured.Fetch.Attempts[1].Attempt);
        Assert.Equal(new EtlHttpSourceFetchOutcome(400, null, false), captured.Fetch.Attempts[1].Outcome);
        Assert.Equal(Start.AddSeconds(3), captured.Fetch.Attempts[1].CompletedAt);
        Assert.Null(captured.Permit);

        var stopped = EtlHttpSourceFetchTransitions.Begin(captured.Fetch, RequestHash, 0, RequestId(3), 8,
            Start.AddSeconds(3), Policy());
        Assert.Equal(EtlStoreStatus.Replay, stopped.Status);
        Assert.Null(stopped.Permit);
        EtlHttpSourceFetchTransitions.Validate(captured.Fetch, Policy());
    }

    [Fact]
    public void Capture_requires_an_in_window_response_and_replays_only_an_identical_receipt()
    {
        var (fetch, attempt) = Begin();
        var exactExpiry = EtlHttpSourceFetchTransitions.Capture(fetch, attempt, Page(), attempt.ExpiresAt,
            attempt.ExpiresAt.AddSeconds(1), Policy());
        var emptyBody = EtlHttpSourceFetchTransitions.Capture(fetch, attempt, Page(bodyLength: 0), Start.AddSeconds(1),
            Start.AddSeconds(2), Policy());
        var captured = EtlHttpSourceFetchTransitions.Capture(fetch, attempt, Page(), Start.AddSeconds(1),
            Start.AddSeconds(2), Policy());
        var replay = EtlHttpSourceFetchTransitions.Capture(captured.Fetch!, attempt, Page(), Start.AddSeconds(1),
            Start.AddSeconds(3), Policy());
        var mismatch = EtlHttpSourceFetchTransitions.Capture(captured.Fetch!, attempt, Page(RequestHash.Replace('a', 'c')),
            Start.AddSeconds(1), Start.AddSeconds(3), Policy());
        var alteredPermit = attempt with { StartedAt = attempt.StartedAt.AddTicks(1) };
        var alteredPermitReplay = EtlHttpSourceFetchTransitions.Capture(captured.Fetch!, alteredPermit, Page(),
            Start.AddSeconds(1), Start.AddSeconds(3), Policy());

        Assert.Equal(EtlStoreStatus.Invalid, exactExpiry.Status);
        Assert.Equal(EtlStoreStatus.Invalid, emptyBody.Status);
        Assert.Equal(EtlStoreStatus.Applied, captured.Status);
        Assert.Equal(EtlStoreStatus.Replay, replay.Status);
        Assert.Equal(EtlStoreStatus.Conflict, mismatch.Status);
        Assert.Equal(EtlStoreStatus.Conflict, alteredPermitReplay.Status);
    }

    [Fact]
    public void Same_request_hash_cannot_remap_a_page_sequence()
    {
        var (fetch, attempt) = Begin(pageSequence: 0);
        var remappedBegin = EtlHttpSourceFetchTransitions.Begin(fetch, RequestHash, 1, RequestId(2), 8,
            Start.AddSeconds(1), Policy());
        var remappedCapture = EtlHttpSourceFetchTransitions.Capture(fetch, attempt, Page(pageSequence: 1),
            Start.AddSeconds(1), Start.AddSeconds(2), Policy());
        var captured = EtlHttpSourceFetchTransitions.Capture(fetch, attempt, Page(), Start.AddSeconds(1),
            Start.AddSeconds(2), Policy());
        var corruptedCaptureCoordinate = captured.Fetch! with { PageSequence = 1 };
        var corruptedOutOfRangeCoordinate = fetch with { PageSequence = Policy().MaxPagesPerPartition };

        Assert.Equal(EtlStoreStatus.Conflict, remappedBegin.Status);
        Assert.Equal(EtlStoreStatus.Conflict, remappedCapture.Status);
        Assert.Equal(EtlStoreStatus.Applied, captured.Status);
        Assert.Throws<InvalidDataException>(() => EtlHttpSourceFetchTransitions.Validate(corruptedCaptureCoordinate, Policy()));
        Assert.Throws<InvalidDataException>(() => EtlHttpSourceFetchTransitions.Validate(corruptedOutOfRangeCoordinate, Policy()));
    }

    [Fact]
    public void Completed_definite_failure_cannot_be_replaced_by_a_page_capture()
    {
        var (fetch, attempt) = Begin();
        var rejected = Complete(fetch, attempt, new(400, null, false), Start.AddSeconds(1));
        var capture = EtlHttpSourceFetchTransitions.Capture(rejected.Fetch!, attempt, Page(), Start.AddMilliseconds(500),
            Start.AddSeconds(1), Policy());

        Assert.Equal(EtlStoreStatus.Applied, rejected.Status);
        Assert.Equal(EtlStoreStatus.Conflict, capture.Status);
        Assert.Equal(EtlConnectorHttpSourceFetchFailure.ResponseRejected, capture.Fetch!.Failure);
    }

    [Fact]
    public void Unknown_response_closes_as_uncertain_and_uses_the_shared_retry_delay()
    {
        var (fetch, attempt) = Begin();
        var completedAt = Start.AddSeconds(1);

        var result = Complete(fetch, attempt, new(null, null, false), completedAt);

        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Waiting, result.Fetch!.Phase);
        Assert.Null(result.Fetch.Attempts[0].Outcome);
        Assert.Equal(completedAt, result.Fetch.Attempts[0].CompletedAt);
        Assert.Equal(completedAt.AddSeconds(1), result.Fetch.NotBefore);
        EtlHttpSourceFetchTransitions.Validate(result.Fetch, Policy());
    }

    [Fact]
    public void Attempt_and_deadline_bounds_fail_without_allocating_an_extra_retry()
    {
        var (oneAttempt, permit) = Begin(policy: Policy(maxAttempts: 1));
        var exhausted = Complete(oneAttempt, permit, new(500, null, false), Start.AddSeconds(1), Policy(maxAttempts: 1));

        var shortPolicy = Policy(maxAttempts: 3, deadlineSeconds: 3, requestTimeoutSeconds: 3,
            initialDelaySeconds: 2, maxDelaySeconds: 3);
        var (shortFetch, shortPermit) = Begin(policy: shortPolicy);
        var deadline = Complete(shortFetch, shortPermit, new(500, null, false), Start.AddSeconds(1), shortPolicy);

        Assert.Equal(EtlConnectorHttpSourceFetchFailure.AttemptsExhausted, exhausted.Fetch!.Failure);
        Assert.Equal(EtlConnectorHttpSourceFetchFailure.DeadlineExceeded, deadline.Fetch!.Failure);
        Assert.Null(exhausted.Permit);
        Assert.Null(deadline.Permit);
        EtlHttpSourceFetchTransitions.Validate(exhausted.Fetch, Policy(maxAttempts: 1));
        EtlHttpSourceFetchTransitions.Validate(deadline.Fetch, shortPolicy);
    }

    [Fact]
    public void Malformed_journal_expiry_and_retry_time_are_rejected()
    {
        var (fetch, attempt) = Begin();
        var waiting = Complete(fetch, attempt, new(503, null, false), Start.AddSeconds(1)).Fetch!;
        var badExpiry = waiting with
        {
            Attempts = new[]
            {
                waiting.Attempts[0] with
                {
                    Attempt = waiting.Attempts[0].Attempt with { ExpiresAt = waiting.Attempts[0].Attempt.ExpiresAt.AddSeconds(-1) }
                }
            }
        };
        var badRetry = waiting with { NotBefore = waiting.NotBefore!.Value.AddSeconds(1) };

        Assert.Throws<InvalidDataException>(() => EtlHttpSourceFetchTransitions.Validate(badExpiry, Policy()));
        Assert.Throws<InvalidDataException>(() => EtlHttpSourceFetchTransitions.Validate(badRetry, Policy()));
    }

    [Fact]
    public void Applied_capture_and_deadline_failure_snapshot_caller_attempt_collections()
    {
        var (fetch, attempt) = Begin();
        var captureAttempts = new List<EtlHttpSourceFetchAttemptRecord>(fetch.Attempts);
        var callerOwnedCapture = fetch with { Attempts = captureAttempts };
        var captured = EtlHttpSourceFetchTransitions.Capture(callerOwnedCapture, attempt, Page(), Start.AddSeconds(1),
            Start.AddSeconds(2), Policy());
        captureAttempts.Clear();

        var waiting = Complete(fetch, attempt, new(500, null, false), Start.AddSeconds(1)).Fetch!;
        var failureAttempts = new List<EtlHttpSourceFetchAttemptRecord>(waiting.Attempts);
        var callerOwnedWaiting = waiting with { Attempts = failureAttempts };
        var failed = EtlHttpSourceFetchTransitions.Begin(callerOwnedWaiting, RequestHash, 0, RequestId(2), 7,
            waiting.Deadline, Policy());
        failureAttempts.Clear();

        Assert.Equal(EtlStoreStatus.Applied, captured.Status);
        Assert.Single(captured.Fetch!.Attempts);
        Assert.Equal(EtlStoreStatus.Applied, failed.Status);
        Assert.Single(failed.Fetch!.Attempts);
        Assert.Equal(EtlConnectorHttpSourceFetchFailure.DeadlineExceeded, failed.Fetch.Failure);
    }

    private static (EtlConnectorHttpSourceFetch Fetch, EtlHttpSourceFetchAttempt Permit) Begin(EtlHttpPolicy? policy = null,
        long pageSequence = 0)
    {
        var result = EtlHttpSourceFetchTransitions.Begin(null, RequestHash, pageSequence, RequestId(1), 7, Start, policy ?? Policy());
        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        return (result.Fetch!, result.Permit!);
    }

    private static EtlConnectorHttpSourceFetchResult Complete(EtlConnectorHttpSourceFetch fetch,
        EtlHttpSourceFetchAttempt attempt, EtlHttpSourceFetchOutcome outcome, DateTimeOffset now,
        EtlHttpPolicy? policy = null) => EtlHttpSourceFetchTransitions.Complete(fetch, attempt, outcome,
        attempt.Fence, now, policy ?? Policy());

    private static EtlRetainedHttpPage Page(string? requestHash = null, long bodyLength = 16, long pageSequence = 0) => new(pageSequence, requestHash ?? RequestHash,
        new EtlObjectReference("page-0", BodyHash, bodyLength), 1, null);

    private static EtlHttpPolicy Policy(int maxAttempts = 3, int deadlineSeconds = 60, int requestTimeoutSeconds = 10,
        int initialDelaySeconds = 1, int maxDelaySeconds = 8) => new()
    {
        MaxAttempts = maxAttempts,
        DeadlineSeconds = deadlineSeconds,
        RequestTimeoutSeconds = requestTimeoutSeconds,
        InitialDelaySeconds = initialDelaySeconds,
        MaxDelaySeconds = maxDelaySeconds,
        MaxPagesPerPartition = 10,
        MaxPageBytes = 1024,
        MaxPageRecords = 10,
        MaxContinuationBytes = 128,
        MaxRetainedInputBytes = 4096,
        MaxRetainedMetadataBytes = 4096
    };

    private static string RequestId(int suffix) => suffix.ToString("x32");
}
