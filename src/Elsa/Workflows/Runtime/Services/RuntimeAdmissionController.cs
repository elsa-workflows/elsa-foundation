using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// The one admission controller (RB1, #1235). Adaptive by default and pinned by
/// <see cref="RuntimeAdmissionOptions.StaticLimit"/> when an operator wants predictability — pinned by clamping this
/// controller's range to a single value, never by swapping in a second implementation, so an operator running a
/// static ceiling exercises exactly the code the adaptive tests cover.
///
/// <para><b>Only saturated samples move the limit.</b> A completion taken while the host was nowhere near its limit
/// says nothing about capacity: it is slow because the work was slow, not because the writer was contended. Feeding
/// such samples in would let one early fast reading ratchet a quiet host down to <see cref="RuntimeAdmissionOptions.MinLimit"/>
/// and start shedding for no reason. So the limit only moves on a sample admitted at half the limit or more, which is
/// also why a host that never saturates never sheds.</para>
///
/// <para><b>A lone command is never shed.</b> With nothing in flight there is no contention to protect against, and
/// refusing would mean a host that can serve exactly one request at a time serves none.</para>
/// </summary>
public sealed class RuntimeAdmissionController : IRuntimeAdmissionController
{
    private readonly IRuntimeAdmissionLoadSignal _loadSignal;
    private readonly RuntimeAdmissionOptions _options;
    private readonly RuntimeAdmissionDiagnostics? _diagnostics;
    private readonly TimeProvider _timeProvider;
    private readonly object _limitLock = new();
    private double _limit;
    private long _baselineTicks;

    public RuntimeAdmissionController(
        IRuntimeAdmissionLoadSignal loadSignal,
        RuntimeAdmissionOptions? options = null,
        RuntimeAdmissionDiagnostics? diagnostics = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(loadSignal);

        _loadSignal = loadSignal;
        _options = options ?? new RuntimeAdmissionOptions();
        _diagnostics = diagnostics;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_options.MinLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The minimum admission limit must be greater than zero.");
        if (_options.MaxLimit < _options.MinLimit)
            throw new ArgumentOutOfRangeException(nameof(options), "The maximum admission limit cannot be below the minimum.");
        if (_options.StaticLimit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The static admission limit must be greater than zero.");
        if (_options.DecreaseFactor is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(options), "The admission decrease factor must be between zero and one, exclusive.");
        if (_options.LatencyTolerance < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "The admission latency tolerance cannot be below one.");

        _limit = Clamp(_options.StaticLimit ?? _options.InitialLimit);
    }

    public double Limit
    {
        get
        {
            lock (_limitLock)
                return _limit;
        }
    }

    public RuntimeAdmissionDecision TryAdmit()
    {
        var load = _loadSignal.InFlightDispatches;
        var limit = Limit;

        if (_options.Enabled && load > 0 && load >= limit)
        {
            _diagnostics?.RecordShed();
            return RuntimeAdmissionDecision.Shed(
                $"Runtime dispatch admission is at capacity: {load} dispatch units in flight against a limit of {limit:F0}.",
                _options.RetryAfter,
                load,
                limit);
        }

        _diagnostics?.RecordAdmitted();
        return RuntimeAdmissionDecision.Admitted(
            _loadSignal.OpenCharge(),
            OnCompleted,
            load,
            limit,
            _timeProvider.GetTimestamp());
    }

    private void OnCompleted(RuntimeAdmissionCompletion completion)
    {
        var duration = _timeProvider.GetElapsedTime(completion.StartTimestamp);
        if (duration <= TimeSpan.Zero)
            return;

        lock (_limitLock)
        {
            // The first sample only calibrates: with no baseline there is nothing to call this completion fast or slow
            // against, so moving the limit on it would be moving it on noise.
            if (_baselineTicks == 0)
            {
                _baselineTicks = duration.Ticks;
                return;
            }

            // An unsaturated sample carries no capacity information. Admitting at a quarter of the limit and taking
            // 200 ms says the work is slow, not that the host is full.
            var saturated = completion.ObservedLoad * 2 >= completion.Limit;
            if (saturated)
            {
                if (duration.Ticks <= _baselineTicks * _options.LatencyTolerance)
                {
                    _limit = Clamp(_limit + _options.IncreaseStep);
                    _diagnostics?.RecordLimitIncrease();
                }
                else
                {
                    _limit = Clamp(_limit * _options.DecreaseFactor);
                    _diagnostics?.RecordLimitDecrease();
                }
            }

            // Let the baseline drift up toward the observed duration so a host that genuinely got slower recalibrates
            // instead of cutting forever against a reading it can no longer reach. A faster sample still wins outright.
            _baselineTicks = Math.Min(duration.Ticks, (long)(_baselineTicks * _options.BaselineDecay) + 1);
        }
    }

    private double Clamp(double limit)
    {
        // The static ceiling collapses the adaptive range onto one value. Adaptation still runs and still reports its
        // increases and decreases; the clamp is simply what makes them no-ops.
        var min = _options.StaticLimit ?? _options.MinLimit;
        var max = _options.StaticLimit ?? _options.MaxLimit;
        return Math.Clamp(limit, min, max);
    }
}
