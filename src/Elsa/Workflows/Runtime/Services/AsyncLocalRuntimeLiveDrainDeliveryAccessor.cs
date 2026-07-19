using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed live-drain delivery accessor. Mirrors
/// <see cref="Coalescing.AsyncLocalRuntimeCoalescingSessionAccessor"/>: pushes form a stack so nested scopes restore
/// their prior frame on dispose and the flow is confined to the pushing async context (the live drain loop).
/// </summary>
public sealed class AsyncLocalRuntimeLiveDrainDeliveryAccessor : IRuntimeLiveDrainDeliveryAccessor
{
    private readonly AsyncLocal<Frame?> _current = new();

    public RuntimeLiveDrainDeliveryScope? Current => _current.Value?.Scope;

    public IDisposable Push(RuntimeLiveDrainDeliveryScope? scope)
    {
        var prior = _current.Value;
        _current.Value = new Frame(scope, prior);
        return new PopWhenDisposed(this, prior);
    }

    private sealed record Frame(RuntimeLiveDrainDeliveryScope? Scope, Frame? Prior);

    private sealed class PopWhenDisposed(AsyncLocalRuntimeLiveDrainDeliveryAccessor accessor, Frame? prior) : IDisposable
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
