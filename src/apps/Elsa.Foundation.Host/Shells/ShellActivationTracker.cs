using CShells.Lifecycle;

namespace Elsa.Foundation.Host.Shells;

/// <summary>
/// What an EF module's refusal says of itself, however it was recognised, with the host's directory in its command. It is what
/// <see cref="ShellReloadFailure"/> reads out of a failed reload or activation, and what a shell's failure record keeps.
/// </summary>
/// <param name="Module">The EF module that refused.</param>
/// <param name="Code">One of the codes of <c>IEfModuleRefusal</c>, for example <c>pending-migrations</c>.</param>
/// <param name="PendingMigrations">The migrations the refusal concerns; empty when it concerns none.</param>
/// <param name="Command">The command that resolves it, when there is one an operator can run as it stands.</param>
public sealed record ShellActivationRefusal(string Module, string Code, IReadOnlyList<string> PendingMigrations, string? Command);

/// <summary>A shell the host has tried to activate and could not, and where its attempts stand.</summary>
/// <param name="Shell">The shell's name.</param>
/// <param name="Attempts">How many activations have failed, in a row, since the shell last was active.</param>
/// <param name="FailureType">The type of the exception that stopped the last attempt. Nothing of its message: a driver's message can echo a connection string, so the host log is where that is read.</param>
/// <param name="Refusal">The EF module's refusal when that is what stopped it, which an operator resolves and a retry does not.</param>
/// <param name="FirstFailedAt">When the first of these attempts failed.</param>
/// <param name="LastFailedAt">When the last attempt failed.</param>
/// <param name="RetryDelay">How long the host waits, from <paramref name="LastFailedAt"/>, before the next attempt.</param>
public sealed record ShellActivationFailure(
    string Shell,
    int Attempts,
    string FailureType,
    ShellActivationRefusal? Refusal,
    DateTimeOffset FirstFailedAt,
    DateTimeOffset LastFailedAt,
    TimeSpan RetryDelay)
{
    /// <summary>When the host tries again.</summary>
    public DateTimeOffset NextAttemptAt => LastFailedAt + RetryDelay;
}

/// <summary>
/// What the host knows of the shells it could not activate: written by <see cref="EagerShellActivationHostedService"/> as its
/// attempts fail and succeed, read by the readiness probe (<see cref="Health.HealthEndpoints"/>) and the Attention
/// contributor (<see cref="ShellActivationAttentionContributor"/>). A shell appears here from its first failed activation until
/// it is active, by any path: the retry that activates it, a request that does, or a reload.
/// </summary>
/// <remarks>
/// <para>
/// Host-level state. The contributor is read from inside a shell, while the failing one is not running, so the one instance is
/// registered on the host's container (<c>Program.cs</c>) and a shell's copy of that registration is the same object.
/// </para>
/// <para>
/// Once <see cref="Observe"/> has attached the registry, the tracker is also the registry's lifecycle subscriber, which forgets
/// a shell the moment it becomes active, and it reports only shells the registry does not hold active, so a record that outlived
/// a lazy activation is never read. It is the one clock of the retries too: <see cref="TimeProvider"/> stamps the failures and
/// runs the delays between them.
/// </para>
/// </remarks>
public sealed class ShellActivationTracker(TimeProvider? timeProvider = null) : IShellLifecycleSubscriber
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ShellActivationFailure> _failing = new(StringComparer.OrdinalIgnoreCase);
    private IShellRegistry? _registry;

    /// <summary>The clock that stamps the failures and runs the delays between attempts.</summary>
    public TimeProvider TimeProvider { get; } = timeProvider ?? TimeProvider.System;

    /// <summary>Attaches <paramref name="registry"/>: what it holds active is not failing, and a shell it activates is forgotten.</summary>
    public void Observe(IShellRegistry registry)
    {
        _registry = registry;
        registry.Subscribe(this);
    }

    /// <summary>The shells that have failed to activate and are not active, by name.</summary>
    public IReadOnlyList<ShellActivationFailure> Failing()
    {
        lock (_gate)
            return [.. _failing.Values.Where(failure => !IsActive(failure.Shell)).OrderBy(failure => failure.Shell, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The failure of <paramref name="shell"/>, or <see langword="null"/> when it has not failed since it last was active, or is active.</summary>
    public ShellActivationFailure? FailureOf(string shell)
    {
        lock (_gate)
            return _failing.GetValueOrDefault(shell) is { } failure && !IsActive(shell) ? failure : null;
    }

    /// <summary>
    /// Records one more failed activation of <paramref name="shell"/>, and with it how long the host waits before the next
    /// attempt, from <paramref name="retry"/> and the number of failed attempts, this one included.
    /// </summary>
    public ShellActivationFailure Failed(string shell, string failureType, ShellActivationRefusal? refusal, EagerShellActivationRetryOptions retry)
    {
        var now = TimeProvider.GetUtcNow();
        lock (_gate)
        {
            var previous = _failing.GetValueOrDefault(shell);
            var attempts = (previous?.Attempts ?? 0) + 1;
            return _failing[shell] = new ShellActivationFailure(
                shell, attempts, failureType, refusal, previous?.FirstFailedAt ?? now, now, retry.NextDelay(attempts, refused: refusal is not null));
        }
    }

    /// <summary>Forgets the failure of <paramref name="shell"/>: it is active.</summary>
    public void Activated(string shell)
    {
        lock (_gate)
            _failing.Remove(shell);
    }

    /// <summary>Forgets a shell as it becomes active, whoever activated it: the eager attempt, a request, a reload.</summary>
    public Task OnStateChangedAsync(IShell shell, ShellLifecycleState previous, ShellLifecycleState current, CancellationToken cancellationToken = default)
    {
        if (current == ShellLifecycleState.Active)
            Activated(shell.Descriptor.Name);

        return Task.CompletedTask;
    }

    private bool IsActive(string shell) => _registry?.GetActive(shell)?.State == ShellLifecycleState.Active;
}
