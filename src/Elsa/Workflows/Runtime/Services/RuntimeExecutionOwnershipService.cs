using System.Globalization;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default execution ownership service (single-writer fencing, RT-2). Backs ownership with the operational state store:
/// each workflow execution has a single ownership record whose metadata carries the highest fencing token ever issued
/// for it. Acquisition issues a strictly greater token; release clears the live lease/heartbeat while preserving that
/// counter so tokens are never reused; the checkpoint-commit funnel calls <see cref="EnsureCurrentAsync"/> to fence
/// stale writers out.
/// </summary>
public sealed class RuntimeExecutionOwnershipService : IRuntimeExecutionOwnershipService
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IExecutionLivenessStateStore _operationalStateStore;
    private readonly TimeProvider _timeProvider;
    private readonly RuntimeExecutionOwnershipOptions _options;

    public RuntimeExecutionOwnershipService(IExecutionLivenessStateStore operationalStateStore)
        : this(operationalStateStore, TimeProvider.System, new RuntimeExecutionOwnershipOptions())
    {
    }

    public RuntimeExecutionOwnershipService(
        IExecutionLivenessStateStore operationalStateStore,
        TimeProvider timeProvider,
        RuntimeExecutionOwnershipOptions options)
    {
        ArgumentNullException.ThrowIfNull(operationalStateStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OwnerId);

        if (options.LeaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Lease duration must be greater than zero.");

        _operationalStateStore = operationalStateStore;
        _timeProvider = timeProvider;
        _options = options;
    }

    public async ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindOwnershipStateAsync(workflowExecutionId, cancellationToken);
            var nextToken = ReadHighestIssuedToken(existing) + 1;
            var now = _timeProvider.GetUtcNow();

            var lease = new RuntimeExecutionLease(
                leaseId: ShortIdentityGenerator.Generate(now),
                workflowExecutionId: workflowExecutionId,
                ownerId: _options.OwnerId,
                acquiredAt: now,
                expiresAt: now + _options.LeaseDuration,
                fencingToken: nextToken);

            var heartbeat = NewHeartbeat(workflowExecutionId, lease, now);
            await SaveOwnershipStateAsync(workflowExecutionId, lease, heartbeat, nextToken, cancellationToken);
            return lease;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask HeartbeatAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindOwnershipStateAsync(lease.WorkflowExecutionId, cancellationToken);
            var highestToken = Math.Max(ReadHighestIssuedToken(existing), lease.FencingToken);
            var now = _timeProvider.GetUtcNow();

            var refreshedLease = new RuntimeExecutionLease(
                leaseId: lease.LeaseId,
                workflowExecutionId: lease.WorkflowExecutionId,
                ownerId: lease.OwnerId,
                acquiredAt: lease.AcquiredAt,
                expiresAt: now + _options.LeaseDuration,
                fencingToken: lease.FencingToken);

            var heartbeat = NewHeartbeat(lease.WorkflowExecutionId, refreshedLease, now);
            await SaveOwnershipStateAsync(lease.WorkflowExecutionId, refreshedLease, heartbeat, highestToken, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask ReleaseAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindOwnershipStateAsync(lease.WorkflowExecutionId, cancellationToken);
            var highestToken = Math.Max(ReadHighestIssuedToken(existing), lease.FencingToken);

            // Clear the live lease/heartbeat so a completed execution is not mistaken for an interrupted one, but keep
            // the fencing-token counter so the next acquisition never reuses a token.
            await SaveOwnershipStateAsync(lease.WorkflowExecutionId, executionLease: null, heartbeat: null, highestToken, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var existing = await FindOwnershipStateAsync(workflowExecutionId, cancellationToken);
        var highestToken = ReadHighestIssuedToken(existing);

        // No ownership has ever been established for this execution — nothing to fence against.
        if (highestToken == 0)
            return;

        if (fencingToken != highestToken)
            throw new RuntimeStaleFencingTokenException(workflowExecutionId, fencingToken, highestToken);
    }

    private RuntimeHeartbeat NewHeartbeat(string workflowExecutionId, RuntimeExecutionLease lease, DateTimeOffset now) =>
        new(
            heartbeatId: ShortIdentityGenerator.Generate(now),
            workflowExecutionId: workflowExecutionId,
            ownerId: lease.OwnerId,
            leaseId: lease.LeaseId,
            recordedAt: now);

    private async ValueTask SaveOwnershipStateAsync(
        string workflowExecutionId,
        RuntimeExecutionLease? executionLease,
        RuntimeHeartbeat? heartbeat,
        long highestIssuedToken,
        CancellationToken cancellationToken)
    {
        var state = new ExecutionLivenessState(
            operationalStateId: OwnershipStateId(workflowExecutionId),
            workflowExecutionId: workflowExecutionId,
            executionLease: executionLease,
            heartbeat: heartbeat,
            drain: null,
            interruptedExecution: null,
            metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.OwnershipFencingToken] = highestIssuedToken.ToString(CultureInfo.InvariantCulture)
            });

        await _operationalStateStore.SaveAsync(state, cancellationToken);
    }

    private ValueTask<ExecutionLivenessState?> FindOwnershipStateAsync(string workflowExecutionId, CancellationToken cancellationToken) =>
        _operationalStateStore.FindAsync(workflowExecutionId, OwnershipStateId(workflowExecutionId), cancellationToken);

    private static long ReadHighestIssuedToken(ExecutionLivenessState? state)
    {
        if (state is null)
            return 0;

        if (state.Metadata.TryGetValue(RuntimeMetadataKeys.OwnershipFencingToken, out var raw) &&
            long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var token))
            return token;

        return state.ExecutionLease?.FencingToken ?? 0;
    }

    private static string OwnershipStateId(string workflowExecutionId) => $"ownership:{workflowExecutionId}";
}
