using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Testing;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

internal static class AspNetCoreIdentityV2AcceptanceCatalog
{
    private static readonly Replacement NativeProviderReplacement = new(
        typeof(AspNetCoreIdentityNativeProviderScenario),
        nameof(AspNetCoreIdentityNativeProviderScenario.RunAsync));

    private static readonly Replacement RestartReplacement = new(
        typeof(IdentityProcessProbeRunner),
        nameof(IdentityProcessProbeRunner.RunAsync));

    public static IReadOnlyList<string> RequiredObjectiveIds { get; } =
    [
        "atomicity.injected-failure-does-not-leave-partial-state",
        "cancellation.pre-cancelled-load-is-cancelled",
        "concurrency.duplicate-normalized-user-name-has-one-winner",
        "concurrency.external-login-owner-has-one-winner",
        "delete.user-delete-removes-relationships-after-success",
        "framework-capability.role-claim-store",
        "framework-capability.role-store",
        "framework-capability.user-authentication-token-store",
        "framework-capability.user-authenticator-key-store",
        "framework-capability.user-claim-store",
        "framework-capability.user-email-store",
        "framework-capability.user-lockout-store",
        "framework-capability.user-login-store",
        "framework-capability.user-password-store",
        "framework-capability.user-phone-number-store",
        "framework-capability.user-role-store",
        "framework-capability.user-security-stamp-store",
        "framework-capability.user-store",
        "framework-capability.user-two-factor-recovery-code-store",
        "framework-capability.user-two-factor-store",
        "failure-window.lost-acknowledgement-reconciles-committed-result",
        "lifecycle.close-reopen-preserves-authority",
        "lifecycle.expired-mutation-receipt-is-cleaned",
        "lifecycle.process-restart-preserves-authority",
        "tenancy.cross-scope-read-is-not-disclosed"
    ];

    public static IReadOnlyDictionary<string, Replacement> Replacements { get; } =
        new Dictionary<string, Replacement>(StringComparer.Ordinal)
        {
            ["atomicity.injected-failure-does-not-leave-partial-state"] = new(
                typeof(AspNetCoreIdentityReconciliationTests),
                nameof(AspNetCoreIdentityReconciliationTests.Failure_after_domain_staging_but_before_commit_rolls_back_domain_state_and_receipt)),
            ["cancellation.pre-cancelled-load-is-cancelled"] = NativeProviderReplacement,
            ["concurrency.duplicate-normalized-user-name-has-one-winner"] = NativeProviderReplacement,
            ["concurrency.external-login-owner-has-one-winner"] = NativeProviderReplacement,
            ["delete.user-delete-removes-relationships-after-success"] = NativeProviderReplacement,
            ["framework-capability.role-claim-store"] = NativeProviderReplacement,
            ["framework-capability.role-store"] = NativeProviderReplacement,
            ["framework-capability.user-authentication-token-store"] = NativeProviderReplacement,
            ["framework-capability.user-authenticator-key-store"] = NativeProviderReplacement,
            ["framework-capability.user-claim-store"] = NativeProviderReplacement,
            ["framework-capability.user-email-store"] = NativeProviderReplacement,
            ["framework-capability.user-lockout-store"] = NativeProviderReplacement,
            ["framework-capability.user-login-store"] = NativeProviderReplacement,
            ["framework-capability.user-password-store"] = NativeProviderReplacement,
            ["framework-capability.user-phone-number-store"] = NativeProviderReplacement,
            ["framework-capability.user-role-store"] = NativeProviderReplacement,
            ["framework-capability.user-security-stamp-store"] = NativeProviderReplacement,
            ["framework-capability.user-store"] = NativeProviderReplacement,
            ["framework-capability.user-two-factor-recovery-code-store"] = NativeProviderReplacement,
            ["framework-capability.user-two-factor-store"] = NativeProviderReplacement,
            ["failure-window.lost-acknowledgement-reconciles-committed-result"] = new(
                typeof(AspNetCoreIdentityReconciliationTests),
                nameof(AspNetCoreIdentityReconciliationTests.Atomic_write_reconciles_lost_acknowledgement_after_the_row_was_committed)),
            ["lifecycle.close-reopen-preserves-authority"] = RestartReplacement,
            ["lifecycle.expired-mutation-receipt-is-cleaned"] = new(
                typeof(AspNetCoreIdentityReconciliationTests),
                nameof(AspNetCoreIdentityReconciliationTests.Expired_mutation_receipt_is_reclaimed_instead_of_replayed)),
            ["lifecycle.process-restart-preserves-authority"] = RestartReplacement,
            ["tenancy.cross-scope-read-is-not-disclosed"] = NativeProviderReplacement
        };

    public static void RequireExactCoverage(IEnumerable<string> observedObjectiveIds)
    {
        ArgumentNullException.ThrowIfNull(observedObjectiveIds);
        var observed = observedObjectiveIds.ToHashSet(StringComparer.Ordinal);
        var missing = RequiredObjectiveIds
            .Except(observed, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unexpected = observed
            .Except(RequiredObjectiveIds, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length == 0 && unexpected.Length == 0)
            return;

        throw new InvalidOperationException(
            $"Identity v2 acceptance coverage mismatch: missing=[{string.Join(", ", missing)}] " +
            $"unexpected=[{string.Join(", ", unexpected)}].");
    }

    internal sealed record Replacement(Type TestType, string MethodName);
}
