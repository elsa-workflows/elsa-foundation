using Groundwork.Documents.Scoping;

namespace Elsa.Persistence.Groundwork.Scoping;

/// <summary>
/// Publishes one admitted provider-owned session factory at startup. It retains only static provider
/// resources; tenant/access state exists exclusively in the sessions returned by <see cref="OpenAsync"/>.
/// </summary>
public sealed class GroundworkStoreSessionSource : IGroundworkStoreSessionSource, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private Publication? _publication;
    private TaskCompletionSource? _openDrain;
    private TaskCompletionSource? _disposal;
    private int _activeOpens;
    private bool _disposed;

    public bool IsInitialized => Volatile.Read(ref _publication) is not null;

    public bool TrySet(
        Func<DocumentStoreAccess, CancellationToken, ValueTask<GroundworkStoreSessionResources>> openSession,
        IAsyncDisposable? providerLease = null)
    {
        ArgumentNullException.ThrowIfNull(openSession);
        lock (_gate)
        {
            if (_disposed || _publication is not null)
                return false;

            Volatile.Write(ref _publication, new(openSession, providerLease));
            return true;
        }
    }

    public async ValueTask<GroundworkStoreSessionResources> OpenAsync(
        DocumentStoreAccess access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        cancellationToken.ThrowIfCancellationRequested();

        Publication publication;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            publication = _publication ?? throw new InvalidOperationException(
                "The Groundwork store-session source has not been initialized. Run the selected provider initializer before resolving persistence stores.");
            _activeOpens++;
        }

        try
        {
            return await publication.OpenSession(access, cancellationToken);
        }
        finally
        {
            lock (_gate)
            {
                _activeOpens--;
                if (_activeOpens == 0)
                    _openDrain?.TrySetResult();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource disposal;
        Task openDrain;
        IAsyncDisposable? lease;
        lock (_gate)
        {
            if (_disposal is not null)
                return new(_disposal.Task);

            _disposed = true;
            disposal = _disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            openDrain = _activeOpens == 0
                ? Task.CompletedTask
                : (_openDrain = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            lease = _publication?.ProviderLease;
        }

        _ = CompleteDisposalAsync(openDrain, lease, disposal);

        return new(disposal.Task);
    }

    private static async Task CompleteDisposalAsync(
        Task openDrain,
        IAsyncDisposable? lease,
        TaskCompletionSource completion)
    {
        try
        {
            await openDrain;
            if (lease is not null)
                await lease.DisposeAsync();
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private sealed record Publication(
        Func<DocumentStoreAccess, CancellationToken, ValueTask<GroundworkStoreSessionResources>> OpenSession,
        IAsyncDisposable? ProviderLease);
}
