using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Admission;

internal sealed class JitProvisioningPolicy : IExternalProvisioningPolicy
{
    public string Name => "jit";
    public ValueTask<ExternalProvisioningDecision> AssessAsync(ExternalAdmissionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Connection.Record.Admission.Matcher is not null)
            return ValueTask.FromResult(new ExternalProvisioningDecision());
        return ValueTask.FromResult(new ExternalProvisioningDecision
        {
            Kind = ExternalAdmissionKind.NewUser, Failure = ExternalAdmissionFailure.None,
            Email = ClaimValue.Single(context.Claims, "email"), DisplayName = ClaimValue.Single(context.Claims, "name"),
            RoleIds = context.Connection.Record.Admission.DefaultRoleIds.ToArray()
        });
    }
}

internal sealed class MatchProvisioningPolicy(IUserStore users) : IExternalProvisioningPolicy
{
    public string Name => "match";
    public async ValueTask<ExternalProvisioningDecision> AssessAsync(ExternalAdmissionContext context, CancellationToken cancellationToken = default)
    {
        if (context.Connection.Record.Admission.Matcher != "verified-email") return new();
        var email = ClaimValue.Single(context.Claims, "email");
        var verification = context.Claims.Where(claim => claim.Type == "email_verified").ToArray();
        if (string.IsNullOrWhiteSpace(email) || verification.Length != 1 || verification[0].Value != "true" || verification[0].ValueType != ClaimValueTypes.Boolean)
            return new() { Failure = ExternalAdmissionFailure.VerifiedEmailRequired };
        var user = await users.FindByEmailAsync(context.Identity.TenantId, email, cancellationToken);
        // Foundation-managed addresses are assigned by Elsa's administrator. An unverified profile imported
        // by an earlier JIT sign-in is not sufficient evidence for automatically attaching another identity.
        if (user is null || user.Status != UserStatus.Active || user.Ownership != ResourceOwnership.Foundation)
            return new() { Failure = ExternalAdmissionFailure.NoEligibleMatch };
        return new() { Kind = ExternalAdmissionKind.ExistingUser, ExistingUserId = user.Id, Failure = ExternalAdmissionFailure.None };
    }
}

internal static class ClaimValue
{
    public static string? Single(IEnumerable<Claim> claims, string type)
    {
        var values = claims.Where(claim => claim.Type == type).Take(2).ToArray();
        return values.Length == 1 ? values[0].Value : null;
    }
}
