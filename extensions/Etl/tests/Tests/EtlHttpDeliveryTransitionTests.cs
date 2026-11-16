using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Etl.Tests;

public sealed class EtlHttpDeliveryTransitionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-20T18:00:00Z");

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    public void Only_the_documented_success_statuses_acknowledge(int statusCode)
    {
        var (state, attempt) = Begin();

        var result = Complete(state, attempt, new(statusCode, null), Start.AddSeconds(1));

        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, result.Delivery!.Phase);
        Assert.Equal(EtlHttpDeliveryFailure.None, result.Delivery.Failure);
        Assert.Null(result.Permit);
    }

    [Theory]
    [InlineData(202)]
    [InlineData(203)]
    [InlineData(302)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(501)]
    public void Accepted_but_unacknowledged_and_permanent_statuses_fail(int statusCode)
    {
        var (state, attempt) = Begin();

        var result = Complete(state, attempt, new(statusCode, null), Start.AddSeconds(1));

        Assert.Equal(EtlHttpDeliveryPhase.Failed, result.Delivery!.Phase);
        Assert.Equal(EtlHttpDeliveryFailure.ResponseRejected, result.Delivery.Failure);
        Assert.Null(result.Delivery.NotBefore);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void Transient_statuses_schedule_the_first_exponential_delay(int statusCode)
    {
        var (state, attempt) = Begin();
        var completedAt = Start.AddSeconds(1);

        var result = Complete(state, attempt, new(statusCode, null), completedAt);

        Assert.Equal(EtlHttpDeliveryPhase.Waiting, result.Delivery!.Phase);
        Assert.Equal(completedAt.AddSeconds(1), result.Delivery.NotBefore);
        Assert.Equal(EtlHttpDeliveryFailure.None, result.Delivery.Failure);

        var early = EtlHttpDeliveryTransitions.Begin(result.Delivery, EffectId, RequestId(2), 7,
            completedAt, Policy());
        var due = EtlHttpDeliveryTransitions.Begin(result.Delivery, EffectId, RequestId(2), 7,
            completedAt.AddSeconds(1), Policy());
        Assert.Equal(EtlStoreStatus.Conflict, early.Status);
        Assert.Equal(EtlStoreStatus.Applied, due.Status);
        Assert.NotNull(due.Permit);
    }

    [Fact]
    public void Exponential_backoff_stops_at_the_configured_cap()
    {
        var policy = Policy(maxAttempts: 5, deadlineSeconds: 60, initialDelaySeconds: 2, maxDelaySeconds: 5);
        var (state, attempt) = Begin(policy: policy);
        var first = Complete(state, attempt, new(500, null), Start.AddSeconds(1), policy: policy);
        var secondAttempt = Begin(first.Delivery!, RequestId(2), Start.AddSeconds(3), fence: 7, policy: policy);
        var second = Complete(secondAttempt.Delivery!, secondAttempt.Permit!, new(500, null), Start.AddSeconds(4), policy: policy);
        var thirdAttempt = Begin(second.Delivery!, RequestId(3), Start.AddSeconds(8), fence: 7, policy: policy);
        var third = Complete(thirdAttempt.Delivery!, thirdAttempt.Permit!, new(500, null), Start.AddSeconds(9), policy: policy);

        Assert.Equal(Start.AddSeconds(14), third.Delivery!.NotBefore); // Attempt 3 would be 8 seconds without the cap.
    }

    [Fact]
    public void Uncertain_outcome_uses_exponential_backoff_and_honors_a_later_retry_after()
    {
        var policy = Policy(maxAttempts: 4, deadlineSeconds: 60, initialDelaySeconds: 2, maxDelaySeconds: 5);
        var (state, attempt) = Begin(policy: policy);

        var first = Complete(state, attempt, new(503, null), Start.AddSeconds(1), policy: policy);
        Assert.Equal(Start.AddSeconds(3), first.Delivery!.NotBefore);

        var second = Begin(first.Delivery, RequestId(2), Start.AddSeconds(3), fence: 7, policy: policy);
        var uncertain = Complete(second.Delivery!, second.Permit!, new(null, null), Start.AddSeconds(4), policy: policy);
        Assert.Equal(Start.AddSeconds(8), uncertain.Delivery!.NotBefore); // 2 * 2 seconds.
        Assert.Null(Assert.Single(uncertain.Delivery.Attempts, x => x.Attempt.Number == 2).Response);

        var third = Begin(uncertain.Delivery, RequestId(3), Start.AddSeconds(8), fence: 7, policy: policy);
        var retryAfter = Start.AddSeconds(28); // Longer than the five second cap, within the deadline.
        var delayed = Complete(third.Delivery!, third.Permit!, new(429, retryAfter), Start.AddSeconds(9), policy: policy);

        Assert.Equal(retryAfter, delayed.Delivery!.NotBefore);
        Assert.Equal(EtlStoreStatus.Applied, delayed.Status);
    }

    [Fact]
    public void Retry_after_at_the_deadline_exhausts_without_an_early_retry()
    {
        var policy = Policy(maxAttempts: 3, deadlineSeconds: 20, initialDelaySeconds: 1, maxDelaySeconds: 5);
        var (state, attempt) = Begin(policy: policy);
        var deadline = state.Deadline;

        var result = Complete(state, attempt, new(503, deadline), Start.AddSeconds(1), policy: policy);

        Assert.Equal(EtlHttpDeliveryPhase.Failed, result.Delivery!.Phase);
        Assert.Equal(EtlHttpDeliveryFailure.DeadlineExceeded, result.Delivery.Failure);
        Assert.Null(result.Delivery.NotBefore);
    }

    [Fact]
    public void Reaching_the_attempt_limit_fails_without_a_new_wait()
    {
        var policy = Policy(maxAttempts: 1);
        var (state, attempt) = Begin(policy: policy);

        var result = Complete(state, attempt, new(503, null), Start.AddSeconds(1), policy: policy);

        Assert.Equal(EtlHttpDeliveryPhase.Failed, result.Delivery!.Phase);
        Assert.Equal(EtlHttpDeliveryFailure.AttemptsExhausted, result.Delivery.Failure);
        Assert.Null(result.Delivery.NotBefore);
    }

    [Fact]
    public void Begin_replays_only_the_current_live_permit_and_completion_is_idempotent()
    {
        var (state, attempt) = Begin();

        var identical = EtlHttpDeliveryTransitions.Begin(state, EffectId, RequestId(1), 7, Start.AddSeconds(1), Policy());
        var competing = EtlHttpDeliveryTransitions.Begin(state, EffectId, RequestId(2), 7, Start.AddSeconds(1), Policy());
        var wrongFence = EtlHttpDeliveryTransitions.Begin(state, EffectId, RequestId(1), 8, Start.AddSeconds(1), Policy());
        var acknowledged = Complete(state, attempt, new(200, null), Start.AddSeconds(2));
        var duplicateComplete = Complete(acknowledged.Delivery!, attempt, new(200, null), Start.AddSeconds(50));
        var changedComplete = Complete(acknowledged.Delivery!, attempt, new(201, null), Start.AddSeconds(3));
        var terminalBegin = EtlHttpDeliveryTransitions.Begin(acknowledged.Delivery, EffectId, RequestId(99), 7,
            Start.AddSeconds(3), Policy());

        Assert.Equal(EtlStoreStatus.Replay, identical.Status);
        Assert.Equal(attempt, identical.Permit);
        Assert.Equal(EtlStoreStatus.Conflict, competing.Status);
        Assert.Null(competing.Permit);
        Assert.Equal(EtlStoreStatus.Conflict, wrongFence.Status);
        Assert.Equal(EtlStoreStatus.Replay, duplicateComplete.Status);
        Assert.Equal(EtlStoreStatus.Conflict, changedComplete.Status);
        Assert.Equal(EtlStoreStatus.Replay, terminalBegin.Status);
        Assert.Null(terminalBegin.Permit);
    }

    [Fact]
    public void Replaced_old_request_conflicts_even_after_terminal_state_but_a_new_id_replays()
    {
        var policy = Policy();
        var (state, firstAttempt) = Begin(policy: policy);
        var waiting = Complete(state, firstAttempt, new(500, null), Start.AddSeconds(1), policy: policy);
        var second = Begin(waiting.Delivery!, RequestId(2), Start.AddSeconds(2), fence: 8, policy: policy);
        var acknowledged = Complete(second.Delivery!, second.Permit!, new(200, null), Start.AddSeconds(3), policy: policy);

        var oldRequest = EtlHttpDeliveryTransitions.Begin(acknowledged.Delivery, EffectId, RequestId(1), 7,
            Start.AddSeconds(4), policy);
        var newRequest = EtlHttpDeliveryTransitions.Begin(acknowledged.Delivery, EffectId, RequestId(99), 7,
            Start.AddSeconds(4), policy);

        Assert.Equal(EtlStoreStatus.Conflict, oldRequest.Status);
        Assert.Equal(EtlStoreStatus.Replay, newRequest.Status);
        Assert.Null(newRequest.Permit);
    }

    [Fact]
    public void A_replacement_fence_records_uncertainty_before_a_new_request_can_start()
    {
        var (state, attempt) = Begin();

        var replaced = EtlHttpDeliveryTransitions.Begin(state, EffectId, RequestId(2), 8, Start.AddSeconds(1), Policy());
        var oldSuccess = Complete(replaced.Delivery!, attempt,
            new EtlHttpDeliveryResponse(200, null) { ResponseReceivedAtUtc = Start.AddMilliseconds(500) }, Start.AddSeconds(1));
        var oldUncertain = Complete(replaced.Delivery!, attempt, new(null, null), Start.AddSeconds(1));
        var early = EtlHttpDeliveryTransitions.Begin(replaced.Delivery, EffectId, RequestId(3), 8,
            Start.AddSeconds(1), Policy());
        var afterDelay = EtlHttpDeliveryTransitions.Begin(replaced.Delivery, EffectId, RequestId(3), 8,
            Start.AddSeconds(2), Policy());
        var staleBegin = EtlHttpDeliveryTransitions.Begin(afterDelay.Delivery, EffectId, RequestId(1), 7,
            Start.AddSeconds(2), Policy());
        var staleComplete = Complete(afterDelay.Delivery!, attempt, new(null, null), Start.AddSeconds(2));

        Assert.Equal(EtlStoreStatus.Applied, replaced.Status);
        Assert.Null(replaced.Permit);
        Assert.Equal(EtlHttpDeliveryPhase.Waiting, replaced.Delivery!.Phase);
        Assert.Null(replaced.Delivery.Attempts[0].Response);
        Assert.Equal(Start.AddSeconds(1), replaced.Delivery.Attempts[0].CompletedAt);
        Assert.Equal(EtlStoreStatus.Conflict, oldSuccess.Status);
        Assert.Equal(EtlStoreStatus.Replay, oldUncertain.Status);
        Assert.Equal(EtlStoreStatus.Conflict, early.Status);
        Assert.Equal(EtlStoreStatus.Applied, afterDelay.Status);
        Assert.Equal(EtlStoreStatus.Conflict, staleBegin.Status);
        Assert.Equal(EtlStoreStatus.Conflict, staleComplete.Status);
        Assert.Equal(2, afterDelay.Permit!.Number);
        Assert.Equal(8, afterDelay.Permit.Fence);
    }

    [Fact]
    public void Waiting_expires_at_the_deadline_and_each_request_timeout_is_clamped_to_it()
    {
        var policy = Policy(maxAttempts: 3, deadlineSeconds: 10, requestTimeoutSeconds: 8,
            initialDelaySeconds: 1, maxDelaySeconds: 4);
        var (state, attempt) = Begin(policy: policy);
        var waiting = Complete(state, attempt, new(500, null), Start.AddSeconds(7), policy: policy);
        var retry = Begin(waiting.Delivery!, RequestId(2), Start.AddSeconds(8), fence: 7, policy: policy);

        Assert.Equal(Start.AddSeconds(10), retry.Permit!.ExpiresAt);

        var expired = EtlHttpDeliveryTransitions.Begin(waiting.Delivery, EffectId, RequestId(2), 7,
            Start.AddSeconds(10), policy);

        Assert.Equal(EtlStoreStatus.Applied, expired.Status);
        Assert.Equal(EtlHttpDeliveryPhase.Failed, expired.Delivery!.Phase);
        Assert.Equal(EtlHttpDeliveryFailure.DeadlineExceeded, expired.Delivery.Failure);
        Assert.Null(expired.Permit);
    }

    [Fact]
    public void Deadline_and_retry_expiry_near_datetimeoffset_max_do_not_overflow()
    {
        var policy = Policy(maxAttempts: 3, deadlineSeconds: 10, requestTimeoutSeconds: 8,
            initialDelaySeconds: 1, maxDelaySeconds: 4);
        var nearMax = DateTimeOffset.MaxValue.AddSeconds(-10);
        var first = EtlHttpDeliveryTransitions.Begin(null, EffectId, RequestId(1), 7, nearMax, policy);
        var waiting = EtlHttpDeliveryTransitions.Complete(first.Delivery!, first.Permit!, new(500, null), 7,
            nearMax.AddSeconds(7), policy);
        var retry = EtlHttpDeliveryTransitions.Begin(waiting.Delivery, EffectId, RequestId(2), 7,
            nearMax.AddSeconds(9), policy);

        Assert.Equal(EtlStoreStatus.Applied, first.Status);
        Assert.Equal(DateTimeOffset.MaxValue, first.Delivery!.Deadline);
        Assert.Equal(DateTimeOffset.MaxValue, retry.Permit!.ExpiresAt);
        EtlHttpDeliveryTransitions.Validate(retry.Delivery!, EffectId, policy);
    }

    [Fact]
    public void Completion_after_request_expiry_is_rejected_but_uncertainty_can_be_recorded()
    {
        var (state, attempt) = Begin();
        var lateResponse = Complete(state, attempt, new(200, null), attempt.ExpiresAt.AddSeconds(1));
        var exactExpiryResponse = Complete(state, attempt, new(200, null), attempt.ExpiresAt);
        var uncertain = Complete(state, attempt, new(null, null), attempt.ExpiresAt.AddSeconds(1));

        Assert.Equal(EtlStoreStatus.Invalid, lateResponse.Status);
        Assert.Equal(EtlStoreStatus.Invalid, exactExpiryResponse.Status);
        Assert.Same(state, lateResponse.Delivery);
        Assert.Equal(EtlStoreStatus.Applied, uncertain.Status);
        Assert.Equal(EtlHttpDeliveryPhase.Waiting, uncertain.Delivery!.Phase);
        Assert.Null(uncertain.Delivery.Attempts[0].Response);
    }

    [Fact]
    public void Timely_success_receipt_can_be_reconciled_after_the_attempt_and_run_deadline()
    {
        var policy = Policy(maxAttempts: 2, deadlineSeconds: 10, requestTimeoutSeconds: 10);
        var (state, attempt) = Begin(policy);
        var receivedAt = attempt.ExpiresAt.AddTicks(-1);
        var reconciledAt = attempt.ExpiresAt.AddSeconds(1);
        var response = new EtlHttpDeliveryResponse(204, null) { ResponseReceivedAtUtc = receivedAt };

        var result = Complete(state, attempt, response, reconciledAt, policy);

        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, result.Delivery!.Phase);
        var recorded = Assert.Single(result.Delivery.Attempts);
        Assert.Equal(response, recorded.Response);
        Assert.Equal(reconciledAt, recorded.CompletedAt);
        Assert.True(recorded.CompletedAt!.Value > recorded.Response!.ResponseReceivedAtUtc!.Value);
        Assert.True(recorded.CompletedAt.Value > result.Delivery.Deadline);
        EtlHttpDeliveryTransitions.Validate(result.Delivery, EffectId, policy);
    }

    [Fact]
    public void Timely_transient_receipt_reconciled_after_request_expiry_uses_reconciliation_time_for_backoff()
    {
        var policy = Policy(maxAttempts: 2, deadlineSeconds: 30, requestTimeoutSeconds: 10);
        var (state, attempt) = Begin(policy);
        var reconciledAt = attempt.ExpiresAt.AddSeconds(1);
        var response = new EtlHttpDeliveryResponse(503, null)
        {
            ResponseReceivedAtUtc = attempt.ExpiresAt.AddTicks(-1)
        };

        var result = Complete(state, attempt, response, reconciledAt, policy);

        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        Assert.Equal(EtlHttpDeliveryPhase.Waiting, result.Delivery!.Phase);
        Assert.Equal(reconciledAt.AddSeconds(1), result.Delivery.NotBefore);
        Assert.Equal(reconciledAt, Assert.Single(result.Delivery.Attempts).CompletedAt);
        EtlHttpDeliveryTransitions.Validate(result.Delivery, EffectId, policy);
    }

    [Fact]
    public void Timely_transient_receipt_reconciled_after_the_run_deadline_cannot_schedule_another_attempt()
    {
        var policy = Policy(maxAttempts: 2, deadlineSeconds: 10, requestTimeoutSeconds: 10);
        var (state, attempt) = Begin(policy);
        var response = new EtlHttpDeliveryResponse(503, null)
        {
            ResponseReceivedAtUtc = attempt.ExpiresAt.AddTicks(-1)
        };

        var result = Complete(state, attempt, response, attempt.ExpiresAt.AddSeconds(1), policy);

        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        Assert.Equal(EtlHttpDeliveryPhase.Failed, result.Delivery!.Phase);
        Assert.Equal(EtlHttpDeliveryFailure.DeadlineExceeded, result.Delivery.Failure);
        Assert.Null(result.Delivery.NotBefore);
        Assert.Equal(response, Assert.Single(result.Delivery.Attempts).Response);
        EtlHttpDeliveryTransitions.Validate(result.Delivery, EffectId, policy);
    }

    [Theory]
    [InlineData("before-start")]
    [InlineData("at-expiry")]
    [InlineData("future")]
    [InlineData("non-utc")]
    public void Response_receipt_must_be_utc_and_inside_the_physical_attempt(string defect)
    {
        var (state, attempt) = Begin();
        var now = Start.AddSeconds(1);
        var receivedAt = defect switch
        {
            "before-start" => attempt.StartedAt.AddTicks(-1),
            "at-expiry" => attempt.ExpiresAt,
            "future" => now.AddTicks(1),
            "non-utc" => now.ToOffset(TimeSpan.FromHours(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };

        var result = Complete(state, attempt,
            new EtlHttpDeliveryResponse(200, null) { ResponseReceivedAtUtc = receivedAt }, now);

        Assert.Equal(EtlStoreStatus.Invalid, result.Status);
        Assert.Same(state, result.Delivery);
    }

    [Fact]
    public void A_receipt_timestamp_without_a_response_and_tampered_journal_receipts_are_rejected()
    {
        var (state, attempt) = Begin();
        var receiptWithoutStatus = Complete(state, attempt,
            new EtlHttpDeliveryResponse(null, null) { ResponseReceivedAtUtc = Start.AddMilliseconds(500) },
            Start.AddSeconds(1));
        var successful = Complete(state, attempt,
            new EtlHttpDeliveryResponse(200, null) { ResponseReceivedAtUtc = Start.AddMilliseconds(500) },
            Start.AddSeconds(1));
        var completed = Assert.Single(successful.Delivery!.Attempts);
        var futureReceipt = successful.Delivery with
        {
            Attempts = new[] { completed with
            {
                Response = completed.Response! with { ResponseReceivedAtUtc = completed.CompletedAt!.Value.AddTicks(1) }
            } }
        };
        var earlyReceipt = successful.Delivery with
        {
            Attempts = new[] { completed with
            {
                Response = completed.Response! with { ResponseReceivedAtUtc = attempt.StartedAt.AddTicks(-1) }
            } }
        };

        Assert.Equal(EtlStoreStatus.Invalid, receiptWithoutStatus.Status);
        Assert.Same(state, receiptWithoutStatus.Delivery);
        Assert.Throws<InvalidDataException>(() => EtlHttpDeliveryTransitions.Validate(futureReceipt, EffectId, Policy()));
        Assert.Throws<InvalidDataException>(() => EtlHttpDeliveryTransitions.Validate(earlyReceipt, EffectId, Policy()));
    }

    [Fact]
    public void Deadline_prevents_acknowledgement_even_when_the_response_is_successful()
    {
        var policy = Policy(maxAttempts: 2, deadlineSeconds: 10, requestTimeoutSeconds: 10);
        var (state, attempt) = Begin(policy: policy);

        var result = Complete(state, attempt, new(204, null), state.Deadline, policy: policy);

        Assert.Equal(EtlStoreStatus.Invalid, result.Status);
        Assert.Same(state, result.Delivery);
    }

    [Fact]
    public void Every_returned_attempt_journal_is_read_only_and_malformed_journals_are_rejected()
    {
        var (state, attempt) = Begin();
        var waiting = Complete(state, attempt, new(500, null), Start.AddSeconds(1)).Delivery!;
        var duplicateId = waiting with
        {
            Attempts = new[] { waiting.Attempts[0], waiting.Attempts[0] with { Attempt = waiting.Attempts[0].Attempt with { Number = 2 } } }
        };
        var brokenBackoff = waiting with { NotBefore = Start.AddSeconds(1) };
        var brokenDeadline = waiting with { Deadline = waiting.Deadline.AddSeconds(1) };
        var missingJournal = state with { Attempts = null! };
        var backwardsFence = waiting with
        {
            Attempts = new[]
            {
                waiting.Attempts[0] with { Attempt = waiting.Attempts[0].Attempt with { Fence = 8 } },
                waiting.Attempts[0] with { Attempt = waiting.Attempts[0].Attempt with { Number = 2, Fence = 7, RequestId = RequestId(2) } }
            }
        };

        Assert.IsAssignableFrom<System.Collections.ObjectModel.ReadOnlyCollection<EtlHttpDeliveryAttemptRecord>>(state.Attempts);
        Assert.Throws<NotSupportedException>(() => ((IList<EtlHttpDeliveryAttemptRecord>)state.Attempts).Add(state.Attempts[0]));
        Assert.Throws<InvalidDataException>(() => EtlHttpDeliveryTransitions.Validate(duplicateId, EffectId, Policy()));
        Assert.Throws<InvalidDataException>(() => EtlHttpDeliveryTransitions.Validate(brokenBackoff, EffectId, Policy()));
        Assert.Throws<InvalidDataException>(() => EtlHttpDeliveryTransitions.Validate(brokenDeadline, EffectId, Policy()));
        Assert.Throws<InvalidDataException>(() => EtlHttpDeliveryTransitions.Validate(backwardsFence, EffectId, Policy()));
        Assert.Throws<InvalidDataException>(() => Complete(missingJournal, attempt, new(200, null), Start.AddSeconds(1)));
    }

    [Fact]
    public void Completion_with_an_attempt_from_another_effect_conflicts_without_corrupting_state()
    {
        var (state, attempt) = Begin();
        var wrongEffect = attempt with { EffectId = "route-errors" };

        var result = EtlHttpDeliveryTransitions.Complete(state, wrongEffect, new(200, null), 7,
            Start.AddSeconds(1), Policy());

        Assert.Equal(EtlStoreStatus.Conflict, result.Status);
        Assert.Same(state, result.Delivery);
    }

    [Fact]
    public void Invalid_begin_arguments_return_invalid_without_throwing()
    {
        var badId = EtlHttpDeliveryTransitions.Begin(null, EffectId, "not-a-guid", 7, Start, Policy());
        var badFence = EtlHttpDeliveryTransitions.Begin(null, EffectId, RequestId(1), 0, Start, Policy());
        var badPolicy = EtlHttpDeliveryTransitions.Begin(null, EffectId, RequestId(1), 7, Start,
            Policy(requestTimeoutSeconds: 61));

        Assert.Equal(EtlStoreStatus.Invalid, badId.Status);
        Assert.Equal(EtlStoreStatus.Invalid, badFence.Status);
        Assert.Equal(EtlStoreStatus.Invalid, badPolicy.Status);
    }

    private static (EtlConnectorHttpDelivery State, EtlHttpDeliveryAttempt Permit) Begin(EtlHttpPolicy? policy = null)
    {
        var result = EtlHttpDeliveryTransitions.Begin(null, EffectId, RequestId(1), 7, Start, policy ?? Policy());
        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        return (result.Delivery!, result.Permit!);
    }

    private static EtlConnectorHttpDeliveryResult Begin(EtlConnectorHttpDelivery state, string requestId,
        DateTimeOffset now, long fence, EtlHttpPolicy policy) =>
        EtlHttpDeliveryTransitions.Begin(state, EffectId, requestId, fence, now, policy);

    private static EtlConnectorHttpDeliveryResult Complete(EtlConnectorHttpDelivery state, EtlHttpDeliveryAttempt attempt,
        EtlHttpDeliveryResponse response, DateTimeOffset now, EtlHttpPolicy? policy = null) =>
        EtlHttpDeliveryTransitions.Complete(state, attempt, response, attempt.Fence, now, policy ?? Policy());

    private static EtlHttpPolicy Policy(int maxAttempts = 5, int deadlineSeconds = 60, int requestTimeoutSeconds = 10,
        int initialDelaySeconds = 1, int maxDelaySeconds = 8) => new()
    {
        MaxAttempts = maxAttempts,
        DeadlineSeconds = deadlineSeconds,
        RequestTimeoutSeconds = requestTimeoutSeconds,
        InitialDelaySeconds = initialDelaySeconds,
        MaxDelaySeconds = maxDelaySeconds
    };

    private static string RequestId(int suffix) => suffix.ToString("x32");

    private const string EffectId = "route-main";
}
