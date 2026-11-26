namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// Typed holder capturing the pre-decoration (durable) implementation of a runtime store contract so both the
/// coalescing decorator and the coalescing session can reach the inner store without re-resolving the decorated
/// registration (which would recurse). Registered with the persistence modes, which every run drains through. A store
/// the holder created from its registration is disposed with the holder, as the container disposed it before it was
/// wrapped; a store registered as an instance is not.
/// </summary>
public sealed class CoalescingInner<TService> : IDisposable, IAsyncDisposable where TService : class
{
    private readonly bool _ownsValue;

    public CoalescingInner(TService value) : this(value, ownsValue: false)
    {
    }

    internal CoalescingInner(TService value, bool ownsValue)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        _ownsValue = ownsValue;
    }

    public TService Value { get; }

    public void Dispose()
    {
        if (_ownsValue && Value is IDisposable disposable)
            disposable.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_ownsValue)
            return;

        if (Value is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (Value is IDisposable disposable)
            disposable.Dispose();
    }
}
