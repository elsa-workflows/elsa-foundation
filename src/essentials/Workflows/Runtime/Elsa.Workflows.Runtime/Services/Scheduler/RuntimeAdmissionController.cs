using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Services.Scheduler;

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
/// <para><b>Nor does a sample that dispatched nothing.</b> A gated command that never reached a scheduler dispatch —
/// <c>AlterWorkflow</c> (#1325) is the shipped case — spent its time on something other than the writer this limit
/// bounds, so its duration says nothing about capacity and its fast exits would otherwise collapse the latency
/// baseline every later sample is judged against.</para>
///
/// <para><b>A lone command is never shed.</b> With nothing in flight there is no contention to protect against, and
/// refusing would mean a host that can serve exactly one request at a time serves none. A command dispatched from
/// inside an already-admitted one — a child workflow started during its parent's drain — is never shed either: the
/// parent still holds its charge, so refusing frees nothing and the retry can only bounce until the parent is done.
/// The limiter's job is to refuse work at the door, not to interrupt work already inside.</para>
///
/// <para><b>Every decision is observable (#2614).</b> The limit, the in-flight load, the baseline and each sampled
/// completion are reported on <see cref="RuntimeAdmissionTelemetry"/>; a shed and a limit cut are logged at Debug with
/// the numbers that caused them, and a limit rise at Trace. None of it feeds back into a decision.</para>
/// </summary>
public sealed class RuntimeAdmissionController : IRuntimeAdmissionController
{
    private readonly IRuntimeAdmissionLoadSignal _loadSignal;
    private readonly RuntimeAdmissionOptions _options;
    private readonly RuntimeAdmissionDiagnostics? _diagnostics;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly object _limitLock = new();
    private double _limit;
    private long _baselineTicks;
    private long _lastCutTimestamp = long.MinValue;

    public RuntimeAdmissionController(
        IRuntimeAdmissionLoadSignal loadSignal,
        RuntimeAdmissionOptions? options = null,
        RuntimeAdmissionDiagnostics? diagnostics = null,
        TimeProvider? timeProvider = null,
        ILogger<RuntimeAdmissionController>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(loadSignal);

        _loadSignal = loadSignal;
        _options = options ?? new RuntimeAdmissionOptions();
        _diagnostics = diagnostics;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? (ILogger)NullLogger.Instance;

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
        RuntimeAdmissionTelemetry.Limit.Record(_limit);
    }

    /// <summary>The completion latency saturated samples are judged against; zero until the first sample calibrates it.</summary>
    public TimeSpan Baseline
    {
        get
        {
            lock (_limitLock)
                return TimeSpan.FromTicks(_baselineTicks);
        }
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
        var limit = Limit;

        // The two exemptions are decided before the limit is consulted at all, so neither can be turned into a
        // refusal by a concurrent burst. The load is read before the charge is opened so ObservedLoad means the same
        // thing on both paths — the pre-reservation load — and an exempt command's own seeded unit cannot tip its
        // completion sample into the saturated band.
        if (!_options.Enabled || _loadSignal.HasAmbientCharge)
        {
            var observedLoad = _loadSignal.InFlightDispatches;
            return Admit(_loadSignal.OpenCharge(), observedLoad, limit);
        }

        // One atomic compare-and-reserve, not a read followed by a reserve. Two operations would let N callers
        // arriving together all observe the same below-limit load and all pass, which is the burst the limiter exists
        // to stop.
        var charge = _loadSignal.TryOpenCharge(limit, out var load);
        if (charge is null)
        {
            _diagnostics?.RecordShed();
            RuntimeAdmissionTelemetry.Decisions.Add(1, new KeyValuePair<string, object?>(RuntimeAdmissionTelemetry.OutcomeTag, RuntimeAdmissionTelemetry.ShedOutcome));
            RuntimeAdmissionTelemetry.InFlight.Record(load);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug(
                    new EventId(68201, "RuntimeAdmissionShed"),
                    "Runtime admission shed a command. InFlightDispatches={InFlightDispatches} Limit={Limit} BaselineMs={BaselineMs}",
                    load,
                    limit,
                    Baseline.TotalMilliseconds);
            return RuntimeAdmissionDecision.Shed(
                $"Runtime dispatch admission is at capacity: {load} dispatch units in flight against a limit of {limit:F0}.",
                _options.RetryAfter,
                load,
                limit);
        }

        return Admit(charge, load, limit);
    }

    private RuntimeAdmissionDecision Admit(IRuntimeAdmissionCharge charge, long load, double limit)
    {
        _diagnostics?.RecordAdmitted();
        RuntimeAdmissionTelemetry.Decisions.Add(1, new KeyValuePair<string, object?>(RuntimeAdmissionTelemetry.OutcomeTag, RuntimeAdmissionTelemetry.AdmittedOutcome));
        RuntimeAdmissionTelemetry.InFlight.Record(load);
        return RuntimeAdmissionDecision.Admitted(charge, OnCompleted, load, limit, _timeProvider.GetTimestamp());
    }

