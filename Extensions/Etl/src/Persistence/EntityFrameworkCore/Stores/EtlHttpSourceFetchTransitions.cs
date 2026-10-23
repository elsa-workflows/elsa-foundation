using System.Collections.ObjectModel;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

/// <summary>Pure, bounded transitions for retrying and capturing one HTTP source request.</summary>
internal static class EtlHttpSourceFetchTransitions
{
    internal static EtlConnectorHttpSourceFetchResult Begin(EtlConnectorHttpSourceFetch? fetch, string requestHash,
        long pageSequence, string requestId, long fence, DateTimeOffset now, EtlHttpPolicy policy)
    {
        if (!ValidBeginArguments(requestHash, pageSequence, requestId, fence, now, policy))
            return new(EtlStoreStatus.Invalid, fetch, null);

        if (fetch is null)
        {
            if (!EtlHttpRetryPolicy.TryGetDeadline(now, policy, out var deadline))
                return new(EtlStoreStatus.Invalid, null, null);

            var attempt = NewAttempt(requestId, 1, fence, requestHash, now, deadline, policy);
            var created = new EtlConnectorHttpSourceFetch(requestHash, pageSequence, deadline,
                Snapshot([new EtlHttpSourceFetchAttemptRecord(attempt, null, null)]),
                EtlConnectorHttpSourceFetchPhase.InFlight, null, EtlConnectorHttpSourceFetchFailure.None, now, null);
            return new(EtlStoreStatus.Applied, created, attempt);
        }

        Validate(fetch, policy);
        if (!StringComparer.Ordinal.Equals(fetch.RequestHash, requestHash) || fetch.PageSequence != pageSequence)
            return new(EtlStoreStatus.Conflict, fetch, null);

        var latest = fetch.Attempts[^1];
        var sameRequest = fetch.Attempts.FirstOrDefault(record =>
            StringComparer.Ordinal.Equals(record.Attempt.RequestId, requestId));
        if (sameRequest is not null && (sameRequest.Attempt.Fence != fence || latest.Attempt.Fence != fence))
            return new(EtlStoreStatus.Conflict, fetch, null);

        if (fetch.Phase is EtlConnectorHttpSourceFetchPhase.Captured or EtlConnectorHttpSourceFetchPhase.Failed)
            return new(EtlStoreStatus.Replay, fetch, null);

        if (now < latest.Attempt.StartedAt)
            return new(EtlStoreStatus.Invalid, fetch, null);

        if (sameRequest?.CompletedAt is not null)
            return new(EtlStoreStatus.Replay, fetch, null);

        if (fetch.Phase == EtlConnectorHttpSourceFetchPhase.InFlight && sameRequest is not null &&
            sameRequest.Attempt == latest.Attempt && sameRequest.Attempt.Fence == fence &&
            now < latest.Attempt.ExpiresAt && now < fetch.Deadline)
            return new(EtlStoreStatus.Replay, fetch, latest.Attempt);

        if (fence < latest.Attempt.Fence)
            return new(EtlStoreStatus.Conflict, fetch, null);

        if (fetch.Phase == EtlConnectorHttpSourceFetchPhase.Waiting)
        {
            if (now >= fetch.Deadline)
                return new(EtlStoreStatus.Applied, Fail(fetch, now, EtlConnectorHttpSourceFetchFailure.DeadlineExceeded), null);

            if (now < fetch.NotBefore!.Value)
                return new(EtlStoreStatus.Conflict, fetch, null);

            var attempt = NewAttempt(requestId, fetch.Attempts.Count + 1, fence, requestHash, now, fetch.Deadline, policy);
            var inFlight = fetch with
            {
                Attempts = Snapshot(fetch.Attempts.Append(new EtlHttpSourceFetchAttemptRecord(attempt, null, null))),
                Phase = EtlConnectorHttpSourceFetchPhase.InFlight,
                NotBefore = null,
                Failure = EtlConnectorHttpSourceFetchFailure.None,
                UpdatedAt = now
            };
            return new(EtlStoreStatus.Applied, inFlight, attempt);
        }

        if (fetch.Phase != EtlConnectorHttpSourceFetchPhase.InFlight)
            return new(EtlStoreStatus.Invalid, fetch, null);

        if (now >= fetch.Deadline)
            return new(EtlStoreStatus.Applied, CloseUncertain(fetch, now, policy, deadlineReached: true), null);

        var current = latest.Attempt;
        if (fence == current.Fence && now < current.ExpiresAt)
            return new(EtlStoreStatus.Conflict, fetch, null);

        if (sameRequest is not null && fence != current.Fence && now < current.ExpiresAt)
            return new(EtlStoreStatus.Conflict, fetch, null);

        return new(EtlStoreStatus.Applied, CloseUncertain(fetch, now, policy, deadlineReached: false), null);
    }

