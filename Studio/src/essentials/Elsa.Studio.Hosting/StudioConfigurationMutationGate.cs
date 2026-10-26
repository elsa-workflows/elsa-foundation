namespace Elsa.Studio.Hosting;

/// <summary>Coordinates whole configuration read/revision/write/reload operations in one running host.</summary>
public sealed class StudioConfigurationMutationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal StudioConfigurationMutationGate() { }

    /// <summary>Acquires exclusive access until the returned borrowed lease is disposed.</summary>
    public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
