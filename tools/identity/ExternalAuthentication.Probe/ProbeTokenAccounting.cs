// Owned qualification host only. Pair the ordinary interactive exchange count with its database snapshot.
internal sealed class ProbeTokenAccounting
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, long> completed = new(StringComparer.Ordinal);

    public async Task ExchangeAsync(Func<Task<string?>> exchange, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var subject = await exchange();
            if (subject is not null) completed[subject] = completed.GetValueOrDefault(subject) + 1;
        }
        finally { gate.Release(); }
    }

    public async Task<T> SnapshotAsync<T>(string? subject, Func<long, Task<T>> snapshot, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await snapshot(subject is null ? 0 : completed.GetValueOrDefault(subject)); }
        finally { gate.Release(); }
    }
}
