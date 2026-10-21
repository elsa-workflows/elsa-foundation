using System.Collections.ObjectModel;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

/// <summary>Pure, bounded transitions for durable HTTP route delivery attempts.</summary>
internal static class EtlHttpDeliveryTransitions
{
    public static EtlConnectorHttpDeliveryResult Begin(EtlConnectorHttpDelivery? state, string effectId, string requestId,
        long fence, DateTimeOffset now, EtlHttpPolicy policy)
    {
        if (!ValidBeginArguments(effectId, requestId, fence, now, policy))
            return new(EtlStoreStatus.Invalid, state, null);

        if (state is null)
        {
            if (!EtlHttpRetryPolicy.TryGetDeadline(now, policy, out var deadline))
                return new(EtlStoreStatus.Invalid, null, null);

            var attempt = NewAttempt(requestId, 1, effectId, fence, now, deadline, policy);
            var created = new EtlConnectorHttpDelivery(deadline, Snapshot([new EtlHttpDeliveryAttemptRecord(attempt, null, null)]),
                EtlHttpDeliveryPhase.InFlight, null, EtlHttpDeliveryFailure.None, now);
            return new(EtlStoreStatus.Applied, created, attempt);
        }

        Validate(state, effectId, policy);

        var latest = state.Attempts[^1];
        var sameRequest = state.Attempts.FirstOrDefault(record => StringComparer.Ordinal.Equals(record.Attempt.RequestId, requestId));
        if (sameRequest is not null && (sameRequest.Attempt.Fence != fence || latest.Attempt.Fence != fence))
            return new(EtlStoreStatus.Conflict, state, null);

        if (IsTerminal(state.Phase))
            return new(EtlStoreStatus.Replay, state, null);

        if (now < latest.Attempt.StartedAt)
            return new(EtlStoreStatus.Invalid, state, null);

        if (sameRequest?.CompletedAt is not null)
            return new(EtlStoreStatus.Replay, state, null);

        if (state.Phase == EtlHttpDeliveryPhase.InFlight && sameRequest is not null &&
            sameRequest.Attempt.RequestId == latest.Attempt.RequestId && sameRequest.Attempt.Fence == fence &&
            now < latest.Attempt.ExpiresAt && now < state.Deadline)
            return new(EtlStoreStatus.Replay, state, latest.Attempt);

        if (fence < latest.Attempt.Fence)
            return new(EtlStoreStatus.Conflict, state, null);

        if (state.Phase == EtlHttpDeliveryPhase.Waiting)
        {
            if (now >= state.Deadline)
                return new(EtlStoreStatus.Applied, Fail(state, now, EtlHttpDeliveryFailure.DeadlineExceeded), null);

            if (now < state.NotBefore!.Value)
                return new(EtlStoreStatus.Conflict, state, null);

            // Validate guarantees a waiting journal has room for this attempt.
            var attempt = NewAttempt(requestId, state.Attempts.Count + 1, effectId, fence, now, state.Deadline, policy);
            var attempts = state.Attempts.Append(new EtlHttpDeliveryAttemptRecord(attempt, null, null));
            var inFlight = state with
            {
                Attempts = Snapshot(attempts),
                Phase = EtlHttpDeliveryPhase.InFlight,
                NotBefore = null,
                Failure = EtlHttpDeliveryFailure.None,
                UpdatedAt = now
            };
            return new(EtlStoreStatus.Applied, inFlight, attempt);
        }

        // The only remaining nonterminal phase is InFlight. A different request id under the
        // same live fence cannot race the already allocated physical attempt.
        if (state.Phase != EtlHttpDeliveryPhase.InFlight)
            return new(EtlStoreStatus.Invalid, state, null);

        if (now >= state.Deadline)
            return new(EtlStoreStatus.Applied, CloseUncertain(state, now, policy, deadlineReached: true), null);

        var current = latest.Attempt;
        if (fence == current.Fence && now < current.ExpiresAt)
            return new(EtlStoreStatus.Conflict, state, null);

        // Reusing the current unexpired request id under a replacement fence is not a new
        // idempotency key and must not retire the original attempt.
        if (sameRequest is not null && fence != current.Fence && now < current.ExpiresAt)
            return new(EtlStoreStatus.Conflict, state, null);

        return new(EtlStoreStatus.Applied, CloseUncertain(state, now, policy, deadlineReached: false), null);
    }

