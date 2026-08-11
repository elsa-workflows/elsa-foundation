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
/// <para>The ambient charge is an <see cref="AsyncLocal{T}"/>, the same idiom the live-drain delivery and burst
/// scopes use. A dispatch performed on a flow that was never admitted — the recovery sweep, the placement and timer
/// pumps — charges nothing, so the reading stays a reading of <em>live</em> dispatch.</para>
/// </summary>
public sealed class DispatchRuntimeAdmissionLoadSignal : IRuntimeAdmissionLoadSignal
{
    private static readonly AsyncLocal<Charge?> Ambient = new();
    private long _inFlightDispatches;

    public long InFlightDispatches => Interlocked.Read(ref _inFlightDispatches);

    public IRuntimeAdmissionCharge OpenCharge()
    {
        // Seeded at one unit: an admitted command always performs at least one dispatch, so a command that has been
        // admitted but has not dispatched yet must still weigh something. Without the seed a burst of simultaneous
        // arrivals would all read a load of zero and all be admitted.
        var charge = new Charge(this, Ambient.Value);
        Ambient.Value = charge;
        Interlocked.Increment(ref _inFlightDispatches);
        return charge;
    }

    public void RecordDispatch() => Ambient.Value?.Add();

    private sealed class Charge(DispatchRuntimeAdmissionLoadSignal owner, Charge? previous) : IRuntimeAdmissionCharge
    {
        private long _units = 1;
        private bool _disposed;

        public long Units => Interlocked.Read(ref _units);

        public void Add()
        {
            if (_disposed)
                return;
            Interlocked.Increment(ref _units);
            Interlocked.Increment(ref owner._inFlightDispatches);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Interlocked.Add(ref owner._inFlightDispatches, -Interlocked.Read(ref _units));
            Ambient.Value = previous;
        }
    }
}
