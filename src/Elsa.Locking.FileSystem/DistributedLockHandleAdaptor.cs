namespace Elsa.Locking.FileSystem
{
    public sealed class DistributedLockHandleAdaptor(Medallion.Threading.IDistributedSynchronizationHandle handle) : Elsa.Locking.Core.IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => handle.HandleLostToken;

        public void Dispose() => handle.Dispose();

        public ValueTask DisposeAsync() => handle.DisposeAsync();
    }
}
