using Elsa.Attention.Core;

namespace Elsa.Foundation.Host.Shells;

/// <summary>
/// One Attention item for every shell that is not active and that the host has tried to activate and could not: a warning while
/// the host retries a fault, critical when an EF module refused the activation and an operator has to resolve it. The item goes
/// when the shell is active, by any path (see <see cref="ShellActivationTracker"/>).
/// </summary>
/// <remarks>
/// <para>
/// A host-level <see cref="IAttentionContributor"/>, registered as an instance on the host's container, which CShells copies into
/// every shell, so any active shell's Attention endpoint lists it: the contract the modules contribute through, from the one
/// place that knows a shell is not running.
/// </para>
/// <para>
/// It is served by an active shell, so when the only shell is the one that is down, nothing can serve it. That host's operators
/// see the failure on <c>/health/ready</c> (a stable reason code, the attempts and the next attempt) and in the host log, which
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
