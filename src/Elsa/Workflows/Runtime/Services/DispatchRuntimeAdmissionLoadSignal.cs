using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// The shipped load signal: in-flight load measured in <b>scheduler dispatches</b> (RB1, #1235).
///
/// <para>Counting admitted commands would have been simpler and would have been wrong. An External-leaf run pays ~56
/// dispatches where a fusable run pays ~5 (#1225), so a command-count limit admits roughly eleven times the real work
/// for the shape production traffic actually has while reporting the same number. Charging each command one unit on
/// admission plus one per dispatch it performs makes the limit mean the same amount of work for both shapes.</para>
///
/// <para>Every read and write of the counter takes one lock, which is what makes
/// <see cref="TryOpenCharge"/> a real compare-and-reserve rather than a check followed by a hopeful increment. The
/// lock is uncontended in the common case and is held for a comparison and an addition; a dispatch it guards costs
/// orders of magnitude more.</para>
///
/// <para>The ambient charge is an <see cref="AsyncLocal{T}"/>, the same idiom the live-drain delivery and burst
/// scopes use. A dispatch performed on a flow that was never admitted — the recovery sweep, the placement and timer
/// pumps — charges nothing, so the reading stays a reading of <em>live</em> dispatch. Those paths carry their own
/// bounds (<c>RuntimeResumptionOptions.MaxExecutionsPerSweep</c> and its siblings); admission does not double-bound
/// them.</para>
/// </summary>
public sealed class DispatchRuntimeAdmissionLoadSignal : IRuntimeAdmissionLoadSignal
{
    private static readonly AsyncLocal<Charge?> Ambient = new();
    private readonly object _gate = new();
    private long _inFlightDispatches;

    public long InFlightDispatches
    {
        get
        {
            lock (_gate)
                return _inFlightDispatches;
        }
    }

    public bool HasAmbientCharge => Ambient.Value is not null;

    public IRuntimeAdmissionCharge? TryOpenCharge(double limit, out long observedLoad)
    {
        lock (_gate)
        {
            observedLoad = _inFlightDispatches;

            // A lone command is never refused: with nothing in flight there is no contention to protect against, and
            // refusing would mean a host that can serve exactly one request at a time serves none. Kept explicit even
            // though the clamped limit is always at least one, so the invariant survives a looser option range.
            if (observedLoad > 0 && observedLoad >= limit)
                return null;

            return OpenChargeCore();
        }
    }

    public IRuntimeAdmissionCharge OpenCharge()
    {
        lock (_gate)
            return OpenChargeCore();
    }

    public void RecordDispatch() => Ambient.Value?.Add();

    // Seeded at one unit: an admitted command always performs at least one dispatch, so a command that has been
    // admitted but has not dispatched yet must still weigh something. Without the seed a burst of simultaneous
    // arrivals would all read a load of zero and all be admitted.
    private Charge OpenChargeCore()
    {
        var charge = new Charge(this, Ambient.Value);
        Ambient.Value = charge;
        _inFlightDispatches++;
        return charge;
    }

    private sealed class Charge(DispatchRuntimeAdmissionLoadSignal owner, Charge? previous) : IRuntimeAdmissionCharge
    {
        private long _units = 1;
        private bool _disposed;

        public long Units
        {
            get
            {
                lock (owner._gate)
                    return _units;
            }
        }

        public void Add()
        {
            lock (owner._gate)
            {
                if (_disposed)
                    return;
                _units++;
                owner._inFlightDispatches++;
            }
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                owner._inFlightDispatches -= _units;
            }

            Ambient.Value = previous;
        }
    }
}
