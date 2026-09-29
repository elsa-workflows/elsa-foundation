using System.Reflection;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="EfIdentityAuthorityRelationshipCoordinator"/>'s private <c>Prepare(TenantMembershipEntity, string, string)</c>
/// upcasts a row's registries from its own stamp before restamping it, so a row this ever receives with a prior
/// stamp - not a shape any production caller passes today, since <c>MutateMembershipAsync</c> always builds a fresh,
/// unstamped row - restamps completely on its own instead of trusting the caller's registries as already current
/// (spec 180, FR-014; #2144). No production caller exercises that branch, so it is proven directly here, through
/// reflection, to keep it from rotting unseen.
/// </summary>
public sealed class EfIdentityAuthorityRelationshipCoordinatorPrepareTests
{
    private static readonly MethodInfo PrepareMethod = typeof(EfIdentityAuthorityRelationshipCoordinator).GetMethod(
        "Prepare",
        BindingFlags.NonPublic | BindingFlags.Static,
        [typeof(TenantMembershipEntity), typeof(string), typeof(string)])
        ?? throw new MissingMethodException(nameof(EfIdentityAuthorityRelationshipCoordinator), "Prepare(TenantMembershipEntity, string, string)");

    [Fact]
    public void Prepare_carries_forward_the_registries_of_a_membership_row_already_stamped_at_the_current_version()
    {
        const string tenant = "tenant-a";
        const string user = "user-a";
        var row = new TenantMembershipEntity
        {
            SchemaVersion = IdentityIamEfModule.SchemaVersion,
            RoleIdsJson = """["role-a","role-b"]""",
            DirectPermissionsJson = """["identity.users.read"]""",
            Status = 1,
            Revision = 3
        };

        PrepareMethod.Invoke(null, [row, tenant, user]);

        Assert.Equal(IdentityIamEfModule.SchemaVersion, row.SchemaVersion);
        Assert.Equal("""["role-a","role-b"]""", row.RoleIdsJson);
        Assert.Equal("""["identity.users.read"]""", row.DirectPermissionsJson);
        Assert.Equal(tenant, row.TenantId);
        Assert.Equal(EfIdentityStoreSupport.TenantLookup(tenant), row.TenantLookupKey);
        Assert.Equal(user, row.UserId);
        Assert.Equal(EfIdentityStoreSupport.Lookup(tenant, user), row.UserLookupKey);
        Assert.Equal(3, row.Revision);
    }
}
