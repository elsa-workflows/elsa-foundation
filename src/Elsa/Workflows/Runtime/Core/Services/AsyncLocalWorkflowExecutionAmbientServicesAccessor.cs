using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class AsyncLocalWorkflowExecutionAmbientServicesAccessor : IWorkflowExecutionAmbientServicesAccessor
{
    private readonly AsyncLocal<Frame?> _current = new();

    public IServiceProvider? Current => _current.Value?.Services;

    public IDisposable Push(IServiceProvider? services)
    {
        var prior = _current.Value;
        _current.Value = new Frame(services, prior);
        return new PopWhenDisposed(this, prior);
    }

    private sealed record Frame(IServiceProvider? Services, Frame? Prior);

    private sealed class PopWhenDisposed(
        AsyncLocalWorkflowExecutionAmbientServicesAccessor accessor,
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

public sealed class NoopWorkflowExecutionAmbientServicesAccessor : IWorkflowExecutionAmbientServicesAccessor
{
    public static readonly NoopWorkflowExecutionAmbientServicesAccessor Instance = new();

    public IServiceProvider? Current => null;

    public IDisposable Push(IServiceProvider? services) => NoopDisposable.Instance;

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
