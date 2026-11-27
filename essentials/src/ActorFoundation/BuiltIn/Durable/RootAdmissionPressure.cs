using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Durable;

internal enum RootAdmissionKind { Start, Input, ShedLoad }

/// <summary>One receiving node's limits. The worker publishes backlog separately from incoming requests.</summary>
internal sealed class RootAdmissionPressure
{
    private sealed class Bucket(double tokens, long at)
    {
        internal double Tokens { get; set; } = tokens;
        internal long At { get; set; } = at;
    }

    private readonly object _sync = new();
    private readonly Dictionary<(string Tenant, RootAdmissionKind Kind), Bucket> _buckets = [];
    private readonly SemaphoreSlim _ordinary;
    private readonly SemaphoreSlim _operators;
    private readonly TimeProvider _time;
    private readonly Rate _ordinaryRate;
    private readonly Rate _operatorRate;
    private HashSet<int> _full = [];
    private int _nodes = 1;

    internal sealed record Rate(int PerSecond, int Burst);

    internal static RootAdmissionPressure CreateDefault() => new(
        new(10_000, 8_000, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
        ordinaryCapacity: 256, operatorCapacity: 32, ordinaryRate: new(1_000, 1_000), operatorRate: new(100, 100));

    internal RootAdmissionPressure(BacklogOptions backlog, int ordinaryCapacity, int operatorCapacity,
        Rate ordinaryRate, Rate operatorRate, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ordinaryCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(operatorCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ordinaryRate.PerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ordinaryRate.Burst);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(operatorRate.PerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(operatorRate.Burst);
        ArgumentOutOfRangeException.ThrowIfNegative(backlog.LowWater);
        ArgumentOutOfRangeException.ThrowIfLessThan(backlog.HighWater, backlog.LowWater);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(backlog.SampleInterval, TimeSpan.Zero);
        Backlog = backlog;
        _ordinary = new(ordinaryCapacity, ordinaryCapacity);
        _operators = new(operatorCapacity, operatorCapacity);
        _time = timeProvider ?? TimeProvider.System;
        _ordinaryRate = ordinaryRate;
        _operatorRate = operatorRate;
    }

    internal BacklogOptions Backlog { get; }

    // A complete pressure-list refresh is also an opportunity to release idle tenant buckets.
    internal void Refresh(IReadOnlyDictionary<int, int> estimates, int nodes)
    {
        lock (_sync)
        {
            var full = estimates.Where(p => p.Value > Backlog.HighWater || p.Value >= Backlog.LowWater && _full.Contains(p.Key))
                .Select(p => p.Key).ToHashSet();
            Volatile.Write(ref _full, full);
            _nodes = Math.Max(1, nodes);
            var now = _time.GetTimestamp();
            foreach (var (key, bucket) in _buckets.ToArray())
            {
                var rate = Limits(key.Kind);
                if (_time.GetElapsedTime(bucket.At, now).TotalSeconds >= (double)rate.Burst * _nodes / rate.PerSecond)
                    _buckets.Remove(key);
            }
        }
    }

    internal Attempt Begin(string tenant, RootAdmissionKind kind)
    {
        var lane = kind == RootAdmissionKind.ShedLoad ? _operators : _ordinary;
        // A busy attempt can still replay a receipt. The store presents the refusal only after checking the receipt.
        return new(this, tenant, kind, lane.Wait(0) ? lane : null);
    }

    internal static RootAdmissionKind Operator(string kind) => kind is
        Contract.Kinds.Suspend or Contract.Kinds.Terminate or Contract.Kinds.Delete or Contract.Kinds.Discard
            ? RootAdmissionKind.ShedLoad : RootAdmissionKind.Input;

    private Rate Limits(RootAdmissionKind kind) => kind == RootAdmissionKind.ShedLoad ? _operatorRate : _ordinaryRate;

    internal sealed class Attempt(RootAdmissionPressure owner, string tenant, RootAdmissionKind kind, SemaphoreSlim? reserved) : IDisposable
    {
        private SemaphoreSlim? _slot = reserved;
        private HashSet<int>? _placement;
        private Bucket? _debited;

        internal bool IsBusy => _slot is null;

        internal int Place(int partition, int count)
        {
            var full = _placement = Volatile.Read(ref owner._full);
            var candidate = partition;
            while (full.Contains(candidate))
            {
                candidate = candidate == count - 1 ? 0 : candidate + 1;
                if (candidate == partition) break;
            }
            return candidate;
        }

        // Call after a duplicate can win and before the creator or event is written. The transactional receipt rolls back
        // if this refuses. The attempt remains reserved through the end of the provider transaction.
        internal void Require(int partition)
        {
            RequireCapacity();
            lock (owner._sync)
            {
                if (kind != RootAdmissionKind.ShedLoad && (_placement ?? owner._full).Contains(partition))
                    throw new RootAdmissionRefused(Contract.RefusalReason.InboxFull, owner.Backlog.SampleInterval);

                var key = (tenant, kind);
                var limits = owner.Limits(kind);
                var rate = (double)limits.PerSecond / owner._nodes;
                var now = owner._time.GetTimestamp();
                var tokens = owner._buckets.TryGetValue(key, out var bucket)
                    ? Math.Min(limits.Burst, bucket.Tokens + owner._time.GetElapsedTime(bucket.At, now).TotalSeconds * rate)
                    : limits.Burst;
                if (tokens < 1)
                    throw new RootAdmissionRefused(Contract.RefusalReason.RateLimited, TimeSpan.FromSeconds((1 - tokens) / rate));
                if (bucket is null || tokens >= limits.Burst) owner._buckets[key] = bucket = new(tokens, now);
                bucket.Tokens = tokens - 1;
                bucket.At = now;
                _debited = bucket;
            }
        }

        internal void RequireCapacity()
        {
            if (IsBusy)
                throw new RootAdmissionRefused(Contract.RefusalReason.NodeBusy, TimeSpan.FromSeconds(1));
        }

        internal void Committed() => _debited = null;

        internal void RolledBack()
        {
            if (_debited is not { } charged) return;
            _debited = null;
            lock (owner._sync)
            {
                var key = (tenant, kind);
                // Refresh or full refill can retire this bucket while its provider transaction is still open.
                if (owner._buckets.TryGetValue(key, out var bucket) && ReferenceEquals(bucket, charged))
                    bucket.Tokens = Math.Min(owner.Limits(kind).Burst, bucket.Tokens + 1);
            }
        }

        public void Dispose()
        {
            var slot = Interlocked.Exchange(ref _slot, null);
            if (slot is null) return;
            RolledBack();
            slot.Release();
        }
    }
}

internal sealed class RootAdmissionRefused(Contract.RefusalReason reason, TimeSpan retryAfter) : Exception(reason.ToString())
{
    internal Contract.Admission Admission => new(Contract.AdmissionOutcome.Refused, Reason: reason, RetryAfter: retryAfter);
    internal Contract.Reply Reply => new(null, null, Error: Message) { RefusalReason = reason, RetryAfter = retryAfter };
}
