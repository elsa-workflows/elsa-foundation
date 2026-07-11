namespace Elsa.Server.Readiness;

public sealed class ShellReadinessState(TimeProvider timeProvider)
{
    private readonly object _syncRoot = new();
    private ShellReadinessSnapshot _snapshot = new(
        ShellReadinessStatus.NotStarted,
        "shell_activation_not_started");
    private long _startedTimestamp;

    public ShellReadinessSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public bool TryBegin(string shellName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);

        lock (_syncRoot)
        {
            if (_snapshot.Status != ShellReadinessStatus.NotStarted)
                return false;

            _startedTimestamp = timeProvider.GetTimestamp();
            Volatile.Write(ref _snapshot, new ShellReadinessSnapshot(
                ShellReadinessStatus.Starting,
                "shell_activation_pending",
                shellName,
                Attempt: 1,
                StartedAt: timeProvider.GetUtcNow()));
            return true;
        }
    }

    public void MarkReady(int generation) => Complete(
        ShellReadinessStatus.Ready,
        "shell_active",
        generation);

    public void MarkFailed(string code) => Complete(
        ShellReadinessStatus.Failed,
        code,
        generation: null);

    public void MarkDisabled(string shellName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);

        lock (_syncRoot)
        {
            if (_snapshot.Status != ShellReadinessStatus.NotStarted)
                return;

            Volatile.Write(ref _snapshot, new ShellReadinessSnapshot(
                ShellReadinessStatus.Disabled,
                "shell_warmup_disabled",
                shellName));
        }
    }

    private void Complete(ShellReadinessStatus status, string code, int? generation)
    {
        lock (_syncRoot)
        {
            if (_snapshot.Status != ShellReadinessStatus.Starting)
                return;

            var completedAt = timeProvider.GetUtcNow();
            Volatile.Write(ref _snapshot, _snapshot with
            {
                Status = status,
                Code = code,
                Generation = generation,
                CompletedAt = completedAt,
                Duration = timeProvider.GetElapsedTime(_startedTimestamp)
            });
        }
    }
}
