using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentitySqliteNullableProjectionTests
{
    private const string TenantId = "tenant-nullable-projections";

    [Fact]
    [Trait("Category", "Sqlite")]
    public async Task Physical_schema_admits_and_round_trips_authority_rows_with_optional_lookup_projections()
    {
        await using var driver = new SqliteGroundworkProviderDriver();
        await driver.InitializeAsync(CancellationToken.None);
        await driver.ResetPhysicalAsync([new IdentityGroundworkStorageManifestSource()], CancellationToken.None);
        await using var client = await driver.OpenPhysicalClientAsync(
            DocumentStoreAccess.Scoped(new StorageScope(TenantId)),
            CancellationToken.None);
        var bounded = client.BoundedDocumentStore
                      ?? throw new InvalidOperationException("Physical Identity tests require a bounded query runtime.");
        var accessor = new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(TenantId)));
        var users = new GroundworkIdentityUserStore(client.DocumentStore, accessor, bounded);
        var roles = new GroundworkIdentityRoleStore(client.DocumentStore, accessor, bounded);
        var userWithoutEmail = User("user-without-email", "no-email", "NO-EMAIL", null, null);
        var userWithoutName = User("user-without-name", null, null, "nameless@example.test", "NAMELESS@EXAMPLE.TEST");
        var roleWithoutName = new IdentityRole
        {
            Id = "role-without-name",
            Name = null,
            NormalizedName = null,
            ConcurrencyStamp = "role-without-name-v1"
        };

        Assert.True((await users.CreateAsync(userWithoutEmail, CancellationToken.None)).Succeeded);
        Assert.True((await users.CreateAsync(userWithoutName, CancellationToken.None)).Succeeded);
        Assert.True((await roles.CreateAsync(roleWithoutName, CancellationToken.None)).Succeeded);

        var loadedWithoutEmail = await users.FindByIdAsync(userWithoutEmail.Id, CancellationToken.None);
        var loadedWithoutName = await users.FindByIdAsync(userWithoutName.Id, CancellationToken.None);
        var loadedRole = await roles.FindByIdAsync(roleWithoutName.Id, CancellationToken.None);
        Assert.NotNull(loadedWithoutEmail);
        Assert.Null(loadedWithoutEmail.Email);
        Assert.Null(loadedWithoutEmail.NormalizedEmail);
        Assert.NotNull(loadedWithoutName);
        Assert.Null(loadedWithoutName.UserName);
        Assert.Null(loadedWithoutName.NormalizedUserName);
        Assert.NotNull(loadedRole);
        Assert.Null(loadedRole.Name);
        Assert.Null(loadedRole.NormalizedName);
    }

    private static AspNetCoreIdentityUser User(
        string id,
        string? userName,
        string? normalizedUserName,
        string? email,
        string? normalizedEmail) => new()
        {
            Id = id,
            TenantId = TenantId,
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            NormalizedEmail = normalizedEmail,
            DisplayName = id,
            SecurityStamp = $"security-{id}",
            ConcurrencyStamp = $"revision-{id}"
        };

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
