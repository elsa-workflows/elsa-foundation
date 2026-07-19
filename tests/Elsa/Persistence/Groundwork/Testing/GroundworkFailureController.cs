using System.Collections.Concurrent;

namespace Elsa.Persistence.Groundwork.Testing;

public readonly record struct GroundworkFailureWindow
{
    public GroundworkFailureWindow(string value)
    {
        EvidenceCatalog.EnsureSlug(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public sealed class InjectedGroundworkFailureException(GroundworkFailureWindow window)
    : Exception($"A deterministic failure was injected at '{window.Value}'.")
{
    public GroundworkFailureWindow Window { get; } = window;
}

public sealed class GroundworkFailureController
{
    private readonly ConcurrentDictionary<GroundworkFailureWindow, ArmedAction> _armed = new();
    private readonly ConcurrentQueue<GroundworkFailureWindow> _reached = new();

    public IReadOnlyList<GroundworkFailureWindow> ReachedWindows => _reached.ToArray();

    public void FailAt(GroundworkFailureWindow window, Func<Exception>? exceptionFactory = null) =>
        Arm(window, new ArmedAction(FailureAction.Fail, exceptionFactory));

    public void CancelAt(GroundworkFailureWindow window) =>
        Arm(window, new ArmedAction(FailureAction.Cancel, null));

    public GroundworkFailurePause PauseAt(GroundworkFailureWindow window)
    {
        var pause = new GroundworkFailurePause(window);
        Arm(window, new ArmedAction(FailureAction.Pause, null, pause));
        return pause;
    }

    public async ValueTask ReachAsync(GroundworkFailureWindow window, CancellationToken cancellationToken = default)
    {
        _reached.Enqueue(window);
        if (!_armed.TryRemove(window, out var armed))
            return;

        switch (armed.Action)
        {
            case FailureAction.Fail:
                throw armed.ExceptionFactory?.Invoke() ?? new InjectedGroundworkFailureException(window);
            case FailureAction.Cancel:
                throw new OperationCanceledException(
                    $"Cancellation was injected at '{window.Value}'.",
                    new CancellationToken(canceled: true));
            case FailureAction.Pause:
                await armed.Pause!.ReachAsync(cancellationToken);
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Reset()
    {
        foreach (var action in _armed.Values)
            action.Pause?.Release();
        _armed.Clear();
        while (_reached.TryDequeue(out _))
        {
        }
    }

    private void Arm(GroundworkFailureWindow window, ArmedAction action)
    {
        if (!_armed.TryAdd(window, action))
            throw new InvalidOperationException($"Failure window '{window.Value}' is already armed.");
    }

    private enum FailureAction
    {
        Fail,
        Cancel,
        Pause
    }

    private sealed record ArmedAction(
        FailureAction Action,
        Func<Exception>? ExceptionFactory,
        GroundworkFailurePause? Pause = null);
}

public sealed class GroundworkFailurePause
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal GroundworkFailurePause(GroundworkFailureWindow window) => Window = window;

    public GroundworkFailureWindow Window { get; }
    public Task WaitUntilReachedAsync(CancellationToken cancellationToken = default) => _reached.Task.WaitAsync(cancellationToken);
    public void Release() => _released.TrySetResult();

    internal async ValueTask ReachAsync(CancellationToken cancellationToken)
    {
        _reached.TrySetResult();
        await _released.Task.WaitAsync(cancellationToken);
    }
}
