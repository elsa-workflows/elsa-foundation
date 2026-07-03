using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed ownership scope accessor. Pushes form a stack so nested scopes restore their
/// prior frame on dispose, and the flow is confined to the pushing async context.
/// </summary>
public sealed class AsyncLocalRuntimeExecutionOwnershipContextAccessor : IRuntimeExecutionOwnershipContextAccessor
{
    private readonly AsyncLocal<Frame?> _current = new();

    public RuntimeExecutionLease? Current => _current.Value?.Lease;

    public IDisposable Push(RuntimeExecutionLease? lease)
    {
        var prior = _current.Value;
        _current.Value = new Frame(lease, prior);
        return new PopWhenDisposed(this, prior);
    }

    private sealed record Frame(RuntimeExecutionLease? Lease, Frame? Prior);

    private sealed class PopWhenDisposed(
        AsyncLocalRuntimeExecutionOwnershipContextAccessor accessor,
        Frame? prior) : IDisposable
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