    public static EtlConnectorHttpDeliveryResult Complete(EtlConnectorHttpDelivery state, EtlHttpDeliveryAttempt attempt,
        EtlHttpDeliveryResponse response, long fence, DateTimeOffset now, EtlHttpPolicy policy)
    {
        if (state is null || attempt is null || !EtlHttpRetryPolicy.IsValid(policy) || fence <= 0 || !EtlHttpRetryPolicy.IsUtc(now) ||
            !ValidAttemptArgument(attempt) || !ValidResponseArgument(response))
            return new(EtlStoreStatus.Invalid, state, null);

        var stateEffectId = ReadStateEffectId(state);
        Validate(state, stateEffectId!, policy);
        if (!StringComparer.Ordinal.Equals(attempt.EffectId, stateEffectId))
            return new(EtlStoreStatus.Conflict, state, null);

        var record = state.Attempts.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Attempt.RequestId, attempt.RequestId));
        if (record is null || record.Attempt != attempt)
            return new(EtlStoreStatus.Conflict, state, null);

        var latest = state.Attempts[^1];
        if (record.CompletedAt is not null)
        {
            if (fence != attempt.Fence || attempt.Fence < latest.Attempt.Fence)
                return new(EtlStoreStatus.Conflict, state, null);

            return SameOutcome(record.Response, response)
                ? new(EtlStoreStatus.Replay, state, null)
                : new(EtlStoreStatus.Conflict, state, null);
        }

        if (state.Phase != EtlHttpDeliveryPhase.InFlight || latest.Attempt != attempt || attempt.Fence != fence)
            return new(EtlStoreStatus.Conflict, state, null);

        if (now < attempt.StartedAt)
            return new(EtlStoreStatus.Invalid, state, null);

        if (response.StatusCode is not null && now >= attempt.ExpiresAt)
            return new(EtlStoreStatus.Invalid, state, null);

        var storedResponse = response.StatusCode is null ? null : response;
        var completed = state with
        {
            Attempts = ReplaceLast(state.Attempts, latest with { Response = storedResponse, CompletedAt = now }),
            UpdatedAt = now
        };

        if (now >= state.Deadline)
            return new(EtlStoreStatus.Applied, Fail(completed, now, EtlHttpDeliveryFailure.DeadlineExceeded), null);

        if (IsAcknowledgement(response.StatusCode))
        {
            var acknowledged = completed with
            {
                Phase = EtlHttpDeliveryPhase.Acknowledged,
                NotBefore = null,
                Failure = EtlHttpDeliveryFailure.None
            };
            return new(EtlStoreStatus.Applied, acknowledged, null);
        }

        if (!EtlHttpRetryPolicy.IsRetryable(response.StatusCode))
        {
            var rejected = completed with
            {
                Phase = EtlHttpDeliveryPhase.Failed,
                NotBefore = null,
                Failure = EtlHttpDeliveryFailure.ResponseRejected
            };
            return new(EtlStoreStatus.Applied, rejected, null);
        }

        return new(EtlStoreStatus.Applied, ScheduleRetry(completed, now, policy), null);
    }

    public static void Validate(EtlConnectorHttpDelivery state, string effectId, EtlHttpPolicy policy)
    {
        try
        {
            ValidateCore(state, effectId, policy);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or
                                           NullReferenceException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("The persisted HTTP delivery journal is invalid.", exception);
        }
    }

    private static void ValidateCore(EtlConnectorHttpDelivery state, string effectId, EtlHttpPolicy policy)
    {
        if (state is null || !ValidEffectId(effectId) || !EtlHttpRetryPolicy.IsValid(policy) || state.Attempts is null ||
            state.Attempts.Count is < 1 || state.Attempts.Count > policy.MaxAttempts ||
            !EtlHttpRetryPolicy.IsUtc(state.Deadline) || !EtlHttpRetryPolicy.IsUtc(state.UpdatedAt) || !Enum.IsDefined(state.Phase) || !Enum.IsDefined(state.Failure))
            throw Corrupt();

        var attempts = state.Attempts;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var first = attempts[0].Attempt;
        if (!ValidAttemptArgument(first) || first.Number != 1 || first.EffectId != effectId)
            throw Corrupt();

        if (!EtlHttpRetryPolicy.TryGetDeadline(first.StartedAt, policy, out var expectedDeadline))
            throw Corrupt();
        if (state.Deadline != expectedDeadline || state.UpdatedAt < first.StartedAt)
            throw Corrupt();

        long previousFence = 0;
        DateTimeOffset previousStarted = DateTimeOffset.MinValue;
        for (var index = 0; index < attempts.Count; index++)
        {
            var item = attempts[index];
            if (item is null || item.Attempt is null || !ValidAttemptArgument(item.Attempt))
                throw Corrupt();

            var current = item.Attempt;
            var isLast = index == attempts.Count - 1;
            if (current.Number != index + 1 || current.EffectId != effectId ||
                !ids.Add(current.RequestId) || current.Fence < previousFence ||
                (index > 0 && current.StartedAt <= previousStarted) || current.StartedAt >= state.Deadline ||
                current.ExpiresAt != EtlHttpRetryPolicy.ClampExpiry(current.StartedAt, state.Deadline, policy))
                throw Corrupt();

            if (item.CompletedAt is not null)
            {
                if (!EtlHttpRetryPolicy.IsUtc(item.CompletedAt.Value) || item.CompletedAt.Value < current.StartedAt ||
                    item.Response is not null && (!ValidResponseArgument(item.Response) || item.CompletedAt.Value >= current.ExpiresAt))
                    throw Corrupt();
                if (state.UpdatedAt < item.CompletedAt.Value)
                    throw Corrupt();
            }
            else if (item.Response is not null)
                throw Corrupt();

            if (!isLast)
            {
                if (item.CompletedAt is null || item.CompletedAt.Value >= state.Deadline ||
                    current.Number >= policy.MaxAttempts || !EtlHttpRetryPolicy.IsRetryable(item.Response?.StatusCode))
                    throw Corrupt();

                var next = attempts[index + 1].Attempt;
                var retryAt = EtlHttpRetryPolicy.NextRetryAt(current.Number, item.CompletedAt.Value,
                    item.Response?.RetryAfter, policy);
                if (next.StartedAt < retryAt || next.StartedAt <= item.CompletedAt.Value || next.StartedAt >= state.Deadline)
                    throw Corrupt();
            }

            previousFence = current.Fence;
            previousStarted = current.StartedAt;
        }

        var last = attempts[^1];
        ValidatePhase(state, last, policy);
    }

    private static void ValidatePhase(EtlConnectorHttpDelivery state, EtlHttpDeliveryAttemptRecord last, EtlHttpPolicy policy)
    {
        var attempt = last.Attempt;
        switch (state.Phase)
        {
            case EtlHttpDeliveryPhase.InFlight:
                if (state.Failure != EtlHttpDeliveryFailure.None || state.NotBefore is not null ||
                    last.Response is not null || last.CompletedAt is not null || state.UpdatedAt != attempt.StartedAt)
                    throw Corrupt();
                break;

            case EtlHttpDeliveryPhase.Waiting:
                if (state.Failure != EtlHttpDeliveryFailure.None || state.NotBefore is null ||
                    !EtlHttpRetryPolicy.IsUtc(state.NotBefore.Value) || last.CompletedAt is null ||
                    !EtlHttpRetryPolicy.IsRetryable(last.Response?.StatusCode) || attempt.Number >= policy.MaxAttempts ||
                    last.CompletedAt.Value >= state.Deadline || state.UpdatedAt != last.CompletedAt.Value ||
                    state.NotBefore.Value != EtlHttpRetryPolicy.NextRetryAt(attempt.Number, last.CompletedAt.Value,
                        last.Response?.RetryAfter, policy) ||
                    state.NotBefore.Value >= state.Deadline)
                    throw Corrupt();
                break;

            case EtlHttpDeliveryPhase.Acknowledged:
                if (state.Failure != EtlHttpDeliveryFailure.None || state.NotBefore is not null ||
                    last.CompletedAt is null || !IsAcknowledgement(last.Response?.StatusCode) ||
                    last.CompletedAt.Value >= state.Deadline || state.UpdatedAt != last.CompletedAt.Value)
                    throw Corrupt();
                break;

            case EtlHttpDeliveryPhase.Failed:
                if (state.NotBefore is not null || last.CompletedAt is null)
                    throw Corrupt();

                switch (state.Failure)
                {
                    case EtlHttpDeliveryFailure.AttemptsExhausted:
                        if (attempt.Number != policy.MaxAttempts || !EtlHttpRetryPolicy.IsRetryable(last.Response?.StatusCode) ||
                            last.CompletedAt.Value >= state.Deadline || state.UpdatedAt != last.CompletedAt.Value)
                            throw Corrupt();
                        break;
                    case EtlHttpDeliveryFailure.DeadlineExceeded:
                        var canRetry = EtlHttpRetryPolicy.IsRetryable(last.Response?.StatusCode);
                        var retryAt = canRetry
                            ? EtlHttpRetryPolicy.NextRetryAt(attempt.Number, last.CompletedAt.Value,
                                last.Response?.RetryAfter, policy)
                            : DateTimeOffset.MinValue;
                        if (!(last.CompletedAt.Value >= state.Deadline ||
                              canRetry && (state.UpdatedAt >= state.Deadline || retryAt >= state.Deadline)))
                            throw Corrupt();
                        break;
                    case EtlHttpDeliveryFailure.ResponseRejected:
                        if (last.Response is null || EtlHttpRetryPolicy.IsRetryable(last.Response.StatusCode) || IsAcknowledgement(last.Response.StatusCode) ||
                            last.CompletedAt.Value >= state.Deadline || state.UpdatedAt != last.CompletedAt.Value)
                            throw Corrupt();
                        break;
                    default:
                        throw Corrupt();
                }
                break;

            default:
                throw Corrupt();
        }
    }

    private static EtlConnectorHttpDelivery CloseUncertain(EtlConnectorHttpDelivery state, DateTimeOffset now,
        EtlHttpPolicy policy, bool deadlineReached)
    {
        var last = state.Attempts[^1];
        var closed = state with
        {
            Attempts = ReplaceLast(state.Attempts, last with { Response = null, CompletedAt = now }),
            UpdatedAt = now
        };

        if (deadlineReached || now >= state.Deadline)
            return Fail(closed, now, EtlHttpDeliveryFailure.DeadlineExceeded);

        if (last.Attempt.Number >= policy.MaxAttempts)
            return Fail(closed, now, EtlHttpDeliveryFailure.AttemptsExhausted);

        return ScheduleRetry(closed, now, policy);
    }

    private static EtlConnectorHttpDelivery ScheduleRetry(EtlConnectorHttpDelivery state, DateTimeOffset completedAt,
        EtlHttpPolicy policy)
    {
        var last = state.Attempts[^1];
        if (last.Attempt.Number >= policy.MaxAttempts)
            return Fail(state, completedAt, EtlHttpDeliveryFailure.AttemptsExhausted);

        var retryAt = EtlHttpRetryPolicy.NextRetryAt(last.Attempt.Number, completedAt,
            last.Response?.RetryAfter, policy);
        if (retryAt >= state.Deadline)
            return Fail(state, completedAt, EtlHttpDeliveryFailure.DeadlineExceeded);

        return state with
        {
            Phase = EtlHttpDeliveryPhase.Waiting,
            NotBefore = retryAt,
            Failure = EtlHttpDeliveryFailure.None,
            UpdatedAt = completedAt
        };
    }

    private static EtlConnectorHttpDelivery Fail(EtlConnectorHttpDelivery state, DateTimeOffset now,
        EtlHttpDeliveryFailure failure) => state with
    {
        Phase = EtlHttpDeliveryPhase.Failed,
        NotBefore = null,
        Failure = failure,
        UpdatedAt = now
    };

    private static EtlHttpDeliveryAttempt NewAttempt(string requestId, int number, string effectId, long fence,
        DateTimeOffset startedAt, DateTimeOffset deadline, EtlHttpPolicy policy) =>
        new(requestId, number, fence, effectId, startedAt, EtlHttpRetryPolicy.ClampExpiry(startedAt, deadline, policy));

    private static bool ValidBeginArguments(string effectId, string requestId, long fence, DateTimeOffset now,
        EtlHttpPolicy policy) => ValidEffectId(effectId) && EtlHttpRetryPolicy.IsAttemptId(requestId) && fence > 0 &&
                                 EtlHttpRetryPolicy.IsUtc(now) && EtlHttpRetryPolicy.IsValid(policy);

    private static bool ValidAttemptArgument(EtlHttpDeliveryAttempt? attempt) => attempt is not null &&
        EtlHttpRetryPolicy.IsAttemptId(attempt.RequestId) && attempt.Number > 0 && attempt.Fence > 0 && ValidEffectId(attempt.EffectId) &&
        EtlHttpRetryPolicy.IsUtc(attempt.StartedAt) && EtlHttpRetryPolicy.IsUtc(attempt.ExpiresAt) && attempt.ExpiresAt >= attempt.StartedAt;

    private static bool ValidResponseArgument(EtlHttpDeliveryResponse? response) => response is not null &&
        (response.StatusCode is null || response.StatusCode is >= 100 and <= 599) &&
        (response.RetryAfter is null || EtlHttpRetryPolicy.IsUtc(response.RetryAfter.Value)) &&
        (response.StatusCode is not null || response.RetryAfter is null);

    private static string? ReadStateEffectId(EtlConnectorHttpDelivery state)
    {
        if (state.Attempts is not { Count: > 0 } || state.Attempts[0] is not { Attempt: { } firstAttempt })
            return null;
        return firstAttempt.EffectId;
    }

    private static bool ValidEffectId(string? value)
    {
        try
        {
            EtlRuntimeRecordCodec.ValidateIdentity(value, "HTTP effect id");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsAcknowledgement(int? statusCode) => statusCode is 200 or 201 or 204;

    private static bool SameOutcome(EtlHttpDeliveryResponse? recorded, EtlHttpDeliveryResponse supplied) =>
        recorded is null ? supplied.StatusCode is null && supplied.RetryAfter is null : recorded == supplied;

    private static bool IsTerminal(EtlHttpDeliveryPhase phase) =>
        phase is EtlHttpDeliveryPhase.Acknowledged or EtlHttpDeliveryPhase.Failed;

    private static IReadOnlyList<EtlHttpDeliveryAttemptRecord> Snapshot(IEnumerable<EtlHttpDeliveryAttemptRecord> attempts) =>
        new ReadOnlyCollection<EtlHttpDeliveryAttemptRecord>(attempts.ToArray());

    private static IReadOnlyList<EtlHttpDeliveryAttemptRecord> ReplaceLast(
        IReadOnlyList<EtlHttpDeliveryAttemptRecord> attempts, EtlHttpDeliveryAttemptRecord replacement)
    {
        var copy = attempts.ToArray();
        copy[^1] = replacement;
        return new ReadOnlyCollection<EtlHttpDeliveryAttemptRecord>(copy);
    }

    private static InvalidDataException Corrupt() => new("The persisted HTTP delivery journal is invalid.");
}
