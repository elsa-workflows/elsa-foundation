using CShells.Lifecycle;

namespace Elsa.Foundation.Hosting;

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
/// Tracks consecutive activation failures for host diagnostics until the registry confirms a settled current generation.
/// </summary>
/// <remarks>
/// <para>
/// The profile registers one instance on the host that shells also use. Unsupported custom registries cannot clear failures.
/// </para>
/// <para>
/// An Active notification can precede settlement. Diagnostic reads check the registry again so a successful request or reload
/// can clear the failure even when its notification was provisional.
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

    internal void Observe(IShellRegistry registry)
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
        {
            foreach (var shell in _failing.Keys.ToArray())
                ObserveSettlement(shell);

            return [.. _failing.Values.OrderBy(failure => failure.Shell, StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>The failure of <paramref name="shell"/>, or <see langword="null"/> when it has not failed since it last was active, or is active.</summary>
    public ShellActivationFailure? FailureOf(string shell)
    {
        lock (_gate)
            return _failing.ContainsKey(shell) && !ObserveSettlement(shell) ? _failing[shell] : null;
    }

    /// <summary>
    /// Projects a runner callback into the existing failure row and selects its one retry delay. The runner's cumulative
    /// attempt number is the source of sequence; this tracker only keeps the offset needed to preserve Elsa's consecutive
    /// visible-failure semantics across a reset. An attempt superseded by a settled generation does not create a failure row.
    /// </summary>
    internal ShellActivationFailure FailedAttempt(
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
            var settled = ObserveSettlement(shell);
            var previous = _failing.GetValueOrDefault(shell);
            var attempts = 1;

            if (!settled)
            {
                if (!_attemptBases.TryGetValue(shell, out var attemptBase))
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

            if (!settled)
                _failing[shell] = failure;

            return failure;
        }
    }

    internal bool ObserveSettlement(string shell, IShell? expected = null)
    {
        lock (_gate)
        {
            var settled = (_registry as ISettledShellRegistry)?.GetSettledActive(shell);
            if (settled is null || (expected is not null && !ReferenceEquals(expected, settled)))
                return false;

            _failing.Remove(shell);
            _attemptBases.Remove(shell);
            return true;
        }
    }

    Task IShellLifecycleSubscriber.OnStateChangedAsync(IShell shell, ShellLifecycleState previous, ShellLifecycleState current, CancellationToken cancellationToken)
    {
        if (current == ShellLifecycleState.Active)
            ObserveSettlement(shell.Descriptor.Name, shell);

        return Task.CompletedTask;
    }


    private static int ProjectAttemptCount(int attemptNumber, long attemptBase) =>
        (int)Math.Clamp((long)attemptNumber - attemptBase, 1, int.MaxValue);
}
