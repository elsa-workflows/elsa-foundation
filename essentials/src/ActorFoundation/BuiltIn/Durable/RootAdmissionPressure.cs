using Kernel.Runtime;
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
        internal int Reserved { get; set; }
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
    private Func<CancellationToken, Task>? _coldRead;
    private long _lastColdRead;
    private bool _coldReading;

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

    internal void RefreshOnDemand(Func<CancellationToken, Task> read)
    {
        _coldRead = read;
        _lastColdRead = _time.GetTimestamp();
    }

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
                if (bucket.Reserved == 0 && _time.GetElapsedTime(bucket.At, now).TotalSeconds >= (double)rate.Burst * _nodes / rate.PerSecond)
                    _buckets.Remove(key);
            }
        }
    }

    internal Attempt Begin(string tenant, RootAdmissionKind kind)
    {
        if (_coldRead is { } read) ScheduleColdRead(read);
        var lane = kind == RootAdmissionKind.ShedLoad ? _operators : _ordinary;
        // A busy attempt can still replay a receipt. The store presents the refusal only after checking the receipt.
        return new(this, tenant, kind, lane.Wait(0) ? lane : null);
    }

    internal Attempt BeginInlineStart(string tenant) => new(this, tenant, RootAdmissionKind.Start, reserved: null);

    internal static int? PlaceInlineStart(string name, int[] eligible, RootAdmissionPressure? pressure)
    {
        if (eligible.Length == 0) return null;
        var full = pressure is null ? null : Volatile.Read(ref pressure._full);
        // A full partition can still execute a start that never needs a provider write.
        var healthy = full is { Count: > 0 } ? eligible.Where(partition => !full.Contains(partition)).ToArray() : eligible;
        var placement = healthy.Length > 0 ? healthy : eligible;
        return placement[InstanceRef.Spread(name, placement.Length)];
    }

    // A standalone receiving edge has no node heartbeat. Refresh at most once per interval and continue using
    // the existing sample while the read runs. An idle edge starts no background work.
    private void ScheduleColdRead(Func<CancellationToken, Task> read)
    {
        lock (_sync)
        {
            var now = _time.GetTimestamp();
            if (_coldReading || _time.GetElapsedTime(_lastColdRead, now) < Backlog.SampleInterval) return;
            _coldReading = true;
            _lastColdRead = now;
        }
        _ = Task.Run(RefreshAsync);

        async Task RefreshAsync()
        {
            using var stop = new CancellationTokenSource(Backlog.SampleInterval);
            try { await read(stop.Token); }
            catch (Exception) { } // A failed observational read leaves the previous pressure intact.
            finally { lock (_sync) _coldReading = false; }
        }
    }

    internal static RootAdmissionKind Operator(string kind) => kind is
        Contract.Kinds.Suspend or Contract.Kinds.Terminate or Contract.Kinds.Delete or Contract.Kinds.Discard
            ? RootAdmissionKind.ShedLoad : RootAdmissionKind.Input;

    private Rate Limits(RootAdmissionKind kind) => kind == RootAdmissionKind.ShedLoad ? _operatorRate : _ordinaryRate;

    private double Refill(Bucket bucket, Rate limits, long now)
    {
        var rate = (double)limits.PerSecond / _nodes;
        bucket.Tokens = Math.Min(limits.Burst, bucket.Tokens + _time.GetElapsedTime(bucket.At, now).TotalSeconds * rate);
        bucket.At = now;
        return rate;
    }

    internal sealed class Attempt(RootAdmissionPressure owner, string tenant, RootAdmissionKind kind, SemaphoreSlim? reserved) : IDisposable
    {
        private SemaphoreSlim? _slot = reserved;
        private HashSet<int>? _placement;
        private Bucket? _reservation;

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

        // The attempt remains reserved through the provider transaction. A successful write spends the token at
        // commit. Replays and rollbacks release only the reservation, so refill never credits them twice.
        internal void Require(int partition) => Require(partition, null);

        internal void Require(IReadOnlyCollection<int> partitions) => Require(null, partitions);

        private void Require(int? partition, IReadOnlyCollection<int>? partitions)
        {
            RequireCapacity();
            lock (owner._sync)
            {
                RequireBacklog(partition, partitions);
                ReserveTenantRate();
            }
        }

        internal void RequireInlineTenantRate()
        {
            lock (owner._sync) ReserveTenantRate();
        }

        internal void RequireInlinePromotion(int partition)
        {
            RequireCapacity();
            lock (owner._sync) RequireBacklog(partition, null);
        }

        private void RequireBacklog(int? partition, IReadOnlyCollection<int>? partitions)
        {
            var full = _placement ?? owner._full;
            if (kind != RootAdmissionKind.ShedLoad && (partition is { } one ? full.Contains(one) : partitions!.Any(full.Contains)))
                throw new RootAdmissionRefused(Contract.RefusalReason.InboxFull, owner.Backlog.SampleInterval);
        }

        private void ReserveTenantRate()
        {
            var key = (tenant, kind);
            var limits = owner.Limits(kind);
            var now = owner._time.GetTimestamp();
            if (!owner._buckets.TryGetValue(key, out var bucket))
                owner._buckets.Add(key, bucket = new(limits.Burst, now));
            var rate = owner.Refill(bucket, limits, now);
            var available = bucket.Tokens - bucket.Reserved;
            if (available < 1)
                throw new RootAdmissionRefused(Contract.RefusalReason.RateLimited, TimeSpan.FromSeconds((1 - available) / rate));
            bucket.Reserved++;
            _reservation = bucket;
        }

        internal void RequireCapacity()
        {
            if (IsBusy)
                throw new RootAdmissionRefused(Contract.RefusalReason.NodeBusy, TimeSpan.FromSeconds(1));
        }

        internal void Committed()
        {
            if (Interlocked.Exchange(ref _reservation, null) is not { } bucket) return;
            lock (owner._sync)
            {
                owner.Refill(bucket, owner.Limits(kind), owner._time.GetTimestamp());
                bucket.Reserved--;
                bucket.Tokens--;
            }
        }

        internal void RolledBack()
        {
            if (Interlocked.Exchange(ref _reservation, null) is not { } bucket) return;
            lock (owner._sync) bucket.Reserved--;
        }

        public void Dispose()
        {
            var slot = Interlocked.Exchange(ref _slot, null);
            RolledBack();
            slot?.Release();
        }
    }
}

internal sealed class RootAdmissionRefused(Contract.RefusalReason reason, TimeSpan retryAfter) : Exception(reason.ToString())
{
    internal Contract.Admission Admission => new(Contract.AdmissionOutcome.Refused, Reason: reason, RetryAfter: retryAfter);
    internal Contract.Reply Reply => new(null, null, Error: Message) { RefusalReason = reason, RetryAfter = retryAfter };
}
