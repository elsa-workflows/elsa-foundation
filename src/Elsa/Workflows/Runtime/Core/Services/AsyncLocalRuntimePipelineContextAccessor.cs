using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed <see cref="IRuntimePipelineContextAccessor"/>. Push/pop nests, so a dispatch that
/// re-enters the pipeline restores the outer context on scope disposal. Mirrors <see cref="AsyncLocalWorkflowExecutionAmbientServicesAccessor"/>.
/// </summary>
public sealed class AsyncLocalRuntimePipelineContextAccessor : IRuntimePipelineContextAccessor
{
    private readonly AsyncLocal<Frame?> _current = new();

    public IRuntimePipelineContext? Current => _current.Value?.Context;

    public IDisposable Push(IRuntimePipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var prior = _current.Value;
        _current.Value = new Frame(context, prior);
        return new PopWhenDisposed(this, prior);
    }

    private sealed record Frame(IRuntimePipelineContext Context, Frame? Prior);

    private sealed class PopWhenDisposed(AsyncLocalRuntimePipelineContextAccessor accessor, Frame? prior) : IDisposable
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

/// <summary>No-op accessor for contexts with no active pipeline dispatch (e.g. a handler constructed directly in a test).</summary>
public sealed class NoopRuntimePipelineContextAccessor : IRuntimePipelineContextAccessor
{
    public static readonly NoopRuntimePipelineContextAccessor Instance = new();

    public IRuntimePipelineContext? Current => null;

    public IDisposable Push(IRuntimePipelineContext context) => NoopDisposable.Instance;

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
