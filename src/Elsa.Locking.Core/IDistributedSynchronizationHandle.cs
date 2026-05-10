namespace Elsa.Locking.Core
{
    //
    // Summary:
    //     A handle to a distributed lock or other synchronization primitive. To unlock/release,
    //     simply dispose the handle.
    public interface IDistributedSynchronizationHandle : IDisposable, IAsyncDisposable
    {
        //
        // Summary:
        //     Gets a System.Threading.CancellationToken instance which may be used to monitor
        //     whether the handle to the lock is lost before the handle is disposed. For example,
        //     this could happen if the lock is backed by a database and the connection to the
        //     database is disrupted. Not all lock types support this; those that don't will
        //     return System.Threading.CancellationToken.None which can be detected by checking
        //     System.Threading.CancellationToken.CanBeCanceled. For lock types that do support
        //     this, accessing this property may incur additional costs, such as polling to
        //     detect connectivity loss.
        CancellationToken HandleLostToken { get; }
    }
}
