namespace Elsa.Studio.Hosting;

/// <summary>Coordinates whole configuration read/revision/write/reload operations in one process.</summary>
public sealed class StudioConfigurationMutationGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal StudioConfigurationMutationGate() { }

    /// <summary>Acquires exclusive access until the returned borrowed lease is disposed.</summary>
    public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(Gate);
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
