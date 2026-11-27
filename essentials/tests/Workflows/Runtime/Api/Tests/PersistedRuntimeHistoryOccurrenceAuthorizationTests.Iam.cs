using System.Security.Claims;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using UserStore = Elsa.Foundation.Identity.Core.Iam.IUserStore;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class PersistedRuntimeHistoryOccurrenceAuthorizationTests
{
    private sealed partial class Fixture
    {
        private const string Role = "history-viewers";
        private async Task CreateUser(IServiceProvider services, string tenant, string grantsFrom)
        {
            var user = new AspNetCoreIdentityUser { Id = Guid.NewGuid().ToString("N"), TenantId = tenant, UserName = "operator-" + tenant };
            var created = await services.GetRequiredService<UserManager<AspNetCoreIdentityUser>>().CreateAsync(user);
            Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(error => error.Code)));
            UserId = user.Id;
            var users = services.GetRequiredService<IRevisionAwareUserStore>();
            var stored = await users.FindWithRevisionAsync(tenant, user.Id); Assert.NotNull(stored);
            if (grantsFrom == "role")
            {
                var result = await services.GetRequiredService<IRevisionAwareRoleStore>().SaveWithRevisionAsync(
                    new(Role, tenant, "History viewers", null, Grants.ToHashSet(StringComparer.Ordinal), false), null);
                Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
            }
            if (grantsFrom == "membership")
            {
                var result = await services.GetRequiredService<IRevisionAwareTenantMembershipStore>().SaveWithRevisionAsync(
                    new(tenant, user.Id, TenantMembershipStatus.Active, new HashSet<string>(), Grants.ToHashSet(StringComparer.Ordinal)), null);
                Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
            }
            // Membership publication legitimately advances the owning user's revision. Observe
            // that acknowledgement before issuing the next independent user CAS operation.
            stored = await users.FindWithRevisionAsync(tenant, user.Id); Assert.NotNull(stored);
            var changed = stored.Record with
            {
                DirectPermissions = grantsFrom == "direct" ? Grants.ToHashSet(StringComparer.Ordinal) : new HashSet<string>(),
                RoleIds = grantsFrom == "role" ? new HashSet<string> { Role } : new HashSet<string>()
            };
            var saved = await users.SaveWithRevisionAsync(changed, stored.Revision);
            Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
            var observed = await users.FindWithRevisionAsync(tenant, user.Id); Assert.NotNull(observed);
            Assert.Equal(saved.Revision, observed.Revision); Assert.True(observed.Record.DirectPermissions.SetEquals(changed.DirectPermissions));
            SignedIn = await services.GetRequiredService<IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>>().CreateAsync(user);
            Assert.True(services.GetRequiredService<NormalizedPrincipalValidator>().TryGetNormalizedPrincipal(SignedIn, out _));
            Assert.Equal(user.Id, Assert.Single(SignedIn.FindAll(IdentityClaimTypes.UserId)).Value);
            Assert.Equal(tenant, Assert.Single(SignedIn.FindAll(IdentityClaimTypes.TenantId)).Value);
            Assert.Empty(SignedIn.FindAll(IdentityClaimTypes.SubjectProvider));
            foreach (var grant in Grants)
            {
                Assert.Contains(SignedIn.Claims, claim => claim.Type == IdentityClaimTypes.Permission && claim.Value == grant);
                Assert.Equal(CurrentSubjectPermissionStatus.Granted,
                    (await services.GetRequiredService<ICurrentSubjectPermissionAuthorizer>().AuthorizeAsync(new(tenant, user.Id, grant))).Status);
            }
        }

        internal async Task<ClaimsPrincipal> ForeignPrincipal()
        {
            var originalId = UserId; var original = SignedIn;
            await Scope(OtherTenant, services => CreateUser(services, OtherTenant, "direct"));
            var foreign = SignedIn; UserId = originalId; SignedIn = original; return foreign;
        }

        internal async Task Retire(string change)
        {
            await Scope(Tenant, async services =>
            {
                if (change is "disabled" or "deleted")
                {
                    var users = services.GetRequiredService<UserStore>();
                    Assert.True(change == "disabled" ? await users.SetStatusAsync(Tenant, UserId, UserStatus.Disabled) : await users.DeleteAsync(Tenant, UserId));
                    var observed = await users.FindAsync(Tenant, UserId);
                    if (change == "disabled") Assert.Equal(UserStatus.Disabled, observed!.Status); else Assert.Null(observed);
                }
                else if (change == "role")
                {
                    var roles = services.GetRequiredService<IRevisionAwareRoleStore>(); var stored = await roles.FindWithRevisionAsync(Tenant, Role); Assert.NotNull(stored);
                    var result = await roles.SaveWithRevisionAsync(stored.Record with { Permissions = new HashSet<string>() }, stored.Revision);
                    Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
                    var observed = await roles.FindWithRevisionAsync(Tenant, Role); Assert.Equal(result.Revision, observed!.Revision); Assert.Empty(observed.Record.Permissions);
                }
                else if (change is "membership" or "membership-grants")
                {
                    var members = services.GetRequiredService<IRevisionAwareTenantMembershipStore>(); var stored = await members.FindWithRevisionAsync(Tenant, UserId); Assert.NotNull(stored);
                    var desired = change == "membership" ? stored.Record with { Status = TenantMembershipStatus.Suspended }
                        : stored.Record with { DirectPermissions = new HashSet<string>() };
                    var result = await members.SaveWithRevisionAsync(desired, stored.Revision);
                    Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
                    var observed = await members.FindWithRevisionAsync(Tenant, UserId); Assert.Equal(result.Revision, observed!.Revision);
                    Assert.Equal(desired.Status, observed.Record.Status); Assert.True(observed.Record.DirectPermissions.SetEquals(desired.DirectPermissions));
                }
                else
                {
                    var users = services.GetRequiredService<IRevisionAwareUserStore>(); var stored = await users.FindWithRevisionAsync(Tenant, UserId); Assert.NotNull(stored);
                    var permissions = stored.Record.DirectPermissions.ToHashSet(StringComparer.Ordinal);
                    var desired = change == "role-membership" ? stored.Record with { RoleIds = new HashSet<string>() }
                        : stored.Record with { DirectPermissions = permissions.Where(permission => permission != Permission(change)).ToHashSet(StringComparer.Ordinal) };
                    var result = await users.SaveWithRevisionAsync(desired, stored.Revision);
                    Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
                    var observed = await users.FindWithRevisionAsync(Tenant, UserId); Assert.Equal(result.Revision, observed!.Revision);
                    Assert.True(observed.Record.DirectPermissions.SetEquals(desired.DirectPermissions)); Assert.True(observed.Record.RoleIds.SetEquals(desired.RoleIds));
                }
            });
        }
    }

    private static string Permission(string change) => change switch
    { "runtime" => Grants[0], "structure" => Grants[1], "values" => Grants[2], "payloads" => Grants[3], _ => throw new ArgumentOutOfRangeException(nameof(change)) };
    private static string Source(string change) => change is "role" or "role-membership" ? "role"
        : change is "membership" or "membership-grants" ? "membership" : "direct";
    private static ClaimsPrincipal Without(ClaimsPrincipal original, string type, string? value = null)
    {
        var copy = original.Clone();
        foreach (var identity in copy.Identities)
        foreach (var claim in identity.Claims.Where(claim => claim.Type == type && (value is null || claim.Value == value)).ToArray()) identity.RemoveClaim(claim);
        return copy;
    }
}
