using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Security.Claims;
using System.Security.Cryptography;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.ExternalAuthentication.Connections;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Admission;

internal sealed class ExternalAdmissionService(IServiceScopeFactory scopes, IConnectionRegistry registry, IOptions<ExternalAuthenticationOptions> options) : IExternalAdmissionService
{
    public async ValueTask<ExternalAdmissionAssessment> AssessAsync(ExternalAdmissionContext context, CancellationToken cancellationToken = default)
    {
        var safe = Snapshot(context);
        if (safe is null) return Reject(ExternalAdmissionFailure.IdentityMismatch);
        await using var scope = CreateScope();
        return await AssessCore(safe, scope.ServiceProvider, cancellationToken);
    }

    public async ValueTask<ExternalAdmissionResult> AdmitAsync(ExternalAdmissionContext context, CancellationToken cancellationToken = default)
    {
        var safe = Snapshot(context);
        if (safe is null) return new(null, ExternalAdmissionFailure.IdentityMismatch);
        var current = (await registry.ReadAsync(cancellationToken)).Connections.SingleOrDefault(view => view.Effective && view.Record.Key == safe.Identity.Provider);
        if (current is null || !current.Record.Enabled || current.Record.ArchivedAt is not null) return new(null, ExternalAdmissionFailure.ConnectionUnavailable);
        if (current.Record.Id != safe.Connection.Record.Id || current.MaterialRevision != safe.Connection.MaterialRevision) return new(null, ExternalAdmissionFailure.StaleConnection);
        safe = Snapshot(safe with { Connection = current })!;
        await using var scope = CreateScope();
        var assessment = await AssessCore(safe, scope.ServiceProvider, cancellationToken);
        if (assessment.Kind == ExternalAdmissionKind.Rejected) return new(null, assessment.Failure);
        if (assessment.Kind == ExternalAdmissionKind.ExistingUser)
        {
            var linked = await scope.ServiceProvider.GetRequiredService<IExternalIdentityStore>().FindByKeyAsync(safe.Identity, cancellationToken);
            if (linked is not null && linked.UserId == assessment.User!.Id) return new(linked.UserId);
        }
        var user = assessment.User!;
        if (assessment.Kind == ExternalAdmissionKind.NewUser) user = user with { Id = Guid.NewGuid().ToString("N") };
        var saved = await scope.ServiceProvider.GetRequiredService<IExternalIdentityStore>().AdmitAsync(
            new(safe.Identity, user, assessment.Kind == ExternalAdmissionKind.NewUser, ExternalIdentityLinkPolicy.Auto)
                { ExpectedMembership = assessment.Membership }, cancellationToken);
        if (!saved.Admitted) return new(null, saved.Failure switch
        {
            "user-unavailable" => ExternalAdmissionFailure.UserUnavailable,
            "role-unavailable" => ExternalAdmissionFailure.RoleUnavailable,
            _ => ExternalAdmissionFailure.Conflict
        });
        return new(saved.UserId);
    }