    private void OnCompleted(RuntimeAdmissionCompletion completion)
    {
        var duration = _timeProvider.GetElapsedTime(completion.StartTimestamp);
        if (duration <= TimeSpan.Zero)
            return;

        // ChargedUnits is the seed unit plus one per RecordDispatch, so <= 1 is exactly "recorded no dispatch" — and a
        // completion that recorded none is not a drain-shaped sample: its duration measures whatever else the command
        // did, not contention on the writer this limit bounds. Deliberately ABOVE the first-sample calibration below,
        // because the baseline update takes a faster sample outright: fed in as an ordinary sample one sub-millisecond
        // exit collapses the baseline every later sample is judged against, and fed in as the FIRST one it becomes
        // that baseline with no decay path back. AlterWorkflow (#1325) is what made this reachable by construction —
        // it reaches no RecordDispatch() call site in any shipped composition; a dispatching alteration would need a
        // custom preflight handler or a replaced executor, and would then report ChargedUnits > 1 and be sampled
        // normally. It is not the only source, though, so this guard is not dead code if AlterWorkflow is ever
        // ungated: a drain that finds the workflow already terminal, one stopped by the pause gate, and one whose
        // AcquireNextAsync loses the item to a concurrent sweep all reach the drainer and record no dispatch too.
        if (completion.ChargedUnits <= 1)
            return;

        string sample;
        double previousLimit, newLimit;
        long judgedAgainstTicks, newBaselineTicks;
        lock (_limitLock)
        {
            previousLimit = _limit;
            judgedAgainstTicks = _baselineTicks;

            // The first sample only calibrates: with no baseline there is nothing to call this completion fast or slow
            // against, so moving the limit on it would be moving it on noise.
            if (_baselineTicks == 0)
            {
                _baselineTicks = duration.Ticks;
                sample = RuntimeAdmissionTelemetry.CalibrationSample;
            }
            else
            {
                sample = MoveLimit(completion, duration);
            }

            newLimit = _limit;
            newBaselineTicks = _baselineTicks;
        }

        // Reported outside the lock: a listener or a log provider must never sit inside the limit's critical section.

        Report(completion, duration, sample, previousLimit, newLimit, judgedAgainstTicks, newBaselineTicks);
    }

    // Called under _limitLock with a calibrated baseline. Returns which kind of sample this completion was.
    private string MoveLimit(RuntimeAdmissionCompletion completion, TimeSpan duration)
    {
        string sample;

        // An unsaturated sample carries no capacity information. Admitting at a quarter of the limit and taking
        // 200 ms says the work is slow, not that the host is full. Written as a division of the double limit
        // rather than a doubling of the long load: halving a double is exact in binary floating point, so the two
        // forms compare identically, and this one is not an integer multiplication feeding a double comparison.
        var saturated = completion.ObservedLoad >= completion.Limit / 2;
        if (saturated)
        {
            if (duration.Ticks <= _baselineTicks * _options.LatencyTolerance)
            {
                _limit = Clamp(_limit + _options.IncreaseStep);
                _diagnostics?.RecordLimitIncrease();
                sample = RuntimeAdmissionTelemetry.IncreasedSample;
            }
            else if (completion.StartTimestamp < _lastCutTimestamp)
            {
                // One congestion episode cuts once (#2614). This command was admitted before the last cut, so its
                // slowness is the same signal that cut reacted to. Compounding it took a host from 512 to 262 in one
                // burst, below the load its clients keep in flight. Judged by admission time, not by the limit it was
                // admitted under, because increases can carry the limit back above that before the command completes.
                // A command admitted after the cut is a fresh signal and still cuts.
                sample = RuntimeAdmissionTelemetry.SupersededSample;
            }
            else
            {
                _lastCutTimestamp = _timeProvider.GetTimestamp();
                _limit = Clamp(_limit * _options.DecreaseFactor);
                _diagnostics?.RecordLimitDecrease();
                sample = RuntimeAdmissionTelemetry.DecreasedSample;
            }
        }
        else
        {
            sample = RuntimeAdmissionTelemetry.UnsaturatedSample;
        }

        // Let the baseline drift up toward the observed duration so a host that genuinely got slower recalibrates
        // instead of cutting forever against a reading it can no longer reach. A faster sample still wins outright.
        _baselineTicks = Math.Min(duration.Ticks, (long)(_baselineTicks * _options.BaselineDecay) + 1);

        return sample;
    }

    private void Report(
        RuntimeAdmissionCompletion completion,
        TimeSpan duration,
        string sample,
        double previousLimit,
        double newLimit,
        long judgedAgainstTicks,
        long newBaselineTicks)
    {
        RuntimeAdmissionTelemetry.SampleDuration.Record(
            duration.TotalMilliseconds,
            new KeyValuePair<string, object?>(RuntimeAdmissionTelemetry.SampleTag, sample));
        RuntimeAdmissionTelemetry.Baseline.Record(TimeSpan.FromTicks(newBaselineTicks).TotalMilliseconds);
        if (newLimit != previousLimit)
            RuntimeAdmissionTelemetry.Limit.Record(newLimit);

        var level = sample switch
        {
            RuntimeAdmissionTelemetry.DecreasedSample => LogLevel.Debug,
            RuntimeAdmissionTelemetry.IncreasedSample => LogLevel.Trace,
            _ => LogLevel.None
        };
        if (level == LogLevel.None || !_logger.IsEnabled(level))
            return;

        _logger.Log(
            level,
            sample == RuntimeAdmissionTelemetry.DecreasedSample
                ? new EventId(68202, "RuntimeAdmissionLimitDecreased")
                : new EventId(68203, "RuntimeAdmissionLimitIncreased"),
            "Runtime admission limit moved from {PreviousLimit} to {Limit}. DurationMs={DurationMs} BaselineMs={BaselineMs} LatencyTolerance={LatencyTolerance} AdmittedAtLoad={AdmittedAtLoad} AdmittedAtLimit={AdmittedAtLimit} ChargedUnits={ChargedUnits}",
            previousLimit,
            newLimit,
            duration.TotalMilliseconds,
            TimeSpan.FromTicks(judgedAgainstTicks).TotalMilliseconds,
            _options.LatencyTolerance,
            completion.ObservedLoad,
            completion.Limit,
            completion.ChargedUnits);
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