    internal static EtlConnectorHttpSourceFetchResult Complete(EtlConnectorHttpSourceFetch fetch,
        EtlHttpSourceFetchAttempt attempt, EtlHttpSourceFetchOutcome outcome, long fence, DateTimeOffset now,
        EtlHttpPolicy policy)
    {
        if (fetch is null || attempt is null || outcome is null || !ValidFetchPolicy(policy) || fence <= 0 ||
            !EtlHttpRetryPolicy.IsUtc(now) || !ValidAttemptArgument(attempt) || !ValidOutcomeArgument(outcome))
            return new(EtlStoreStatus.Invalid, fetch, null);

        Validate(fetch, policy);
        if (!StringComparer.Ordinal.Equals(attempt.RequestHash, fetch.RequestHash))
            return new(EtlStoreStatus.Conflict, fetch, null);

        var record = fetch.Attempts.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Attempt.RequestId, attempt.RequestId));
        if (record is null || record.Attempt != attempt)
            return new(EtlStoreStatus.Conflict, fetch, null);

        var latest = fetch.Attempts[^1];
        if (record.CompletedAt is not null)
        {
            if (fence != attempt.Fence || attempt.Fence < latest.Attempt.Fence)
                return new(EtlStoreStatus.Conflict, fetch, null);

            return SameOutcome(record.Outcome, outcome)
                ? new(EtlStoreStatus.Replay, fetch, null)
                : new(EtlStoreStatus.Conflict, fetch, null);
        }

        if (fetch.Phase != EtlConnectorHttpSourceFetchPhase.InFlight || latest.Attempt != attempt || attempt.Fence != fence)
            return new(EtlStoreStatus.Conflict, fetch, null);

        if (now < attempt.StartedAt)
            return new(EtlStoreStatus.Invalid, fetch, null);

        var hasTimelyInvalidReceipt = IsTimelyInvalidReceipt(outcome, attempt, fetch.Deadline, now);
        if (outcome.ResponseReceivedAtUtc is not null && !hasTimelyInvalidReceipt)
            return new(EtlStoreStatus.Invalid, fetch, null);

        if (outcome.StatusCode is not null && now >= attempt.ExpiresAt && !hasTimelyInvalidReceipt)
            return new(EtlStoreStatus.Invalid, fetch, null);

        // A valid 200 is captured through Capture so its durable receipt is tied to the exact page.
        if (outcome.StatusCode == 200 && !outcome.InvalidPage)
            return new(EtlStoreStatus.Invalid, fetch, null);

        var storedOutcome = outcome.StatusCode is null ? null : outcome;
        var completed = fetch with
        {
            Attempts = ReplaceLast(fetch.Attempts, latest with { Outcome = storedOutcome, CompletedAt = now }),
            UpdatedAt = now
        };

        // A known-invalid page with a timely complete-response receipt remains terminal even when
        // parsing or persistence reconciliation crosses the source deadline.
        if (outcome.InvalidPage && hasTimelyInvalidReceipt)
            return new(EtlStoreStatus.Applied, Fail(completed, now, EtlConnectorHttpSourceFetchFailure.InvalidPage), null);

        if (now >= fetch.Deadline)
            return new(EtlStoreStatus.Applied, Fail(completed, now, EtlConnectorHttpSourceFetchFailure.DeadlineExceeded), null);

        if (outcome.InvalidPage)
            return new(EtlStoreStatus.Applied, Fail(completed, now, EtlConnectorHttpSourceFetchFailure.InvalidPage), null);

        if (!EtlHttpRetryPolicy.IsRetryable(outcome.StatusCode))
            return new(EtlStoreStatus.Applied, Fail(completed, now, EtlConnectorHttpSourceFetchFailure.ResponseRejected), null);

        return new(EtlStoreStatus.Applied, ScheduleRetry(completed, now, policy), null);
    }

    internal static EtlConnectorHttpSourceFetchResult Capture(EtlConnectorHttpSourceFetch fetch,
        EtlHttpSourceFetchAttempt attempt, EtlRetainedHttpPage page, DateTimeOffset responseReceivedAtUtc,
        DateTimeOffset now, EtlHttpPolicy policy)
    {
        if (fetch is null || attempt is null || page is null || !ValidFetchPolicy(policy) ||
            !EtlHttpRetryPolicy.IsUtc(responseReceivedAtUtc) || !EtlHttpRetryPolicy.IsUtc(now) ||
            !ValidAttemptArgument(attempt) || !ValidPageArgument(page, policy))
            return new(EtlStoreStatus.Invalid, fetch, null);

        Validate(fetch, policy);
        if (!StringComparer.Ordinal.Equals(attempt.RequestHash, fetch.RequestHash) ||
            !StringComparer.Ordinal.Equals(page.RequestHash, fetch.RequestHash) || page.PageSequence != fetch.PageSequence)
            return new(EtlStoreStatus.Conflict, fetch, null);

        var record = fetch.Attempts.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Attempt.RequestId, attempt.RequestId));
        if (record is null || record.Attempt != attempt)
            return new(EtlStoreStatus.Conflict, fetch, null);

        var desiredCapture = new EtlHttpSourceFetchCapture(page, attempt.RequestId, attempt.Fence, responseReceivedAtUtc);
        if (fetch.Phase == EtlConnectorHttpSourceFetchPhase.Captured)
            return fetch.Capture == desiredCapture
                ? new(EtlStoreStatus.Replay, fetch, null)
                : new(EtlStoreStatus.Conflict, fetch, null);

        if (record.Outcome is not null)
            return new(EtlStoreStatus.Conflict, fetch, null);

        if (responseReceivedAtUtc < attempt.StartedAt || responseReceivedAtUtc >= attempt.ExpiresAt ||
            responseReceivedAtUtc >= fetch.Deadline || responseReceivedAtUtc > now || now < fetch.UpdatedAt)
            return new(EtlStoreStatus.Invalid, fetch, null);

        var captured = fetch with
        {
            Attempts = Snapshot(fetch.Attempts),
            Phase = EtlConnectorHttpSourceFetchPhase.Captured,
            NotBefore = null,
            Failure = EtlConnectorHttpSourceFetchFailure.None,
            UpdatedAt = now,
            Capture = desiredCapture
        };
        return new(EtlStoreStatus.Applied, captured, null);
    }

    internal static void Validate(EtlConnectorHttpSourceFetch fetch, EtlHttpPolicy policy)
    {
        try
        {
            ValidateCore(fetch, policy);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or
                                           NullReferenceException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("The persisted HTTP source-fetch journal is invalid.", exception);
        }
    }

    private static void ValidateCore(EtlConnectorHttpSourceFetch fetch, EtlHttpPolicy policy)
    {
        if (fetch is null || !ValidRequestHash(fetch.RequestHash) || !ValidFetchPolicy(policy) ||
            fetch.PageSequence < 0 || fetch.PageSequence >= policy.MaxPagesPerPartition || fetch.Attempts is null ||
            fetch.Attempts.Count is < 1 || fetch.Attempts.Count > policy.MaxAttempts ||
            !EtlHttpRetryPolicy.IsUtc(fetch.Deadline) || !EtlHttpRetryPolicy.IsUtc(fetch.UpdatedAt) ||
            !Enum.IsDefined(fetch.Phase) || !Enum.IsDefined(fetch.Failure))
            throw Corrupt();

        var attempts = fetch.Attempts;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var first = attempts[0].Attempt;
        if (!ValidAttemptArgument(first) || first.Number != 1 || first.RequestHash != fetch.RequestHash)
            throw Corrupt();

        if (!EtlHttpRetryPolicy.TryGetDeadline(first.StartedAt, policy, out var expectedDeadline))
            throw Corrupt();
        if (fetch.Deadline != expectedDeadline || fetch.UpdatedAt < first.StartedAt)
            throw Corrupt();

        long previousFence = 0;
        var previousStarted = DateTimeOffset.MinValue;
        for (var index = 0; index < attempts.Count; index++)
        {
            var item = attempts[index];
            if (item is null || item.Attempt is null || !ValidAttemptArgument(item.Attempt))
                throw Corrupt();

            var current = item.Attempt;
            var isLast = index == attempts.Count - 1;
            if (current.Number != index + 1 || current.RequestHash != fetch.RequestHash ||
                !ids.Add(current.RequestId) || current.Fence < previousFence ||
                (index > 0 && current.StartedAt <= previousStarted) || current.StartedAt >= fetch.Deadline ||
                current.ExpiresAt != EtlHttpRetryPolicy.ClampExpiry(current.StartedAt, fetch.Deadline, policy))
                throw Corrupt();

            if (item.CompletedAt is not null)
            {
                if (!EtlHttpRetryPolicy.IsUtc(item.CompletedAt.Value) || item.CompletedAt.Value < current.StartedAt ||
                    item.Outcome is not null && (!ValidOutcomeArgument(item.Outcome) || item.Outcome.StatusCode is null ||
                                                 item.Outcome.StatusCode == 200 && !item.Outcome.InvalidPage ||
                                                 item.Outcome.ResponseReceivedAtUtc is not null &&
                                                 !IsTimelyInvalidReceipt(item.Outcome, current, fetch.Deadline, item.CompletedAt.Value) ||
                                                 item.CompletedAt.Value >= current.ExpiresAt &&
                                                 !IsTimelyInvalidReceipt(item.Outcome, current, fetch.Deadline, item.CompletedAt.Value)))
                    throw Corrupt();
                if (fetch.UpdatedAt < item.CompletedAt.Value)
                    throw Corrupt();
            }
            else if (item.Outcome is not null)
                throw Corrupt();

            if (!isLast)
            {
                if (item.CompletedAt is null || item.CompletedAt.Value >= fetch.Deadline ||
                    current.Number >= policy.MaxAttempts || !EtlHttpRetryPolicy.IsRetryable(item.Outcome?.StatusCode))
                    throw Corrupt();

                var next = attempts[index + 1].Attempt;
                var retryAt = EtlHttpRetryPolicy.NextRetryAt(current.Number, item.CompletedAt.Value,
                    item.Outcome?.RetryAfter, policy);
                if (next.StartedAt < retryAt || next.StartedAt <= item.CompletedAt.Value || next.StartedAt >= fetch.Deadline)
                    throw Corrupt();
            }

            previousFence = current.Fence;
            previousStarted = current.StartedAt;
        }

        var last = attempts[^1];
        ValidateCapture(fetch, policy);
        ValidatePhase(fetch, last, policy);
    }

    private static void ValidateCapture(EtlConnectorHttpSourceFetch fetch, EtlHttpPolicy policy)
    {
        if (fetch.Phase != EtlConnectorHttpSourceFetchPhase.Captured)
        {
            if (fetch.Capture is not null)
                throw Corrupt();
            return;
        }

        var capture = fetch.Capture;
        if (capture is null || !ValidPageArgument(capture.Page, policy) ||
            !StringComparer.Ordinal.Equals(capture.Page.RequestHash, fetch.RequestHash) ||
            capture.Page.PageSequence != fetch.PageSequence ||
            !EtlHttpRetryPolicy.IsAttemptId(capture.RequestId) || capture.Fence <= 0 ||
            !EtlHttpRetryPolicy.IsUtc(capture.ResponseReceivedAtUtc) || fetch.UpdatedAt < capture.ResponseReceivedAtUtc)
            throw Corrupt();

        var record = fetch.Attempts.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Attempt.RequestId, capture.RequestId));
        if (record is null || record.Attempt.Fence != capture.Fence || record.Outcome is not null ||
            capture.ResponseReceivedAtUtc < record.Attempt.StartedAt ||
            capture.ResponseReceivedAtUtc >= record.Attempt.ExpiresAt || capture.ResponseReceivedAtUtc >= fetch.Deadline)
            throw Corrupt();
    }

    private static void ValidatePhase(EtlConnectorHttpSourceFetch fetch, EtlHttpSourceFetchAttemptRecord last,
        EtlHttpPolicy policy)
    {
        var attempt = last.Attempt;
        switch (fetch.Phase)
        {
            case EtlConnectorHttpSourceFetchPhase.InFlight:
                if (fetch.Failure != EtlConnectorHttpSourceFetchFailure.None || fetch.NotBefore is not null ||
                    fetch.Capture is not null || last.Outcome is not null || last.CompletedAt is not null ||
                    fetch.UpdatedAt != attempt.StartedAt)
                    throw Corrupt();
                break;

            case EtlConnectorHttpSourceFetchPhase.Waiting:
                if (fetch.Failure != EtlConnectorHttpSourceFetchFailure.None || fetch.NotBefore is null ||
                    fetch.Capture is not null || !EtlHttpRetryPolicy.IsUtc(fetch.NotBefore.Value) || last.CompletedAt is null ||
                    !EtlHttpRetryPolicy.IsRetryable(last.Outcome?.StatusCode) || attempt.Number >= policy.MaxAttempts ||
                    last.CompletedAt.Value >= fetch.Deadline || fetch.UpdatedAt != last.CompletedAt.Value ||
                    fetch.NotBefore.Value != EtlHttpRetryPolicy.NextRetryAt(attempt.Number, last.CompletedAt.Value,
                        last.Outcome?.RetryAfter, policy) || fetch.NotBefore.Value >= fetch.Deadline)
                    throw Corrupt();
                break;

            case EtlConnectorHttpSourceFetchPhase.Failed:
                if (fetch.NotBefore is not null || fetch.Capture is not null || last.CompletedAt is null)
                    throw Corrupt();

                switch (fetch.Failure)
                {
                    case EtlConnectorHttpSourceFetchFailure.AttemptsExhausted:
                        if (attempt.Number != policy.MaxAttempts || !EtlHttpRetryPolicy.IsRetryable(last.Outcome?.StatusCode) ||
                            last.CompletedAt.Value >= fetch.Deadline || fetch.UpdatedAt != last.CompletedAt.Value)
                            throw Corrupt();
                        break;
                    case EtlConnectorHttpSourceFetchFailure.DeadlineExceeded:
                        var canRetry = EtlHttpRetryPolicy.IsRetryable(last.Outcome?.StatusCode);
                        var retryAt = canRetry
                            ? EtlHttpRetryPolicy.NextRetryAt(attempt.Number, last.CompletedAt.Value, last.Outcome?.RetryAfter, policy)
                            : DateTimeOffset.MinValue;
                        if (!(last.CompletedAt.Value >= fetch.Deadline ||
                              canRetry && (fetch.UpdatedAt >= fetch.Deadline || retryAt >= fetch.Deadline)))
                            throw Corrupt();
                        break;
                    case EtlConnectorHttpSourceFetchFailure.ResponseRejected:
                        if (last.Outcome is null || last.Outcome.StatusCode is null ||
                            EtlHttpRetryPolicy.IsRetryable(last.Outcome.StatusCode) || last.Outcome.StatusCode == 200 ||
                            last.CompletedAt.Value >= fetch.Deadline || fetch.UpdatedAt != last.CompletedAt.Value)
                            throw Corrupt();
                        break;
                    case EtlConnectorHttpSourceFetchFailure.InvalidPage:
                        if (last.Outcome is not { StatusCode: 200, InvalidPage: true } ||
                            last.CompletedAt.Value >= fetch.Deadline &&
                            !IsTimelyInvalidReceipt(last.Outcome, attempt, fetch.Deadline, last.CompletedAt.Value) ||
                            fetch.UpdatedAt != last.CompletedAt.Value)
                            throw Corrupt();
                        break;
                    default:
                        throw Corrupt();
                }
                break;

            case EtlConnectorHttpSourceFetchPhase.Captured:
                if (fetch.Failure != EtlConnectorHttpSourceFetchFailure.None || fetch.NotBefore is not null ||
                    fetch.Capture is null || fetch.UpdatedAt < attempt.StartedAt)
                    throw Corrupt();
                break;

            default:
                throw Corrupt();
        }
    }

    private static EtlConnectorHttpSourceFetch CloseUncertain(EtlConnectorHttpSourceFetch fetch, DateTimeOffset now,
        EtlHttpPolicy policy, bool deadlineReached)
    {
        var last = fetch.Attempts[^1];
        var closed = fetch with
        {
            Attempts = ReplaceLast(fetch.Attempts, last with { Outcome = null, CompletedAt = now }),
            UpdatedAt = now
        };

        if (deadlineReached || now >= fetch.Deadline)
            return Fail(closed, now, EtlConnectorHttpSourceFetchFailure.DeadlineExceeded);
        if (last.Attempt.Number >= policy.MaxAttempts)
            return Fail(closed, now, EtlConnectorHttpSourceFetchFailure.AttemptsExhausted);
        return ScheduleRetry(closed, now, policy);
    }

    private static EtlConnectorHttpSourceFetch ScheduleRetry(EtlConnectorHttpSourceFetch fetch, DateTimeOffset completedAt,
        EtlHttpPolicy policy)
    {
        var last = fetch.Attempts[^1];
        if (last.Attempt.Number >= policy.MaxAttempts)
            return Fail(fetch, completedAt, EtlConnectorHttpSourceFetchFailure.AttemptsExhausted);

        var retryAt = EtlHttpRetryPolicy.NextRetryAt(last.Attempt.Number, completedAt, last.Outcome?.RetryAfter, policy);
        if (retryAt >= fetch.Deadline)
            return Fail(fetch, completedAt, EtlConnectorHttpSourceFetchFailure.DeadlineExceeded);

        return fetch with
        {
            Phase = EtlConnectorHttpSourceFetchPhase.Waiting,
            NotBefore = retryAt,
            Failure = EtlConnectorHttpSourceFetchFailure.None,
            UpdatedAt = completedAt
        };
    }

    private static EtlConnectorHttpSourceFetch Fail(EtlConnectorHttpSourceFetch fetch, DateTimeOffset now,
        EtlConnectorHttpSourceFetchFailure failure) => fetch with
    {
        Attempts = Snapshot(fetch.Attempts),
        Phase = EtlConnectorHttpSourceFetchPhase.Failed,
        NotBefore = null,
        Failure = failure,
        UpdatedAt = now
    };

    private static EtlHttpSourceFetchAttempt NewAttempt(string requestId, int number, long fence, string requestHash,
        DateTimeOffset startedAt, DateTimeOffset deadline, EtlHttpPolicy policy) =>
        new(requestId, number, fence, requestHash, startedAt,
            EtlHttpRetryPolicy.ClampExpiry(startedAt, deadline, policy));

    private static bool ValidBeginArguments(string requestHash, long pageSequence, string requestId, long fence, DateTimeOffset now,
        EtlHttpPolicy policy) => ValidRequestHash(requestHash) && ValidFetchPolicy(policy) && pageSequence >= 0 &&
                                 pageSequence < policy.MaxPagesPerPartition && EtlHttpRetryPolicy.IsAttemptId(requestId) && fence > 0 &&
                                 EtlHttpRetryPolicy.IsUtc(now);

    private static bool ValidAttemptArgument(EtlHttpSourceFetchAttempt? attempt) => attempt is not null &&
        EtlHttpRetryPolicy.IsAttemptId(attempt.RequestId) && attempt.Number > 0 && attempt.Fence > 0 &&
        ValidRequestHash(attempt.RequestHash) && EtlHttpRetryPolicy.IsUtc(attempt.StartedAt) &&
        EtlHttpRetryPolicy.IsUtc(attempt.ExpiresAt) && attempt.ExpiresAt >= attempt.StartedAt;

    private static bool ValidOutcomeArgument(EtlHttpSourceFetchOutcome? outcome) => outcome is not null &&
        (outcome.StatusCode is null || outcome.StatusCode is >= 100 and <= 599) &&
        (outcome.RetryAfter is null || EtlHttpRetryPolicy.IsUtc(outcome.RetryAfter.Value)) &&
        (outcome.StatusCode is not null || outcome.RetryAfter is null && !outcome.InvalidPage) &&
        (!outcome.InvalidPage || outcome.StatusCode == 200) &&
        (outcome.ResponseReceivedAtUtc is null ||
         outcome.StatusCode == 200 && outcome.InvalidPage && EtlHttpRetryPolicy.IsUtc(outcome.ResponseReceivedAtUtc.Value));

    private static bool IsTimelyInvalidReceipt(EtlHttpSourceFetchOutcome outcome, EtlHttpSourceFetchAttempt attempt,
        DateTimeOffset deadline, DateTimeOffset reconciledAt) =>
        outcome.StatusCode == 200 && outcome.InvalidPage && outcome.ResponseReceivedAtUtc is { } receivedAt &&
        EtlHttpRetryPolicy.IsUtc(receivedAt) && receivedAt >= attempt.StartedAt && receivedAt < attempt.ExpiresAt &&
        receivedAt < deadline && receivedAt <= reconciledAt;

    private static bool ValidRequestHash(string? value) => EtlRuntimeRecordCodec.IsSha256(value) &&
        StringComparer.Ordinal.Equals(value, value!.ToLowerInvariant());

    private static bool ValidFetchPolicy(EtlHttpPolicy? policy) => policy is not null && EtlHttpRetryPolicy.IsValid(policy) &&
        policy.MaxPagesPerPartition > 0 && policy.MaxPageBytes > 0 && policy.MaxPageRecords > 0 &&
        policy.MaxContinuationBytes > 0 && policy.MaxContinuationBytes <= EtlHttpPolicy.MaximumSupportedContinuationBytes &&
        policy.MaxRetainedInputBytes > 0 && policy.MaxRetainedMetadataBytes > 0 &&
        policy.MaxPageBytes <= policy.MaxRetainedInputBytes && policy.MaxContinuationBytes <= policy.MaxPageBytes;

    private static bool ValidPageArgument(EtlRetainedHttpPage? page, EtlHttpPolicy policy)
    {
        if (page is null || page.PageSequence < 0 || page.PageSequence >= policy.MaxPagesPerPartition ||
            !ValidRequestHash(page.RequestHash) || page.RecordCount < 0 || page.RecordCount > policy.MaxPageRecords ||
            page.Body is null || page.Body.ByteLength <= 0 || page.Body.ByteLength > policy.MaxPageBytes ||
            page.NextContinuation == string.Empty)
            return false;

        try
        {
            EtlRuntimeRecordCodec.ValidateObject(page.Body, "HTTP source page");
            if (page.NextContinuation is { } continuation &&
                Encoding.UTF8.GetByteCount(continuation) > policy.MaxContinuationBytes)
                return false;
            var json = EtlConnectorRecordCodec.Write(page);
            return Encoding.UTF8.GetByteCount(json) <= policy.MaxRetainedMetadataBytes;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or OverflowException or NullReferenceException)
        {
            return false;
        }
    }

    private static bool SameOutcome(EtlHttpSourceFetchOutcome? recorded, EtlHttpSourceFetchOutcome supplied) =>
        recorded is null
            ? supplied.StatusCode is null && supplied.RetryAfter is null && !supplied.InvalidPage
            : recorded == supplied;

    private static IReadOnlyList<EtlHttpSourceFetchAttemptRecord> Snapshot(IEnumerable<EtlHttpSourceFetchAttemptRecord> attempts) =>
        new ReadOnlyCollection<EtlHttpSourceFetchAttemptRecord>(attempts.ToArray());

    private static IReadOnlyList<EtlHttpSourceFetchAttemptRecord> ReplaceLast(
        IReadOnlyList<EtlHttpSourceFetchAttemptRecord> attempts, EtlHttpSourceFetchAttemptRecord replacement)
    {
        var copy = attempts.ToArray();
        copy[^1] = replacement;
        return new ReadOnlyCollection<EtlHttpSourceFetchAttemptRecord>(copy);
    }

    private static InvalidDataException Corrupt() => new("The persisted HTTP source-fetch journal is invalid.");
}
