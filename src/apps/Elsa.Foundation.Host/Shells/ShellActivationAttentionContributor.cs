using Elsa.Attention.Core;

namespace Elsa.Foundation.Host.Shells;

/// <summary>
/// One Attention item for every recorded activation failure whose shell is not currently active: a warning for a fault, critical
/// when an EF module refused activation and an operator has to resolve it. Its retry time describes the last selected decision;
/// it does not promise another attempt after startup recovery ends (see <see cref="ShellActivationTracker"/>).
/// </summary>
/// <remarks>
/// <para>
/// A host-level <see cref="IAttentionContributor"/>, registered as an instance on the host's container, which CShells copies into
/// every shell, so any active shell's Attention endpoint lists it: the contract the modules contribute through, from the one
/// place that records a shell's failed activation.
/// </para>
/// <para>
/// It is served by an active shell, so when the only shell is the one that is down, nothing can serve it. That host's operators
/// see the failure on <c>/health/ready</c> (a stable reason code, the attempts and the last selected retry time) and in the host log, which
/// has every failure with its exception.
/// </para>
/// <para>
/// This is where the detail is, because the probe is public and this is not: the item names the exception's type and, for a
/// refusal, the EF module and its pending migrations. Never a message, which can echo a connection string; the log has those.
/// It needs the module-management read permission, the one <c>ModularityAttentionContributor</c> asks for the module state it
/// reports, restated here because the host compiles no feature in.
/// </para>
/// </remarks>
public sealed class ShellActivationAttentionContributor(ShellActivationTracker tracker) : IAttentionContributor
{
    /// <summary>The permission of <c>ModuleManagementPermissionKeys.Read</c> in <c>Elsa.Modularity.Api</c>.</summary>
    public const string RequiredPermission = "module-management.read";

    public AttentionContributorDescriptor Descriptor { get; } = new("shell-activation", "Shell activation", RequiredPermission);

    public ValueTask<AttentionContribution> EvaluateAsync(AttentionContributorContext context, CancellationToken cancellationToken = default)
    {
        var observedAt = tracker.TimeProvider.GetUtcNow();
        AttentionItem[] items = [.. tracker.Failing().Select(failure => Item(failure, observedAt))];
        return ValueTask.FromResult(AttentionContribution.Ready(items));
    }

    private static AttentionItem Item(ShellActivationFailure failure, DateTimeOffset observedAt)
    {
        var refusal = failure.Refusal;
        var cause = refusal is null
            ? $"Activation failed {failure.Attempts} time(s), the last with {failure.FailureType}; the host log has the exception. The last retry decision was due"
            : $"EF module '{refusal.Module}' refused the activation ({refusal.Code}{(refusal.PendingMigrations.Count == 0 ? "" : $": {string.Join(", ", refusal.PendingMigrations)}")}), which an operator resolves. The last recheck decision was due";
        const string currentReadiness = "Check live readiness: startup recovery may have ended after another activation path completed.";
        List<AttentionCorrelation> correlations = [new("shell", failure.Shell)];
        if (refusal is not null)
            correlations.Add(new("module", refusal.Module));

        return new(
            $"shell-activation:{failure.Shell}",
            $"{failure.FailureType}:{refusal?.Code}",
            refusal is null ? AttentionSeverity.Warning : AttentionSeverity.Critical,
            $"Shell '{failure.Shell}' is not active",
            $"{cause} at {failure.NextAttemptAt:u}. {currentReadiness}",
            failure.FirstFailedAt,
            observedAt,
            failure.Attempts,
            new("/health/ready", "Inspect readiness"),
            correlations,
            AttentionSensitivity.Metadata);
    }
}
