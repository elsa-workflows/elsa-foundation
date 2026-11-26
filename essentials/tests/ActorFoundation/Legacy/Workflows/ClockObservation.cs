using Kernel.Durable;

namespace Kernel.Workflows;

// The new identity protection predicate cannot obtain a mutable clock through an interface cast.
internal sealed class ClockObservation(ICommitClock clock):ICommitClockObservation
{
    public bool Available=>clock.Available;
    public long? Resolve(string reference)=>clock.Resolve(reference);
}
