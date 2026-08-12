using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The verdict for one command, and — when admitted — the in-flight charge it holds. Disposing an admitted decision
/// releases the charge and hands the completion back to the controller, so the caller has a single
/// <c>using</c> to get right rather than a paired acquire/complete it can forget.
/// </summary>
public sealed class RuntimeAdmissionDecision : IDisposable
{
    private readonly IRuntimeAdmissionCharge? _charge;
    private readonly Action<RuntimeAdmissionCompletion>? _onCompleted;
    private readonly long _startTimestamp;
    private bool _disposed;

    private RuntimeAdmissionDecision(
        bool isAdmitted,
        string? reason,
        TimeSpan? retryAfter,
        long observedLoad,
        double limit,
        IRuntimeAdmissionCharge? charge,
        Action<RuntimeAdmissionCompletion>? onCompleted,
        long startTimestamp)
    {
        IsAdmitted = isAdmitted;
        Reason = reason;
        RetryAfter = retryAfter;
        ObservedLoad = observedLoad;
        Limit = limit;
        _charge = charge;
        _onCompleted = onCompleted;
        _startTimestamp = startTimestamp;
    }

    /// <summary>Whether the command may run now.</summary>
    public bool IsAdmitted { get; }

    /// <summary>Why the command was refused. Non-null exactly when <see cref="IsAdmitted"/> is false.</summary>
    public string? Reason { get; }

    /// <summary>How long a shed caller should wait before retrying. Non-null exactly when the command was refused.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Dispatch units in flight when the decision was made.</summary>
    public long ObservedLoad { get; }

    /// <summary>The limit the decision was made against, in dispatch units.</summary>
    public double Limit { get; }

    /// <summary>Refuses one command. Holds no charge, so there is nothing to release.</summary>
    public static RuntimeAdmissionDecision Shed(string reason, TimeSpan retryAfter, long observedLoad, double limit) =>
        new(false, reason, retryAfter, observedLoad, limit, null, null, 0);

    /// <summary>Admits one command, holding <paramref name="charge"/> until the decision is disposed.</summary>
    public static RuntimeAdmissionDecision Admitted(
        IRuntimeAdmissionCharge charge,
        Action<RuntimeAdmissionCompletion> onCompleted,
        long observedLoad,
        double limit,
        long startTimestamp) =>
        new(true, null, null, observedLoad, limit, charge, onCompleted, startTimestamp);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_charge is null)
            return;

        var units = _charge.Units;
        _charge.Dispose();
        _onCompleted?.Invoke(new RuntimeAdmissionCompletion(ObservedLoad, Limit, units, _startTimestamp));
    }
}

/// <summary>What one admitted command reports back once it is done, and the input the adaptive limit moves on.</summary>
/// <param name="ObservedLoad">Dispatch units in flight when the command was admitted.</param>
/// <param name="Limit">The limit in force when the command was admitted.</param>
/// <param name="ChargedUnits">Dispatch units the command actually consumed.</param>
/// <param name="StartTimestamp">Controller timestamp taken at admission; the controller turns it into a duration.</param>
public readonly record struct RuntimeAdmissionCompletion(
    long ObservedLoad,
    double Limit,
    long ChargedUnits,
    long StartTimestamp);
