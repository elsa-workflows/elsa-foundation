using System.Security.Claims;
using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityRelationshipContractTests
{
    [Fact]
    public async Task User_claims_logins_roles_and_tokens_round_trip_through_deterministic_relationships()
    {
        var fixture = CreateFixture();
        var userStore = fixture.UserStore();
        var roleStore = fixture.RoleStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        var userClaim = AspNetCoreIdentityScenarioData.Claims.First(value => value.OwnerKind == AspNetCoreIdentityScenarioData.ClaimOwnerKind.User);
        var replacementClaim = new Claim(userClaim.Type, "identity.users.export");
        var login = AspNetCoreIdentityScenarioData.Logins.First(value => value.UserId == user.Id);
        var token = AspNetCoreIdentityScenarioData.Tokens.First(value => value.Id == AspNetCoreIdentityScenarioData.Ids.PrimaryToken);

        Assert.True((await userStore.CreateAsync(user, CancellationToken.None)).Succeeded);
        Assert.True((await roleStore.CreateAsync(role, CancellationToken.None)).Succeeded);

        await userStore.AddClaimsAsync(user, [AspNetCoreIdentityScenarioData.CreateClaim(userClaim)], CancellationToken.None);
        await userStore.AddLoginAsync(user, AspNetCoreIdentityScenarioData.CreateLoginInfo(login), CancellationToken.None);
        await userStore.AddToRoleAsync(user, role.NormalizedName!, CancellationToken.None);
        await userStore.SetTokenAsync(user, token.LoginProvider, token.Name, token.Value.Reveal(), CancellationToken.None);

        Assert.Equal(userClaim.Value, Assert.Single(await userStore.GetClaimsAsync(user, CancellationToken.None)).Value);
        Assert.Equal(user.Id, Assert.Single(await userStore.GetUsersForClaimAsync(AspNetCoreIdentityScenarioData.CreateClaim(userClaim), CancellationToken.None)).Id);
        Assert.Equal(login.ProviderKey, Assert.Single(await userStore.GetLoginsAsync(user, CancellationToken.None)).ProviderKey);
        Assert.Equal(user.Id, (await userStore.FindByLoginAsync(login.LoginProvider, login.ProviderKey, CancellationToken.None))?.Id);
        Assert.Equal(role.Name, Assert.Single(await userStore.GetRolesAsync(user, CancellationToken.None)));
        Assert.True(await userStore.IsInRoleAsync(user, role.NormalizedName!, CancellationToken.None));
        Assert.Equal(user.Id, Assert.Single(await userStore.GetUsersInRoleAsync(role.NormalizedName!, CancellationToken.None)).Id);
        Assert.Equal(token.Value.Reveal(), await userStore.GetTokenAsync(user, token.LoginProvider, token.Name, CancellationToken.None));

        await userStore.ReplaceClaimAsync(user, AspNetCoreIdentityScenarioData.CreateClaim(userClaim), replacementClaim, CancellationToken.None);
        Assert.Equal("identity.users.export", Assert.Single(await userStore.GetClaimsAsync(user, CancellationToken.None)).Value);

        await userStore.RemoveClaimsAsync(user, [replacementClaim], CancellationToken.None);
        await userStore.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey, CancellationToken.None);
        await userStore.RemoveFromRoleAsync(user, role.NormalizedName!, CancellationToken.None);
        await userStore.RemoveTokenAsync(user, token.LoginProvider, token.Name, CancellationToken.None);

        Assert.Empty(await userStore.GetClaimsAsync(user, CancellationToken.None));
        Assert.Empty(await userStore.GetLoginsAsync(user, CancellationToken.None));
        Assert.Empty(await userStore.GetRolesAsync(user, CancellationToken.None));
        Assert.Null(await userStore.GetTokenAsync(user, token.LoginProvider, token.Name, CancellationToken.None));
    }

    [Fact]
    public async Task Authenticator_key_and_recovery_codes_follow_framework_token_conventions()
    {
        var userStore = CreateFixture().UserStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);

        Assert.True((await userStore.CreateAsync(user, CancellationToken.None)).Succeeded);

        await userStore.SetAuthenticatorKeyAsync(user, AspNetCoreIdentityScenarioData.AuthenticatorKeyValue.Reveal(), CancellationToken.None);
        await userStore.ReplaceCodesAsync(user, ["code-one", "code-two"], CancellationToken.None);

        Assert.Equal(AspNetCoreIdentityScenarioData.AuthenticatorKeyValue.Reveal(), await userStore.GetAuthenticatorKeyAsync(user, CancellationToken.None));
        Assert.Equal(2, await userStore.CountCodesAsync(user, CancellationToken.None));
        Assert.True(await userStore.RedeemCodeAsync(user, "code-one", CancellationToken.None));
        Assert.False(await userStore.RedeemCodeAsync(user, "code-one", CancellationToken.None));
        Assert.Equal(1, await userStore.CountCodesAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task Public_framework_relationship_readers_return_every_page_plus_one_record_exactly_once()
    {
        const int pagePlusOne = 513;
        const string sharedClaimType = "cursor-shared-claim";
        const string sharedClaimValue = "member";
        var tenantId = AspNetCoreIdentityScenarioData.Ids.PrimaryTenant;
        var fixture = CreateFixture();
        var users = fixture.UserStore();
        var roles = fixture.RoleStore();
        var user = new AspNetCoreIdentityUser
        {
            Id = "cursor-root-user",
            TenantId = tenantId,
            UserName = "cursor-root-user",
            NormalizedUserName = "CURSOR-ROOT-USER"
        };
        var role = new IdentityRole("cursor-root-role")
        {
            Id = "cursor-root-role",
            NormalizedName = "CURSOR-ROOT-ROLE"
        };

        await SaveAsync(
            fixture.Documents,
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, user.Id),
            UserDocument(tenantId, user.Id));
        await SaveAsync(
            fixture.Documents,
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, role.Id),
            RoleDocument(tenantId, role.Id, role.Name!));

        var expectedUserClaims = new HashSet<string>(StringComparer.Ordinal);
        var expectedUserIds = new HashSet<string>(StringComparer.Ordinal);
        var expectedRoleClaims = new HashSet<string>(StringComparer.Ordinal);
        var expectedRoleNames = new HashSet<string>(StringComparer.Ordinal);
        var expectedLoginSubjects = new HashSet<string>(StringComparer.Ordinal);

        // Seed more than the 512-record route page, then invoke only public framework readers below.
        // Writer/aggregate behavior is covered by the existing relationship contract tests.
        for (var index = 0; index < pagePlusOne; index++)
        {
            var suffix = index.ToString("D4");
            var userClaimValue = $"user-claim-{suffix}";
            var userId = $"cursor-user-{suffix}";
            var roleClaimValue = $"role-claim-{suffix}";
            var roleId = $"cursor-role-{suffix}";
            var roleName = $"Cursor Role {suffix}";
            var subject = $"cursor-subject-{suffix}";

            expectedUserClaims.Add(userClaimValue);
            expectedUserIds.Add(userId);
            expectedRoleClaims.Add(roleClaimValue);
            expectedRoleNames.Add(roleName);
            expectedLoginSubjects.Add(subject);

            await SaveAsync(
                fixture.Documents,
                IdentityStorageManifest.UserClaimDocumentKind,
                IdentityDocumentId.From(tenantId, user.Id, "cursor-user-claim", userClaimValue),
                new IdentityUserClaimDocument(
                    tenantId,
                    user.Id,
                    "cursor-user-claim",
                    userClaimValue,
                    IdentityDocumentId.From(tenantId, "cursor-user-claim", userClaimValue),
                    IdentityDocumentId.From(tenantId, user.Id)));
            await SaveAsync(
                fixture.Documents,
                IdentityStorageManifest.IdentityUserDocumentKind,
                IdentityCompositeDocumentId.From(tenantId, userId),
                UserDocument(tenantId, userId));
            await SaveAsync(
                fixture.Documents,
                IdentityStorageManifest.UserClaimDocumentKind,
                IdentityDocumentId.From(tenantId, userId, sharedClaimType, sharedClaimValue),
                new IdentityUserClaimDocument(
                    tenantId,
                    userId,
                    sharedClaimType,
                    sharedClaimValue,
                    IdentityDocumentId.From(tenantId, sharedClaimType, sharedClaimValue),
                    IdentityDocumentId.From(tenantId, userId)));
            await SaveAsync(
                fixture.Documents,
                IdentityStorageManifest.RoleClaimDocumentKind,
                IdentityDocumentId.From(tenantId, role.Id, "cursor-role-claim", roleClaimValue),
                new IdentityRoleClaimDocument(
                    tenantId,
                    role.Id,
                    "cursor-role-claim",
                    roleClaimValue,
                    IdentityDocumentId.From(tenantId, role.Id)));
            await SaveAsync(
                fixture.Documents,
                IdentityStorageManifest.IdentityRoleDocumentKind,
                IdentityCompositeDocumentId.From(tenantId, roleId),
                RoleDocument(tenantId, roleId, roleName));
            await SaveAsync(
                fixture.Documents,
                IdentityStorageManifest.UserRoleDocumentKind,
                IdentityDocumentId.From(tenantId, user.Id, roleId),
                new IdentityUserRoleDocument(
                    tenantId,
                    user.Id,
                    roleId,
                    IdentityDocumentId.From(tenantId, user.Id),
                    IdentityDocumentId.From(tenantId, roleId)));
            await SaveAsync(
                fixture.Documents,
                IdentityStorageManifest.UserRoleDocumentKind,
                IdentityDocumentId.From(tenantId, userId, role.Id),
                new IdentityUserRoleDocument(
                    tenantId,
                    userId,
                    role.Id,
                    IdentityDocumentId.From(tenantId, userId),
                    IdentityDocumentId.From(tenantId, role.Id)));
            await SaveAsync(
                fixture.Documents,
                IdentityStorageManifest.ExternalLoginDocumentKind,
                IdentityCompositeDocumentId.From(tenantId, "cursor-provider", subject),
                new IdentityExternalLoginDocument(
                    tenantId,
                    user.Id,
                    "cursor-provider",
                    subject,
                    IdentityDocumentId.From(tenantId, "cursor-provider", subject),
                    null,
                    new ExternalIdentityRecord(
                        tenantId,
                        "cursor-provider",
                        subject,
                        user.Id,
                        DateTimeOffset.UnixEpoch,
                        null,
                        ExternalIdentityLinkPolicy.Admin),
                    IdentityDocumentId.From(tenantId, user.Id)));
        }

        AssertExactSet(expectedUserClaims, (await users.GetClaimsAsync(user, CancellationToken.None)).Select(claim => claim.Value));
        AssertExactSet(expectedUserIds, (await users.GetUsersForClaimAsync(new Claim(sharedClaimType, sharedClaimValue), CancellationToken.None)).Select(candidate => candidate.Id));
        AssertExactSet(expectedLoginSubjects, (await users.GetLoginsAsync(user, CancellationToken.None)).Select(login => login.ProviderKey));
        AssertExactSet(expectedRoleNames, await users.GetRolesAsync(user, CancellationToken.None));
        AssertExactSet(expectedUserIds, (await users.GetUsersInRoleAsync(role.NormalizedName!, CancellationToken.None)).Select(candidate => candidate.Id));
        AssertExactSet(expectedRoleClaims, (await roles.GetClaimsAsync(role, CancellationToken.None)).Select(claim => claim.Value));
    }

    private static IdentityUserDocument UserDocument(string tenantId, string userId) => new(
        tenantId,
        userId,
        $"{userId.ToUpperInvariant()}",
        null,
        IdentityDocumentId.From(tenantId, userId.ToUpperInvariant()),
        null,
        new UserRecord(
            userId,
            tenantId,
            userId,
            null,
            userId,
            UserStatus.Active,
            ResourceOwnership.Foundation,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

    private static IdentityRoleDocument RoleDocument(string tenantId, string roleId, string roleName) => new(
        tenantId,
        roleId,
        roleName.ToUpperInvariant(),
        IdentityDocumentId.From(tenantId, roleName.ToUpperInvariant()),
        new RoleRecord(
            roleId,
            tenantId,
            roleName,
            null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            false));

    private static async Task SaveAsync<TDocument>(
        InMemoryDocumentStore documents,
        string documentKind,
        string id,
        TDocument document)
    {
        var result = await documents.SaveAsync(
            new SaveDocumentRequest(
                documentKind,
                id,
                IdentityStorageManifest.SchemaVersion,
                JsonSerializer.Serialize(document, IdentityGroundworkJson.Options),
                0),
            CancellationToken.None);
        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
    }

    private static void AssertExactSet(IEnumerable<string> expected, IEnumerable<string?> actual)
    {
        var expectedValues = expected.ToArray();
        var actualValues = actual.Select(value => Assert.IsType<string>(value)).ToArray();
        Assert.Equal(expectedValues.Length, actualValues.Length);
        Assert.Equal(actualValues.Length, actualValues.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            expectedValues.OrderBy(value => value, StringComparer.Ordinal),
            actualValues.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static AspNetCoreIdentityGroundworkStoreFixture CreateFixture() =>
        new(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
}
