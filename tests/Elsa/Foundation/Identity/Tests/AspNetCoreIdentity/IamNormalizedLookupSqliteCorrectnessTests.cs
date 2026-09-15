using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>
/// The timing-free IAM normalized-lookup scenario over the Groundwork SQLite-backed ASP.NET Core Identity stores. It
/// used to run the retired store-performance workload and compare a ratified result digest; performance measurement
/// was retired by owner decision (#1668, ADR 0073), so the scenario's observable results are asserted directly and the
/// native-plan acceptance test that shared this file is retired with it.
/// </summary>
[Trait("Category", "Sqlite")]
public sealed class IamNormalizedLookupSqliteCorrectnessTests : IDisposable
{
    private const string TenantId = "tenant-alpha";
    private const string UserId = "user-ada";
    private const string NormalizedUserName = "ADA";
    private const string NormalizedEmail = "ADA@EXAMPLE.TEST";
    private const string RoleId = "role-admin";
    private const string RoleName = "Administrators";
    private const string NormalizedRoleName = "ADMINISTRATORS";

    private readonly IdentityV2TestPersistence _persistence = new();
    private readonly GroundworkIdentityUserStore _users;
    private readonly GroundworkIdentityRoleStore _roles;

    public IamNormalizedLookupSqliteCorrectnessTests()
    {
        var access = new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(TenantId)));
        _users = new GroundworkIdentityUserStore(_persistence.Rows(access), access);
        _roles = new GroundworkIdentityRoleStore(_persistence.Rows(access), access);
    }

    public void Dispose() => _persistence.Dispose();

    [Fact]
    public async Task Normalized_name_email_and_role_lookups_return_the_canonical_rows_among_noise()
    {
        var user = await SeedAsync();

        Assert.Equal(UserId, (await _users.FindByNameAsync(NormalizedUserName, CancellationToken.None))?.Id);
        Assert.Equal(UserId, (await _users.FindByEmailAsync(NormalizedEmail, CancellationToken.None))?.Id);
        Assert.Equal(RoleId, (await _roles.FindByNameAsync(NormalizedRoleName, CancellationToken.None))?.Id);
        Assert.Equal([RoleName], await _users.GetRolesAsync(user, CancellationToken.None));
        Assert.Equal([UserId], (await _users.GetUsersInRoleAsync(NormalizedRoleName, CancellationToken.None)).Select(member => member.Id));
    }

    [Fact]
    public async Task Current_revision_update_is_accepted_and_stale_revision_update_is_rejected()
    {
        await SeedAsync();
        var stale = await _users.FindByNameAsync(NormalizedUserName, CancellationToken.None);
        var current = await _users.FindByIdAsync(UserId, CancellationToken.None);

        current!.DisplayName = "Ada Updated";
        AssertSucceeded(await _users.UpdateAsync(current, CancellationToken.None));
        stale!.DisplayName = "Ada Stale";
        Assert.False((await _users.UpdateAsync(stale, CancellationToken.None)).Succeeded);

        Assert.Equal("Ada Updated", (await _users.FindByIdAsync(UserId, CancellationToken.None))!.DisplayName);
    }

    private async Task<AspNetCoreIdentityUser> SeedAsync()
    {
        var user = User(UserId, "ada", NormalizedUserName, "ada@example.test", NormalizedEmail);
        AssertSucceeded(await _users.CreateAsync(user, CancellationToken.None));
        for (var index = 0; index < 16; index++)
        {
            var suffix = index.ToString("D4");
            AssertSucceeded(await _users.CreateAsync(
                User($"user-noise-{suffix}", $"noise-{suffix}", $"NOISE-{suffix}", $"noise-{suffix}@example.test", $"NOISE-{suffix}@EXAMPLE.TEST"),
                CancellationToken.None));
        }

        AssertSucceeded(await _roles.CreateAsync(
            new IdentityRole { Id = RoleId, Name = RoleName, NormalizedName = NormalizedRoleName, ConcurrencyStamp = "revision-role-admin-v1" },
            CancellationToken.None));
        await _users.AddToRoleAsync(user, NormalizedRoleName, CancellationToken.None);
        return user;
    }

    private static AspNetCoreIdentityUser User(string id, string userName, string normalizedUserName, string email, string normalizedEmail) => new()
    {
        Id = id,
        TenantId = TenantId,
        UserName = userName,
        NormalizedUserName = normalizedUserName,
        Email = email,
        NormalizedEmail = normalizedEmail,
        DisplayName = "Ada Lovelace",
        SecurityStamp = $"security-{id}-v1",
        ConcurrencyStamp = $"revision-{id}-v1"
    };

    private static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