    private async ValueTask<ExternalAdmissionAssessment> AssessCore(ExternalAdmissionContext context, IServiceProvider services, CancellationToken token)
    {
        var users = services.GetService<IUserStore>();
        var links = services.GetService<IExternalIdentityStore>();
        var roles = services.GetService<IRoleStore>();
        var memberships = services.GetService<ITenantMembershipStore>();
        if (users is null || links is null || !links.SupportsQualifiedKeys || roles is null || memberships is null) return Reject(ExternalAdmissionFailure.IdentityUnavailable);
        var linked = await links.FindByKeyAsync(context.Identity, token);
        if (linked is not null)
        {
            var existing = await users.FindAsync(context.Identity.TenantId, linked.UserId, token);
            return await Existing(existing, roles, memberships, context, false, token);
        }
        var policy = context.Connection.Record.Admission;
        if (policy.Type == "reject") return Reject(ExternalAdmissionFailure.UnknownIdentity);
        if (!links.SupportsAtomicAdmission) return Reject(ExternalAdmissionFailure.IdentityUnavailable);
        var ownership = await services.GetRequiredService<IOwnershipModeProvider>().GetAsync(context.Identity.TenantId, token);
        var capabilities = services.GetRequiredService<IEffectiveCapabilitiesResolver>();
        if (!capabilities.Resolve(ownership, services.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value.ProviderCapabilities).CanManageUsers)
            return Reject(ExternalAdmissionFailure.OwnershipDenied);
        var policies = services.GetServices<IExternalProvisioningPolicy>().Where(candidate => candidate.Name == policy.Type).Take(2).ToArray();
        if (policies.Length != 1) return Reject(ExternalAdmissionFailure.PolicyUnavailable);
        var decision = await policies[0].AssessAsync(context, token);
        if (decision.Kind == ExternalAdmissionKind.Rejected)
            return Reject(decision.Failure is ExternalAdmissionFailure.VerifiedEmailRequired or ExternalAdmissionFailure.NoEligibleMatch
                ? decision.Failure : ExternalAdmissionFailure.PolicyRejected);
        if (decision.Kind == ExternalAdmissionKind.ExistingUser)
        {
            if (string.IsNullOrWhiteSpace(decision.ExistingUserId)) return Reject(ExternalAdmissionFailure.PolicyRejected);
            return await Existing(await users.FindAsync(context.Identity.TenantId, decision.ExistingUserId, token), roles, memberships, context, true, token);
        }
        if (decision.Kind != ExternalAdmissionKind.NewUser || decision.RoleIds is null || decision.RoleIds.Count > 128) return Reject(ExternalAdmissionFailure.PolicyRejected);
        var selected = decision.RoleIds.ToHashSet(StringComparer.Ordinal);
        var permitted = policy.DefaultRoleIds.ToHashSet(StringComparer.Ordinal);
        if (!selected.IsSubsetOf(permitted) || selected.Any(role => options.Value.AllowedRoleIds.Count > 0 && !options.Value.AllowedRoleIds.Contains(role, StringComparer.Ordinal)))
            return Reject(ExternalAdmissionFailure.RoleBoundary);
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in selected)
        {
            var role = await roles.FindAsync(context.Identity.TenantId, id, token);
            if (role is null) return Reject(ExternalAdmissionFailure.RoleUnavailable);
            permissions.UnionWith(role.Permissions);
        }
        var name = decision.UserName ?? UserName(context.Identity);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 400 || decision.Email?.Length > 400 || decision.DisplayName?.Length > 400)
            return Reject(ExternalAdmissionFailure.InvalidProfile);
        var user = new UserRecord("", context.Identity.TenantId, name, decision.Email, decision.DisplayName, UserStatus.Active, ResourceOwnership.External,
            selected.ToFrozenSet(StringComparer.Ordinal), FrozenSet<string>.Empty);
        return new(ExternalAdmissionKind.NewUser, user, selected.ToFrozenSet(StringComparer.Ordinal), permissions.ToFrozenSet(StringComparer.Ordinal));
    }

    private async ValueTask<ExternalAdmissionAssessment> Existing(UserRecord? user, IRoleStore roles, ITenantMembershipStore memberships,
        ExternalAdmissionContext context, bool matching, CancellationToken token)
    {
        if (user is null || user.Status != UserStatus.Active) return Reject(ExternalAdmissionFailure.UserUnavailable);
        var membership = await memberships.FindAsync(user.TenantId, user.Id, token);
        if (membership is not null && membership.Status != TenantMembershipStatus.Active) return Reject(ExternalAdmissionFailure.MembershipUnavailable);
        var ids = user.RoleIds.Concat(membership?.RoleIds ?? FrozenSet<string>.Empty).ToHashSet(StringComparer.Ordinal);
        var permissions = user.DirectPermissions.Concat(membership?.DirectPermissions ?? FrozenSet<string>.Empty).ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var role = await roles.FindAsync(user.TenantId, id, token);
            if (role is null) return Reject(ExternalAdmissionFailure.RoleUnavailable);
            permissions.UnionWith(role.Permissions);
        }
        if (matching)
        {
            // A selectable matcher cannot attach an external identity to a more privileged user than the
            // editor was allowed to delegate through the connection's approved role boundary.
            var permittedIds = context.Connection.Record.Admission.DefaultRoleIds.ToHashSet(StringComparer.Ordinal);
            if (!ids.IsSubsetOf(permittedIds) || ids.Any(id => options.Value.AllowedRoleIds.Count > 0 && !options.Value.AllowedRoleIds.Contains(id, StringComparer.Ordinal)))
                return Reject(ExternalAdmissionFailure.RoleBoundary);
            var permittedPermissions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in permittedIds)
            {
                var role = await roles.FindAsync(user.TenantId, id, token);
                if (role is null) return Reject(ExternalAdmissionFailure.RoleUnavailable);
                permittedPermissions.UnionWith(role.Permissions);
            }
            if (!permittedPermissions.Contains("*") && !permissions.IsSubsetOf(permittedPermissions)) return Reject(ExternalAdmissionFailure.RoleBoundary);
        }
        var snapshot = user with { RoleIds = user.RoleIds.ToFrozenSet(StringComparer.Ordinal), DirectPermissions = user.DirectPermissions.ToFrozenSet(StringComparer.Ordinal) };
        return new(ExternalAdmissionKind.ExistingUser, snapshot, ids.ToFrozenSet(StringComparer.Ordinal), permissions.ToFrozenSet(StringComparer.Ordinal))
        {
            Membership = membership is null ? null : membership with
                { RoleIds = membership.RoleIds.ToFrozenSet(StringComparer.Ordinal), DirectPermissions = membership.DirectPermissions.ToFrozenSet(StringComparer.Ordinal) }
        };
    }

    private AsyncServiceScope CreateScope()
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(options.Value.TenantId)));
        return scope;
    }

    private ExternalAdmissionContext? Snapshot(ExternalAdmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Identity.TenantId != options.Value.TenantId || context.Identity.Provider != context.Connection.Record.Key || context.Claims.Count > 1024 ||
            context.Claims.Sum(claim => (long)claim.Type.Length + claim.Value.Length) > 262144) return null;
        var claims = context.Claims.Select(claim => new Claim(claim.Type, claim.Value, claim.ValueType, claim.Issuer, claim.OriginalIssuer)).ToArray();
        if (ClaimValue.Single(claims, "iss") != context.Identity.Issuer || ClaimValue.Single(claims, "sub") != context.Identity.Subject) return null;
        var record = ConnectionSelection.Snapshot(context.Connection.Record);
        record = record with
        {
            Settings = record.Settings.ToFrozenDictionary(StringComparer.Ordinal), Secrets = record.Secrets.ToFrozenDictionary(StringComparer.Ordinal),
            Admission = record.Admission with { DefaultRoleIds = Array.AsReadOnly(record.Admission.DefaultRoleIds.ToArray()) }
        };
        return context with { Connection = context.Connection with { Record = record }, Claims = Array.AsReadOnly(claims) };
    }

    private static ExternalAdmissionAssessment Reject(ExternalAdmissionFailure failure) =>
        new(ExternalAdmissionKind.Rejected, null, FrozenSet<string>.Empty, FrozenSet<string>.Empty, failure);

    private static string UserName(ExternalIdentityKey key)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        Span<byte> unit = stackalloc byte[sizeof(char)];
        foreach (var part in new[] { "external-user-name/v1", key.TenantId, key.Provider, key.Issuer, key.Subject })
        {
            BinaryPrimitives.WriteInt32BigEndian(length, part.Length);
            hash.AppendData(length);
            foreach (var value in part) { BinaryPrimitives.WriteUInt16BigEndian(unit, value); hash.AppendData(unit); }
        }
        return "external-" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
