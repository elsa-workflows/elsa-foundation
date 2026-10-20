namespace Elsa.Etl.Runtime.Execution;

/// <summary>Logical allowances held by an actual live attempt, including an expired or replaced attempt.</summary>
internal readonly record struct EtlAttemptCharge(long Records, long InputBytes, long LookupBytes, long OutputBytes);

/// <summary>
/// Process-local admission complements durable owed-batch quotas. Only disposal after the actual task unwinds
/// releases these allowances; there is deliberately no lease-expiry or replacement release operation.
/// These are logical payload allowances, not a managed-heap or RSS measurement.
/// </summary>
internal sealed class EtlAttemptBudget
{
    private readonly object _gate = new();
    private readonly int _maxAttempts;
    private readonly EtlAttemptCharge _capacity;
    private int _attempts;
    private EtlAttemptCharge _used;
    private TaskCompletionSource _changed = NewSignal();

    public EtlAttemptBudget(int maxAttempts, EtlAttemptCharge capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        if (capacity.Records <= 0 || capacity.InputBytes <= 0 || capacity.LookupBytes <= 0 || capacity.OutputBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _maxAttempts = maxAttempts;
        _capacity = capacity;
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(EtlAttemptCharge charge, CancellationToken cancellationToken = default)
    {
        if (charge.Records < 0 || charge.InputBytes < 0 || charge.LookupBytes < 0 || charge.OutputBytes < 0 || !Fits(charge, _capacity))
            throw new ArgumentOutOfRangeException(nameof(charge), "The ETL attempt exceeds its host's logical allowances.");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task changed;
            lock (_gate)
            {
                var available = new EtlAttemptCharge(_capacity.Records - _used.Records, _capacity.InputBytes - _used.InputBytes,
                    _capacity.LookupBytes - _used.LookupBytes, _capacity.OutputBytes - _used.OutputBytes);
                if (_attempts < _maxAttempts && Fits(charge, available))
                {
                    _attempts++;
                    _used = new(_used.Records + charge.Records, _used.InputBytes + charge.InputBytes,
                        _used.LookupBytes + charge.LookupBytes, _used.OutputBytes + charge.OutputBytes);
                    return new Permit(this, charge);
                }
                changed = _changed.Task;
            }
            await changed.WaitAsync(cancellationToken);
        }
    }

    private void Release(EtlAttemptCharge charge)
    {
        TaskCompletionSource changed;
        lock (_gate)
        {
            _attempts--;
            _used = new(_used.Records - charge.Records, _used.InputBytes - charge.InputBytes,
                _used.LookupBytes - charge.LookupBytes, _used.OutputBytes - charge.OutputBytes);
            changed = _changed;
            _changed = NewSignal();
        }
        changed.TrySetResult();
    }

    private static bool Fits(EtlAttemptCharge charge, EtlAttemptCharge available) =>
        charge.Records <= available.Records && charge.InputBytes <= available.InputBytes &&
        charge.LookupBytes <= available.LookupBytes && charge.OutputBytes <= available.OutputBytes;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Permit(EtlAttemptBudget owner, EtlAttemptCharge charge) : IAsyncDisposable
    {
        private EtlAttemptBudget? _owner = owner;
        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _owner, null)?.Release(charge);
            return ValueTask.CompletedTask;
        }
    }
}
