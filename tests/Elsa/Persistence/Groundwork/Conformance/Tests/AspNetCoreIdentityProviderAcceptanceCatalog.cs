using System.Security.Cryptography;
using System.Text;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// The provider-independent denominator for the first physical ASP.NET Core Identity acceptance slice.
/// Objective ids describe only behavior executed by <see cref="AspNetCoreIdentityProviderAcceptanceRunner"/>;
/// later acceptance slices extend this catalog instead of making provider-specific evidence claims.
/// </summary>
internal static class AspNetCoreIdentityProviderAcceptanceCatalog
{
    public static IReadOnlyList<string> AdvertisedFrameworkCapabilityIds { get; } =
    [
        "role-claim-store",
        "role-store",
        "user-authentication-token-store",
        "user-authenticator-key-store",
        "user-claim-store",
        "user-email-store",
        "user-lockout-store",
        "user-login-store",
        "user-password-store",
        "user-phone-number-store",
        "user-role-store",
        "user-security-stamp-store",
        "user-store",
        "user-two-factor-recovery-code-store",
        "user-two-factor-store"
    ];

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

    public static IReadOnlyList<string> DeferredObjectiveIds { get; } = [];

    public static void RequireExactCoverage(IEnumerable<string> observedObjectiveIds)
    {
        RequireExact("Identity provider acceptance coverage", RequiredObjectiveIds, observedObjectiveIds);
    }

    public static void RequireExactAdvertisedFrameworkCapabilities(IEnumerable<string> observedCapabilityIds)
    {
        RequireExact(
            "Identity provider advertised framework capability coverage",
            AdvertisedFrameworkCapabilityIds,
            observedCapabilityIds);
    }

    public static string ComputeObjectiveResultDigest(IEnumerable<string> observedObjectiveIds)
    {
        var observed = observedObjectiveIds.Order(StringComparer.Ordinal).ToArray();
        RequireExactCoverage(observed);
        return Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', observed.Select(identity => $"{identity}=pass")))));
    }

    private static void RequireExact(
        string subject,
        IReadOnlyCollection<string> required,
        IEnumerable<string> observedValues)
    {
        ArgumentNullException.ThrowIfNull(observedValues);
        var observed = observedValues.ToHashSet(StringComparer.Ordinal);
        var missing = required.Except(observed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var unexpected = observed.Except(required, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        if (missing.Length == 0 && unexpected.Length == 0)
            return;

        throw new InvalidOperationException(
            $"{subject} mismatch: missing=[{string.Join(", ", missing)}] " +
            $"unexpected=[{string.Join(", ", unexpected)}].");
    }
}
