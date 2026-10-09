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
/// <param name="FailureType">The exception type that stopped the last attempt, or a safe outcome such as NotCurrent. Nothing of an exception message: a driver's message can echo a connection string, so the host log is where that is read.</param>
/// <param name="Refusal">The EF module's refusal when that is what stopped it, which an operator resolves and a retry does not.</param>
/// <param name="FirstFailedAt">When the first of these attempts failed.</param>
/// <param name="LastFailedAt">When the last attempt failed.</param>
/// <param name="RetryDelay">The delay selected by the last retry decision, from <paramref name="LastFailedAt"/>.</param>
public sealed record ShellActivationFailure(
    string Shell,
    int Attempts,
    string FailureType,
    ShellActivationRefusal? Refusal,
    DateTimeOffset FirstFailedAt,
    DateTimeOffset LastFailedAt,
    TimeSpan RetryDelay)
{
    /// <summary>The due time selected by the last retry decision; another activation path may finish recovery before then.</summary>
    public DateTimeOffset NextAttemptAt => LastFailedAt + RetryDelay;
}

/// <summary>
/// What the host knows of the shells it could not activate: written by <see cref="EagerShellActivationHostedService"/> as its
/// attempts fail and succeed, read by the readiness probe (<see cref="Health.HealthEndpoints"/>) and the Attention
/// contributor (<see cref="ShellActivationAttentionContributor"/>). A shell appears here from its first failed activation until
/// an active transition is observed, by any path: the retry that activates it, a request that does, or a reload.
/// </summary>
/// <remarks>
/// <para>
/// Host-level state. The contributor is read from inside a shell, while the failing one is not running, so the one instance is
/// registered on the host's container (<c>Program.cs</c>) and a shell's copy of that registration is the same object.
/// </para>
/// <para>
/// Once <see cref="Observe"/> has attached the registry, the tracker is also the registry's lifecycle subscriber, which forgets
/// a shell the moment it becomes active, and it reports only shells the registry does not hold active, so a record that outlived
/// a lazy activation is never read. Failures use the runner callback's timestamp; <see cref="TimeProvider"/> stamps Attention
/// observations. The host selects retry delays and jitter, while CShells owns scheduling.
/// </para>
/// </remarks>
public sealed class ShellActivationTracker(TimeProvider? timeProvider = null) : IShellLifecycleSubscriber
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ShellActivationFailure> _failing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _attemptBases = new(StringComparer.OrdinalIgnoreCase);
    private IShellRegistry? _registry;

    /// <summary>The clock that stamps Attention observations.</summary>
    public TimeProvider TimeProvider { get; } = timeProvider ?? TimeProvider.System;

    /// <summary>Attaches <paramref name="registry"/>: what it holds active is not failing, and a shell it activates is forgotten.</summary>
    public void Observe(IShellRegistry registry)
    {
        lock (_gate)
        {
            // Idempotent: a second start of the host on the same registry does not subscribe the tracker twice.
            if (ReferenceEquals(_registry, registry))
                return;

            _registry?.Unsubscribe(this);
            _registry = registry;
        }

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
    /// Projects a runner callback into the existing failure row and selects its one retry delay. The runner's cumulative
    /// attempt number is the source of sequence; this tracker only keeps the offset needed to preserve Elsa's consecutive
    /// visible-failure semantics across a reset. A failure hidden by a raw Active read retains a previous row, but rebases the
    /// projection so the hidden callback does not inflate the next visible count.
    /// </summary>
    public ShellActivationFailure FailedAttempt(
        string shell,
        int attemptNumber,
        string failureType,
        ShellActivationRefusal? refusal,
        DateTimeOffset failedAt,
        EagerShellActivationRetryOptions retry)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptNumber);

        lock (_gate)
        {
            var previous = _failing.GetValueOrDefault(shell);
            var active = IsActive(shell);
            long attemptBase;
            int attempts;

            if (active)
            {
                // A raw Active read suppresses this write but is not proof of a lifecycle reset. Preserve any old row and
                // move its offset past the suppressed callback so the next visible failure increments only once.
                attempts = (previous?.Attempts ?? 0) + 1;
                if (previous is null)
                    _attemptBases.Remove(shell);
                else
                    _attemptBases[shell] = (long)attemptNumber - previous.Attempts;
            }
            else
            {
                if (!_attemptBases.TryGetValue(shell, out attemptBase))
                {
                    // If a prior row survived a new runner instance, continue its visible failure streak. Otherwise this is
                    // the first visible failure after an observed reset (or the first row for this shell).
                    attemptBase = (long)attemptNumber - (previous?.Attempts ?? 0) - 1;
                    _attemptBases[shell] = attemptBase;
                }

                attempts = ProjectAttemptCount(attemptNumber, attemptBase);
            }

            var failure = new ShellActivationFailure(
                shell,
                attempts,
                failureType,
                refusal,
                previous?.FirstFailedAt ?? failedAt,
                failedAt,
                retry.NextDelay(attempts, refused: refusal is not null));

            if (!active)
                _failing[shell] = failure;

            return failure;
        }
    }

    /// <summary>Forgets the failure of <paramref name="shell"/>: it is active.</summary>
    public void Activated(string shell)
    {
        lock (_gate)
        {
            _failing.Remove(shell);
            _attemptBases.Remove(shell);
        }
    }

    /// <summary>Forgets a shell as it becomes active, whoever activated it: the eager attempt, a request, a reload.</summary>
    public Task OnStateChangedAsync(IShell shell, ShellLifecycleState previous, ShellLifecycleState current, CancellationToken cancellationToken = default)
    {
        if (current == ShellLifecycleState.Active)
            Activated(shell.Descriptor.Name);

        return Task.CompletedTask;
    }

    private bool IsActive(string shell) => _registry?.GetActive(shell)?.State == ShellLifecycleState.Active;

    private static int ProjectAttemptCount(int attemptNumber, long attemptBase) =>
        (int)Math.Clamp((long)attemptNumber - attemptBase, 1, int.MaxValue);
}
