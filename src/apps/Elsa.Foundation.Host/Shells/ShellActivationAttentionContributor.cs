using Elsa.Attention.Core;

namespace Elsa.Foundation.Host.Shells;

/// <summary>
/// One Attention item for every shell the host could not activate and is still trying to: a warning while the host retries a
/// fault, critical when an EF module refused the activation and an operator has to resolve it. The item goes when the shell
/// activates.
/// </summary>
/// <remarks>
/// A host-level <see cref="IAttentionContributor"/>, registered as an instance on the host's container, which CShells copies into
/// every shell, so any active shell's Attention endpoint lists it: the contract the modules contribute through, from the one
/// place that knows a shell is not running. It carries the failure's type and never its message, which is the host log's (see
/// <see cref="ShellActivationFailure.FailureType"/>), and it is readable by whoever may read Attention at all.
/// </remarks>
public sealed class ShellActivationAttentionContributor(ShellActivationTracker tracker, TimeProvider? timeProvider = null) : IAttentionContributor
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public AttentionContributorDescriptor Descriptor { get; } = new("shell-activation", "Shell activation", RequiredPermission: null);

    public ValueTask<AttentionContribution> EvaluateAsync(AttentionContributorContext context, CancellationToken cancellationToken = default)
    {
        var observedAt = _timeProvider.GetUtcNow();
        AttentionItem[] items = [.. tracker.Failing().Select(failure => Item(failure, observedAt))];
        return ValueTask.FromResult(AttentionContribution.Ready(items));
    }

    private static AttentionItem Item(ShellActivationFailure failure, DateTimeOffset observedAt)
    {
        var refusal = failure.Refusal;
        var cause = refusal is null
            ? $"Activation failed {failure.Attempts} time(s), the last with {failure.FailureType}; the host log has the exception. The host retries"
            : $"EF module '{refusal.Module}' refused the activation ({refusal.Code}{(refusal.PendingMigrations.Count == 0 ? "" : $": {string.Join(", ", refusal.PendingMigrations)}")}), which an operator resolves. The host checks again";
        List<AttentionCorrelation> correlations = [new("shell", failure.Shell)];
        if (refusal is not null)
            correlations.Add(new("module", refusal.Module));

        return new(
            $"shell-activation:{failure.Shell}",
            $"{failure.FailureType}:{refusal?.Code}",
            refusal is null ? AttentionSeverity.Warning : AttentionSeverity.Critical,
            $"Shell '{failure.Shell}' is not active",
            $"{cause} at {failure.NextAttemptAt:u}.",
            failure.FirstFailedAt,
            observedAt,
            failure.Attempts,
            new("/health/ready", "Inspect readiness"),
            correlations,
            AttentionSensitivity.Metadata);
    }
}
