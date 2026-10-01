using System.Collections.Concurrent;

namespace Elsa.Foundation.Host.Shells;

/// <summary>An EF module's refusal behind a shell that would not activate, as far as a probe or an Attention reader may be told it.</summary>
/// <param name="Module">The EF module that refused.</param>
/// <param name="Code">One of the codes of <c>IEfModuleRefusal</c>, for example <c>pending-migrations</c>.</param>
/// <param name="PendingMigrations">The migrations the refusal concerns; empty when it concerns none.</param>
public sealed record ShellActivationRefusal(string Module, string Code, IReadOnlyList<string> PendingMigrations);

/// <summary>A shell the host has tried to activate and could not, and where its attempts stand.</summary>
/// <param name="Shell">The shell's name.</param>
/// <param name="Attempts">How many activations have failed, in a row, since the shell last was active.</param>
/// <param name="FailureType">The type of the exception that stopped the last attempt. Nothing of its message: a driver's message can echo a connection string, so the host log is where that is read.</param>
/// <param name="Refusal">The EF module's refusal when that is what stopped it, which an operator resolves and a retry does not.</param>
/// <param name="FirstFailedAt">When the first of these attempts failed.</param>
/// <param name="LastFailedAt">When the last attempt failed.</param>
/// <param name="NextAttemptAt">When the host tries again.</param>
public sealed record ShellActivationFailure(
    string Shell,
    int Attempts,
    string FailureType,
    ShellActivationRefusal? Refusal,
    DateTimeOffset FirstFailedAt,
    DateTimeOffset LastFailedAt,
    DateTimeOffset NextAttemptAt);

/// <summary>
/// What the host knows of the shells it could not activate: written by <see cref="EagerShellActivationHostedService"/> as its
/// attempts fail and succeed, read by the readiness probe (<see cref="Health.HealthEndpoints"/>) and the Attention
/// contributor (<see cref="ShellActivationAttentionContributor"/>). A shell appears here from its first failed activation until
/// the attempt that activates it.
/// </summary>
/// <remarks>
/// Host-level state. The contributor is read from inside a shell, while the failing one is not running, so the one instance is
/// registered on the host's container (<c>Program.cs</c>) and a shell's copy of that registration is the same object.
/// </remarks>
public sealed class ShellActivationTracker(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, ShellActivationFailure> _failing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shells that have failed to activate and not yet succeeded, by name.</summary>
    public IReadOnlyList<ShellActivationFailure> Failing() => [.. _failing.Values.OrderBy(failure => failure.Shell, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The failure of <paramref name="shell"/>, or <see langword="null"/> when it has not failed since it last was active.</summary>
    public ShellActivationFailure? FailureOf(string shell) => _failing.GetValueOrDefault(shell);

    /// <summary>
    /// Records one more failed activation of <paramref name="shell"/>. <paramref name="retryAfter"/> is given the number of failed
    /// attempts, this one included, and says how long the host waits before the next.
    /// </summary>
    public ShellActivationFailure Failed(string shell, string failureType, ShellActivationRefusal? refusal, Func<int, TimeSpan> retryAfter)
    {
        var now = _timeProvider.GetUtcNow();
        return _failing.AddOrUpdate(
            shell,
            _ => new(shell, 1, failureType, refusal, now, now, now + retryAfter(1)),
            (_, previous) => previous with
            {
                Attempts = previous.Attempts + 1,
                FailureType = failureType,
                Refusal = refusal,
                LastFailedAt = now,
                NextAttemptAt = now + retryAfter(previous.Attempts + 1)
            });
    }

    /// <summary>Forgets the failure of <paramref name="shell"/>: it is active.</summary>
    public void Activated(string shell) => _failing.TryRemove(shell, out _);
}
