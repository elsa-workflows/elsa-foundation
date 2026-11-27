namespace Elsa.Locking.Core;

/// <summary>
/// A handle representing an acquired distributed lock or synchronization primitive.
/// Dispose (synchronously or asynchronously) to release.
/// </summary>
public interface IDistributedSynchronizationHandle : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// A token that fires when the handle is lost before disposal — for example, when
    /// the connection backing the lock is disrupted. Implementations that cannot detect
    /// handle loss return <see cref="CancellationToken.None"/>; callers can distinguish
    /// via <see cref="CancellationToken.CanBeCanceled"/>. Reading this property may incur
    /// runtime cost (e.g. polling) on implementations that do support detection.
    /// </summary>
    CancellationToken HandleLostToken { get; }
}