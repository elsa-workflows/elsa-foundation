namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>Cooperative handoff from a live actor turn to a waiting control command. Never grants write authority.</summary>
internal sealed class RuntimeDrainControlContext
{
    private readonly AsyncLocal<Frame?> current = new();

    public bool ShouldYield(string executionId) => current.Value is { } frame &&
        StringComparer.Ordinal.Equals(frame.ExecutionId, executionId) && frame.Control.IsPending;

    public IDisposable Push(string executionId, RuntimeDrainControl? control)
    {
        var prior = current.Value;
        current.Value = control is null ? null : new(executionId, control);
        return new Restore(this, prior);
    }

    private sealed record Frame(string ExecutionId, RuntimeDrainControl Control);
    private sealed class Restore(RuntimeDrainControlContext owner, Frame? prior) : IDisposable
    {
        public void Dispose() => owner.current.Value = prior;
    }
}

internal sealed class RuntimeDrainControl
{
    private int waiting;
    public bool IsPending => Volatile.Read(ref waiting) > 0;

    public IDisposable Wait()
    {
        Interlocked.Increment(ref waiting);
        return new Registration(this);
    }

    private sealed class Registration(RuntimeDrainControl owner) : IDisposable
    {
        private RuntimeDrainControl? current = owner;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref current, null) is { } registered)
                Interlocked.Decrement(ref registered.waiting);
        }
    }
}
