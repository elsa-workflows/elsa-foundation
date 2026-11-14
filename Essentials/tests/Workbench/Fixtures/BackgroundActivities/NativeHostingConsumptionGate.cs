using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workbench.Tests.BackgroundActivities;

/// <summary>
/// Holds this test host's receipt/transport consumption and new execution drains without suspending database or
/// membership threads. Acknowledgement follows prior reads, acquisitions and the real release of acquired leases.
/// </summary>
internal sealed class NativeHostingConsumptionGate(string journalDirectory, string role) : IDisposable
{
    private readonly SemaphoreSlim _reads = new(1, 1);
    private readonly HashSet<(string ExecutionId, string LeaseId, string OwnerId, long Fence)> _leases = [];
    private int _acquiring;
    private string? _acknowledgedToken;

    public async ValueTask<T> ReadAsync<T>(Func<ValueTask<T>> read, T heldResult, CancellationToken cancellationToken)
    {
        await _reads.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var holdPath = Path.Combine(journalDirectory, $"hold-consumption-{role}");
            if (!File.Exists(holdPath))
                return await read();

            await AcknowledgeIfQuiescentAsync(cancellationToken);
            return heldResult;
        }
        finally
        {
            _reads.Release();
        }
    }

    public async ValueTask<RuntimeExecutionLease> AcquireAsync(
        Func<ValueTask<RuntimeExecutionLease>> acquire, CancellationToken cancellationToken, Action<string>? held = null)
    {
        string? reportedToken = null;
        while (true)
        {
            await _reads.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(Path.Combine(journalDirectory, $"hold-consumption-{role}")))
                {
                    _acquiring++;
                    break;
                }

                var token = await AcknowledgeIfQuiescentAsync(cancellationToken);
                if (token is not null && !StringComparer.Ordinal.Equals(reportedToken, token))
                {
                    held?.Invoke(token);
                    reportedToken = token;
                }
            }
            finally
            {
                _reads.Release();
            }

            // No state lock or provider operation is held while admission is closed. In particular an old drain's
            // release can finish, and a cancelled local resumption dispatch can leave without entering the backend.
            await Task.Delay(25, cancellationToken);
        }

        RuntimeExecutionLease lease;
        try
        {
            lease = await acquire();
        }
        catch
        {
            await FinishAcquireAsync(null);
            throw;
        }

        await FinishAcquireAsync(lease);
        return lease;
    }

    public async ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(
        RuntimeExecutionLease lease, Func<ValueTask<RuntimeExecutionOwnershipTransitionResult>> release)
    {
        // Delegate first, with the caller's original token captured by the decorator. A failure or refused release
        // cannot acknowledge quiescence; neither a held acquisition nor the acknowledgement lock blocks this call.
        var result = await release();
        if (!result.Succeeded && result.Status != RuntimeExecutionOwnershipTransitionStatus.AlreadyApplied)
            return result;

        await _reads.WaitAsync(CancellationToken.None);
        try
        {
            _leases.Remove(Identity(lease));
            await AcknowledgeIfQuiescentAsync(CancellationToken.None);
        }
        finally
        {
            _reads.Release();
        }

        return result;
    }

    private async Task FinishAcquireAsync(RuntimeExecutionLease? lease)
    {
        // Successful backend acquisition must remain tracked even if its caller was cancelled during the call.
        await _reads.WaitAsync(CancellationToken.None);
        try
        {
            _acquiring--;
            if (lease is not null && !_leases.Add(Identity(lease)))
                throw new InvalidOperationException("The native witness acquired an already-active execution lease.");
            await AcknowledgeIfQuiescentAsync(CancellationToken.None);
        }
        finally
        {
            _reads.Release();
        }
    }

    private async Task<string?> AcknowledgeIfQuiescentAsync(CancellationToken cancellationToken)
    {
        if (_acquiring != 0 || _leases.Count != 0)
            return null;
        var holdPath = Path.Combine(journalDirectory, $"hold-consumption-{role}");
        if (!File.Exists(holdPath))
            return null;
        var token = await File.ReadAllTextAsync(holdPath, cancellationToken);
        if (StringComparer.Ordinal.Equals(_acknowledgedToken, token))
            return token;

        await BackgroundWitnessJournal.WriteJsonAtomicallyAsync(
            Path.Combine(journalDirectory, $"consumption-held.{role}.json"), new
            {
                Stage = "consumption-held",
                Role = role,
                ProcessId = Environment.ProcessId,
                RecordedAt = DateTimeOffset.UtcNow,
                HoldToken = token
            }, cancellationToken, overwrite: true);
        _acknowledgedToken = token;
        return token;
    }

    private static (string, string, string, long) Identity(RuntimeExecutionLease lease) =>
        (lease.WorkflowExecutionId, lease.LeaseId, lease.OwnerId, lease.FencingToken);

    public void Dispose() => _reads.Dispose();
}
