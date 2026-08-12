namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// The load reading admission control decides on (RB1, #1235). Split out from the controller on purpose: with the
/// signal behind an interface a test sets <see cref="InFlightDispatches"/> directly and "load crosses the limit, the
/// next command is shed" becomes a deterministic assertion instead of a race against N concurrent workflows. An
/// adaptive limiter that can only be demonstrated under real load cannot be falsified, and this repository's evidence
/// standard is deterministic and load-proof.
/// </summary>
/// <remarks>
/// The unit is a <b>dispatch</b>, not a run (#1225). The default implementation charges each admitted command one
/// unit on admission and one more per scheduler dispatch it performs, so a heavy External-leaf run weighs ~56 while a
/// fusable run weighs ~5 and the limit means the same thing for both shapes.
/// </remarks>
public interface IRuntimeAdmissionLoadSignal
{
    /// <summary>Dispatch units currently in flight across every admitted command this host is running.</summary>
    long InFlightDispatches { get; }

    /// <summary>
    /// Whether the calling flow is already inside an admitted command. A child workflow started from inside a parent's
    /// drain is not new work arriving at the host, it is the continuation of work the host already accepted, so
    /// shedding it would free nothing (the parent still holds its charge) and could only bounce until the parent
    /// finishes.
    /// </summary>
    bool HasAmbientCharge { get; }

    /// <summary>
    /// Compares the in-flight load against <paramref name="limit"/> and opens a charge <b>only if</b> it is below,
    /// as one atomic operation. Returns null when the reservation was refused; <paramref name="observedLoad"/>
    /// always reports the load the decision was made on.
    /// </summary>
    /// <remarks>
    /// Check and reserve must not be two operations. Read the load, then reserve, and N callers arriving together all
    /// read the same below-limit value, all pass, and all reserve — so an arbitrarily large burst walks through a
    /// limiter that exists precisely to stop bursts. The race is worst exactly when the limiter matters most, which
    /// is why the compare-and-increment lives here, next to the counter it protects, rather than in the controller.
    /// </remarks>
    IRuntimeAdmissionCharge? TryOpenCharge(double limit, out long observedLoad);

    /// <summary>
    /// Opens a charge unconditionally, for the paths the controller has already decided are not subject to the limit
    /// (shedding disabled, or a command dispatched from inside an admitted one). Disposing it releases the units it
    /// accumulated and restores the previous ambient charge.
    /// </summary>
    IRuntimeAdmissionCharge OpenCharge();

    /// <summary>
    /// Charges one scheduler dispatch to the ambient charge. A no-op when nothing was admitted on this flow, which is
    /// what keeps recovery-sweep and pump dispatches out of the live-dispatch reading.
    /// </summary>
    void RecordDispatch();
}

/// <summary>The in-flight charge held by one admitted command for as long as it occupies the host.</summary>
public interface IRuntimeAdmissionCharge : IDisposable
{
    /// <summary>Dispatch units this charge currently holds.</summary>
    long Units { get; }
}
