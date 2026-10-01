using Elsa.Locking.Database;
using Medallion.Threading;

namespace Elsa.Locking.Tests;

/// <summary>
/// The database lock adapter's own logic, against a stub Medallion provider (#2192): that a caller's timeout is passed
/// through, that the feature's default fills in when a caller passes none, and that a lock another node holds is a null
/// handle rather than a wrapper around nothing.
/// </summary>
public sealed class MedallionDistributedLockProviderTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(7);

    private readonly StubProvider _stub = new();
    private readonly MedallionDistributedLockProvider _provider;

    public MedallionDistributedLockProviderTests() => _provider = new(_stub, DefaultTimeout);

    [Fact]
    public async Task Fills_in_the_default_timeout_when_a_caller_passes_none()
    {
        _provider.TryAcquireLock("sync");
        await _provider.TryAcquireLockAsync("try");
        await _provider.AcquireLockAsync("acquire");

        Assert.Equal([DefaultTimeout, DefaultTimeout, DefaultTimeout], _stub.Timeouts);
    }

    [Fact]
    public async Task Passes_a_callers_timeout_through()
    {
        var timeout = TimeSpan.FromSeconds(3);

        _provider.TryAcquireLock("sync", timeout);
        await _provider.TryAcquireLockAsync("try", timeout);
        await _provider.AcquireLockAsync("acquire", timeout);

        Assert.Equal([timeout, timeout, timeout], _stub.Timeouts);
    }

    [Fact]
    public async Task Returns_null_when_the_lock_is_not_acquired()
    {
        _stub.Handle = null;

        Assert.Null(_provider.TryAcquireLock("held"));
        Assert.Null(await _provider.TryAcquireLockAsync("held"));
    }

    [Fact]
    public async Task Wraps_an_acquired_handle_and_forwards_its_lost_token_and_disposal()
    {
        using var lost = new CancellationTokenSource();
        var inner = new StubHandle(lost.Token);
        _stub.Handle = inner;

        var handle = await _provider.TryAcquireLockAsync("free");

        Assert.NotNull(handle);
        Assert.Equal(lost.Token, handle.HandleLostToken);
        await handle.DisposeAsync();
        Assert.True(inner.Disposed);
    }

    private sealed class StubProvider : Medallion.Threading.IDistributedLockProvider
    {
        public List<TimeSpan?> Timeouts { get; } = [];
        public IDistributedSynchronizationHandle? Handle { get; set; } = new StubHandle(CancellationToken.None);

        public IDistributedLock CreateLock(string name) => new StubLock(name, this);
    }

    private sealed class StubLock(string name, StubProvider owner) : IDistributedLock
    {
        public string Name => name;

        public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            owner.Timeouts.Add(timeout);
            return owner.Handle;
        }

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            owner.Timeouts.Add(timeout);
            return new(owner.Handle);
        }

        public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            owner.Timeouts.Add(timeout);
            return owner.Handle!;
        }

        public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            owner.Timeouts.Add(timeout);
            return new(owner.Handle!);
        }
    }

    private sealed class StubHandle(CancellationToken lost) : IDistributedSynchronizationHandle
    {
        public bool Disposed { get; private set; }
        public CancellationToken HandleLostToken => lost;
        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }
}
