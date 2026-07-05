using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed coalescing session accessor. Mirrors
/// <see cref="AsyncLocalRuntimeExecutionOwnershipContextAccessor"/>: pushes form a stack so nested scopes restore
/// their prior frame on dispose and the flow is confined to the pushing async context.
/// </summary>
public sealed class AsyncLocalRuntimeCoalescingSessionAccessor : IRuntimeCoalescingSessionAccessor
{
    private readonly AsyncLocal<Frame?> _current = new();

    public RuntimeCoalescingSession? Current => _current.Value?.Session;

    public IDisposable Push(RuntimeCoalescingSession? session)
    {
        var prior = _current.Value;
        _current.Value = new Frame(session, prior);
        return new PopWhenDisposed(this, prior);
    }

    private sealed record Frame(RuntimeCoalescingSession? Session, Frame? Prior);

    private sealed class PopWhenDisposed(AsyncLocalRuntimeCoalescingSessionAccessor accessor, Frame? prior) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            accessor._current.Value = prior;
            _disposed = true;
        }
    }
}
