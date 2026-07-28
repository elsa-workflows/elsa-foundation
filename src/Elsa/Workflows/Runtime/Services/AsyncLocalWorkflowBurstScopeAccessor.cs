using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed burst-scope accessor (ADR 0031 item b, spec 111). Mirrors
/// <see cref="AsyncLocalRuntimeLiveDrainDeliveryAccessor"/>: pushes form a stack so nested scopes restore their prior
/// frame on dispose and the flow is confined to the pushing async context (the drain loop), following the async flow
/// even when a handler opens its own DI scope.
/// </summary>
public sealed class AsyncLocalWorkflowBurstScopeAccessor : IWorkflowBurstScopeAccessor
{
    private readonly AsyncLocal<Frame?> _current = new();

    public WorkflowBurstScope? Current => _current.Value?.Scope;

    public IDisposable Push(WorkflowBurstScope? scope)
    {
        var prior = _current.Value;
        _current.Value = new Frame(scope, prior);
        return new PopWhenDisposed(this, prior);
    }

    private sealed record Frame(WorkflowBurstScope? Scope, Frame? Prior);

    private sealed class PopWhenDisposed(AsyncLocalWorkflowBurstScopeAccessor accessor, Frame? prior) : IDisposable
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
