using Elsa.Persistence.Groundwork.DesignConformance.Tests;

namespace Elsa.Persistence.Groundwork.DesignConformance.Target;

/// <summary>Arms at most one provider-neutral fault at a time and triggers it at the requested phase of the next atomic write.</summary>
public sealed class GroundworkTargetAtomicityFaultController(string providerDisplayName)
{
    private GroundworkTargetAtomicityFaultLease? _armed;

    public IDesignAtomicityFaultLease Arm(DesignAtomicityFaultPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (_armed is not null)
            throw new InvalidOperationException($"Only one {providerDisplayName} atomicity fault may be armed at a time.");

        _armed = new GroundworkTargetAtomicityFaultLease(this, plan);
        return _armed;
    }

    public GroundworkTargetAtomicityOperationCancellation BeginOperation(CancellationToken callerToken) =>
        _armed?.BeginOperation(callerToken) ?? GroundworkTargetAtomicityOperationCancellation.PassThrough(callerToken);

    public void ThrowIfTriggered(DesignAtomicityFaultPhase phase, GroundworkTargetAtomicityOperationCancellation operation)
    {
        var fault = _armed;
        if (fault is null || fault.Plan.Phase != phase || !fault.TryTrigger())
            return;

        switch (fault.Plan.Action)
        {
            case DesignAtomicityFaultAction.Throw:
                throw new InvalidOperationException($"Injected {providerDisplayName} atomicity fault at '{phase}'.");
            case DesignAtomicityFaultAction.Cancel:
                operation.Cancel();
                operation.Token.ThrowIfCancellationRequested();
                break;
            case DesignAtomicityFaultAction.ReturnNonSuccess:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault.Plan), fault.Plan.Action, null);
        }
    }

    public bool ResolveProviderDecision(GroundworkTargetAtomicityOperationCancellation operation, CancellationToken cancellationToken)
    {
        var fault = _armed;
        if (fault is null || fault.Plan.Phase != DesignAtomicityFaultPhase.BeforeProviderDecision || !fault.TryTrigger())
            return false;

        switch (fault.Plan.Action)
        {
            case DesignAtomicityFaultAction.ReturnNonSuccess:
                return true;
            case DesignAtomicityFaultAction.Cancel:
                operation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            case DesignAtomicityFaultAction.Throw:
                throw new InvalidOperationException($"Injected {providerDisplayName} atomicity provider-decision fault.");
            default:
                throw new ArgumentOutOfRangeException(nameof(fault.Plan), fault.Plan.Action, null);
        }
    }

    private void Disarm(GroundworkTargetAtomicityFaultLease lease)
    {
        if (ReferenceEquals(_armed, lease))
            _armed = null;
    }

    private sealed class GroundworkTargetAtomicityFaultLease(
        GroundworkTargetAtomicityFaultController owner,
        DesignAtomicityFaultPlan plan) : IDesignAtomicityFaultLease
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _disposed;

        public DesignAtomicityFaultPlan Plan { get; } = plan;
        public bool WasTriggered { get; private set; }

        public bool TryTrigger()
        {
            if (WasTriggered)
                return false;

            WasTriggered = true;
            return true;
        }

        public GroundworkTargetAtomicityOperationCancellation BeginOperation(CancellationToken callerToken) =>
            new(CancellationTokenSource.CreateLinkedTokenSource(callerToken, _cancellation.Token), _cancellation);

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Disarm(this);
                _cancellation.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}

public sealed class GroundworkTargetAtomicityOperationCancellation : IDisposable
{
    private readonly CancellationTokenSource? _source;
    private readonly CancellationTokenSource? _faultCancellation;

    public GroundworkTargetAtomicityOperationCancellation(CancellationTokenSource source, CancellationTokenSource faultCancellation)
    {
        _source = source;
        _faultCancellation = faultCancellation;
        Token = source.Token;
    }

    private GroundworkTargetAtomicityOperationCancellation(CancellationToken token) => Token = token;

    public CancellationToken Token { get; }

    public static GroundworkTargetAtomicityOperationCancellation PassThrough(CancellationToken token) => new(token);
    public void Cancel() => _faultCancellation?.Cancel();
    public void Dispose() => _source?.Dispose();
}
