using System.Text.Json;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Tests;

public sealed class IdentityIamUpcasterTests() : EfSchemaUpcasterProof<IdentityIamV1Upcaster, string>(IdentityIamEfModule.SchemaFamily, new IdentityIamFixtureStore());

internal sealed class IdentityIamFixtureStore : IEfSchemaUpcasterProofStore<string>
{
    public EfSchemaChain Chain => IdentityIamEfModule.Chain;

    public async Task<string> ReadAsync(EfSchemaUpcasterFixture fixture, string stamp, EfSchemaRowContent row)
    {
        Assert.Equal(typeof(UserEntity), fixture.Entity);
        await using var database = new ExternalIdentityLinkTests.LinkDatabase();
        await using var db = await database.Open();
        await database.Seed(db, "fixture");
        var entity = await db.Users.SingleAsync();
        foreach (var (column, value) in row.Columns) db.Entry(entity).Property(column).CurrentValue = value;
        entity.SchemaVersion = stamp;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var user = await new EfUserStore(db, database.Access).FindAsync("tenant", "fixture");
        return JsonSerializer.Serialize(user);
    }

    public EfSchemaRowContent WriteAt(EfSchemaUpcasterFixture fixture, string value, string version)
    {
        Assert.Contains(version, Chain.ReadableVersions);
        using var document = JsonDocument.Parse(value);
        var roles = document.RootElement.GetProperty("RoleIds").EnumerateArray().Select(item => item.GetString()!).ToHashSet();
        var permissions = document.RootElement.GetProperty("DirectPermissions").EnumerateArray().Select(item => item.GetString()!).ToHashSet();
        return new(typeof(UserEntity),
            (nameof(UserEntity.RoleIdsJson), EfIdentityStoreSupport.SerializeSet(roles)),
            (nameof(UserEntity.DirectPermissionsJson), EfIdentityStoreSupport.SerializeSet(permissions)),
            (nameof(UserEntity.ClaimIdsJson), "[]"), (nameof(UserEntity.LoginIdsJson), "[]"),
            (nameof(UserEntity.RoleLinkIdsJson), "[]"), (nameof(UserEntity.TokenIdsJson), "[]"),
            (nameof(UserEntity.TenantMembershipIdsJson), "[]"));
    }
}
