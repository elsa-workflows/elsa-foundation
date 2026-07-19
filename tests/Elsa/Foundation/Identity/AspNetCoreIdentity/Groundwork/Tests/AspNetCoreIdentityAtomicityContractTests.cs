using System.Security.Claims;
using System.Text.Json;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityAtomicityContractTests
{
    [Fact]
    public async Task Deleting_user_removes_dependent_relationship_documents()
    {
        var fixture = CreateFixture();
        var userStore = fixture.UserStore();
        var roleStore = fixture.RoleStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        var claim = AspNetCoreIdentityScenarioData.Claims.First(value => value.OwnerKind == AspNetCoreIdentityScenarioData.ClaimOwnerKind.User);
        var login = AspNetCoreIdentityScenarioData.Logins.First(value => value.UserId == user.Id);
        var token = AspNetCoreIdentityScenarioData.Tokens.First(value => value.Id == AspNetCoreIdentityScenarioData.Ids.PrimaryToken);

        Assert.True((await userStore.CreateAsync(user, CancellationToken.None)).Succeeded);
        Assert.True((await roleStore.CreateAsync(role, CancellationToken.None)).Succeeded);
        await userStore.AddClaimsAsync(user, [AspNetCoreIdentityScenarioData.CreateClaim(claim)], CancellationToken.None);
        await userStore.AddLoginAsync(user, AspNetCoreIdentityScenarioData.CreateLoginInfo(login), CancellationToken.None);
        await userStore.AddToRoleAsync(user, role.NormalizedName!, CancellationToken.None);
        await userStore.SetTokenAsync(user, token.LoginProvider, token.Name, token.Value.Reveal(), CancellationToken.None);

        Assert.True((await userStore.DeleteAsync(user, CancellationToken.None)).Succeeded);

        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.UserClaimDocumentKind, IdentityDocumentId.From(user.TenantId, user.Id, claim.Type, claim.Value));
        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.ExternalLoginDocumentKind, IdentityCompositeDocumentId.From(user.TenantId, login.LoginProvider, login.ProviderKey));
        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.UserRoleDocumentKind, IdentityDocumentId.From(user.TenantId, user.Id, role.Id));
        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.UserTokenDocumentKind, IdentityDocumentId.From(user.TenantId, user.Id, token.LoginProvider, token.Name));
        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.UserNameReservationDocumentKind, IdentityDocumentId.From(user.TenantId, user.NormalizedUserName));
        Assert.DoesNotContain(
            IdentityDocumentId.From(user.TenantId, user.Id, role.Id),
            (await LoadRoleDocumentAsync(fixture.Documents, user.TenantId, role.Id)).UserLinkIds ?? []);
    }

    [Fact]
    public async Task Stale_user_cannot_create_role_link_after_user_delete()
    {
        var fixture = CreateFixture();
        var userStore = fixture.UserStore();
        var roleStore = fixture.RoleStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);

        Assert.True((await userStore.CreateAsync(user, CancellationToken.None)).Succeeded);
        Assert.True((await roleStore.CreateAsync(role, CancellationToken.None)).Succeeded);
        var staleUser = await userStore.FindByIdAsync(user.Id, CancellationToken.None);
        Assert.NotNull(staleUser);
        Assert.True((await userStore.DeleteAsync(user, CancellationToken.None)).Succeeded);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            userStore.AddToRoleAsync(staleUser!, role.NormalizedName!, CancellationToken.None));

        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.UserRoleDocumentKind, IdentityDocumentId.From(user.TenantId, user.Id, role.Id));
    }

    [Fact]
    public async Task Deleting_role_removes_links_claims_and_user_registry_entries()
    {
        var fixture = CreateFixture();
        var userStore = fixture.UserStore();
        var roleStore = fixture.RoleStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        var claim = AspNetCoreIdentityScenarioData.Claims.Single(value => value.OwnerKind == AspNetCoreIdentityScenarioData.ClaimOwnerKind.Role);

        Assert.True((await userStore.CreateAsync(user, CancellationToken.None)).Succeeded);
        Assert.True((await roleStore.CreateAsync(role, CancellationToken.None)).Succeeded);
        await userStore.AddToRoleAsync(user, role.NormalizedName!, CancellationToken.None);
        role = (await roleStore.FindByIdAsync(role.Id, CancellationToken.None))!;
        await roleStore.AddClaimAsync(role, AspNetCoreIdentityScenarioData.CreateClaim(claim), CancellationToken.None);

        role = (await roleStore.FindByIdAsync(role.Id, CancellationToken.None))!;
        Assert.True((await roleStore.DeleteAsync(role, CancellationToken.None)).Succeeded);

        var linkId = IdentityDocumentId.From(user.TenantId, user.Id, role.Id);
        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.IdentityRoleDocumentKind, IdentityCompositeDocumentId.From(user.TenantId, role.Id));
        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.UserRoleDocumentKind, linkId);
        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.RoleClaimDocumentKind, IdentityDocumentId.From(user.TenantId, role.Id, claim.Type, claim.Value));
        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.RoleNameReservationDocumentKind, IdentityDocumentId.From(user.TenantId, role.NormalizedName));
        Assert.DoesNotContain(linkId, (await LoadUserDocumentAsync(fixture.Documents, user)).RoleLinkIds ?? []);
    }

    [Fact]
    public async Task Adding_role_link_updates_both_owner_registries_and_rotates_user_stamp()
    {
        var fixture = CreateFixture();
        var userStore = fixture.UserStore();
        var roleStore = fixture.RoleStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);

        Assert.True((await userStore.CreateAsync(user, CancellationToken.None)).Succeeded);
        Assert.True((await roleStore.CreateAsync(role, CancellationToken.None)).Succeeded);
        var originalStamp = user.ConcurrencyStamp;

        await userStore.AddToRoleAsync(user, role.NormalizedName!, CancellationToken.None);

        var linkId = IdentityDocumentId.From(user.TenantId, user.Id, role.Id);
        Assert.Contains(linkId, (await LoadUserDocumentAsync(fixture.Documents, user)).RoleLinkIds ?? []);
        Assert.Contains(linkId, (await LoadRoleDocumentAsync(fixture.Documents, user.TenantId, role.Id)).UserLinkIds ?? []);
        Assert.NotEqual(originalStamp, user.ConcurrencyStamp);
    }

    [Fact]
    public async Task Removing_role_link_removes_both_owner_registry_entries()
    {
        var fixture = CreateFixture();
        var userStore = fixture.UserStore();
        var roleStore = fixture.RoleStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);

        Assert.True((await userStore.CreateAsync(user, CancellationToken.None)).Succeeded);
        Assert.True((await roleStore.CreateAsync(role, CancellationToken.None)).Succeeded);
        await userStore.AddToRoleAsync(user, role.NormalizedName!, CancellationToken.None);

        await userStore.RemoveFromRoleAsync(user, role.NormalizedName!, CancellationToken.None);

        var linkId = IdentityDocumentId.From(user.TenantId, user.Id, role.Id);
        Assert.DoesNotContain(linkId, (await LoadUserDocumentAsync(fixture.Documents, user)).RoleLinkIds ?? []);
        Assert.DoesNotContain(linkId, (await LoadRoleDocumentAsync(fixture.Documents, user.TenantId, role.Id)).UserLinkIds ?? []);
        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.UserRoleDocumentKind, linkId);
    }

    [Fact]
    public async Task Stale_owner_conflict_rolls_back_the_already_staged_claim_child()
    {
        var fixture = CreateFixture();
        var store = fixture.UserStore();
        var source = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var claim = AspNetCoreIdentityScenarioData.Claims.First(value =>
            value.OwnerKind == AspNetCoreIdentityScenarioData.ClaimOwnerKind.User);

        Assert.True((await store.CreateAsync(source, CancellationToken.None)).Succeeded);
        var stale = (await store.FindByIdAsync(source.Id, CancellationToken.None))!;
        await store.SetTokenAsync(source, "failure-window", "advance-owner", "v1", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AddClaimsAsync(stale, [AspNetCoreIdentityScenarioData.CreateClaim(claim)], CancellationToken.None));

        var claimId = IdentityDocumentId.From(source.TenantId, source.Id, claim.Type, claim.Value);
        await AssertMissingAsync(fixture.Documents, IdentityStorageManifest.UserClaimDocumentKind, claimId);
        Assert.DoesNotContain(claimId, (await LoadUserDocumentAsync(fixture.Documents, source)).ClaimIds ?? []);
    }

    [Fact]
    public async Task Single_owner_relationship_mutations_rotate_stamp_and_keep_sorted_authoritative_registries()
    {
        var fixture = CreateFixture();
        var store = fixture.UserStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var claims = AspNetCoreIdentityScenarioData.Claims
            .Where(value => value.OwnerKind == AspNetCoreIdentityScenarioData.ClaimOwnerKind.User)
            .Select(AspNetCoreIdentityScenarioData.CreateClaim)
            .Reverse()
            .ToArray();
        var login = AspNetCoreIdentityScenarioData.Logins.First(value => value.UserId == user.Id);

        Assert.True((await store.CreateAsync(user, CancellationToken.None)).Succeeded);
        var initialStamp = user.ConcurrencyStamp;
        await store.AddClaimsAsync(user, claims, CancellationToken.None);
        var afterClaims = user.ConcurrencyStamp;
        await store.AddLoginAsync(user, AspNetCoreIdentityScenarioData.CreateLoginInfo(login), CancellationToken.None);
        var afterLogin = user.ConcurrencyStamp;
        await store.SetTokenAsync(user, "provider-z", "token-z", "secret", CancellationToken.None);

        var document = await LoadUserDocumentAsync(fixture.Documents, user);
        Assert.NotEqual(initialStamp, afterClaims);
        Assert.NotEqual(afterClaims, afterLogin);
        Assert.NotEqual(afterLogin, user.ConcurrencyStamp);
        Assert.Equal(document.ClaimIds?.Order(StringComparer.Ordinal), document.ClaimIds);
        Assert.Equal(document.LoginIds?.Order(StringComparer.Ordinal), document.LoginIds);
        Assert.Equal(document.TokenIds?.Order(StringComparer.Ordinal), document.TokenIds);
    }

    [Fact]
    public async Task External_login_remove_rejects_a_different_owner_without_deleting_the_link()
    {
        var fixture = CreateFixture();
        var store = fixture.UserStore();
        var owner = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var other = AspNetCoreIdentityScenarioData.CreateIdentityUser(
            AspNetCoreIdentityScenarioData.Users.Single(user => user.Id == AspNetCoreIdentityScenarioData.Ids.UnicodeUser));
        var login = AspNetCoreIdentityScenarioData.Logins.First(value => value.UserId == owner.Id);
        Assert.True((await store.CreateAsync(owner, CancellationToken.None)).Succeeded);
        Assert.True((await store.CreateAsync(other, CancellationToken.None)).Succeeded);
        await store.AddLoginAsync(owner, AspNetCoreIdentityScenarioData.CreateLoginInfo(login), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RemoveLoginAsync(
            other, login.LoginProvider, login.ProviderKey, CancellationToken.None));

        var linkId = IdentityCompositeDocumentId.From(owner.TenantId, login.LoginProvider, login.ProviderKey);
        Assert.NotNull(await fixture.Documents.LoadAsync(
            IdentityStorageManifest.ExternalLoginDocumentKind, linkId, CancellationToken.None));
        Assert.Contains(linkId, (await LoadUserDocumentAsync(fixture.Documents, owner)).LoginIds ?? []);
        Assert.DoesNotContain(linkId, (await LoadUserDocumentAsync(fixture.Documents, other)).LoginIds ?? []);
    }

    [Fact]
    public async Task Concurrent_two_user_add_of_the_same_external_login_has_exactly_one_owner_without_transfer()
    {
        var fixture = CreateFixture();
        var firstStore = fixture.UserStore();
        var secondStore = fixture.UserStore();
        var first = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var second = AspNetCoreIdentityScenarioData.CreateIdentityUser(
            AspNetCoreIdentityScenarioData.Users.Single(user => user.Id == AspNetCoreIdentityScenarioData.Ids.UnicodeUser));
        var login = new Microsoft.AspNetCore.Identity.UserLoginInfo("shared-provider", "shared-subject", "Shared");
        Assert.True((await firstStore.CreateAsync(first, CancellationToken.None)).Succeeded);
        Assert.True((await secondStore.CreateAsync(second, CancellationToken.None)).Succeeded);

        var attempts = await Task.WhenAll(
            TryAddAsync(firstStore, first, login),
            TryAddAsync(secondStore, second, login));

        Assert.Single(attempts, exception => exception is null);
        Assert.Single(attempts, exception => exception is InvalidOperationException);
        var loginId = IdentityCompositeDocumentId.From(first.TenantId, login.LoginProvider, login.ProviderKey);
        var loginEnvelope = await fixture.Documents.LoadAsync(
            IdentityStorageManifest.ExternalLoginDocumentKind,
            loginId,
            CancellationToken.None);
        Assert.NotNull(loginEnvelope);
        var loginDocument = JsonSerializer.Deserialize<IdentityExternalLoginDocument>(
            loginEnvelope!.ContentJson,
            IdentityGroundworkJson.Options)!;
        var winner = string.Equals(loginDocument.UserId, first.Id, StringComparison.Ordinal) ? first : second;
        var loser = ReferenceEquals(winner, first) ? second : first;

        Assert.Contains(loginId, (await LoadUserDocumentAsync(fixture.Documents, winner)).LoginIds ?? []);
        Assert.DoesNotContain(loginId, (await LoadUserDocumentAsync(fixture.Documents, loser)).LoginIds ?? []);
        Assert.Equal(winner.Id, (await firstStore.FindByLoginAsync(
            login.LoginProvider,
            login.ProviderKey,
            CancellationToken.None))?.Id);
        Assert.Single(await firstStore.GetLoginsAsync(winner, CancellationToken.None));
        Assert.Empty(await firstStore.GetLoginsAsync(loser, CancellationToken.None));

        static async Task<Exception?> TryAddAsync(
            GroundworkIdentityUserStore store,
            AspNetCoreIdentityUser user,
            Microsoft.AspNetCore.Identity.UserLoginInfo loginInfo)
        {
            try
            {
                await store.AddLoginAsync(user, loginInfo, CancellationToken.None);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }

    [Fact]
    public async Task Relationship_admission_accepts_cardinality_512_and_rejects_513_without_staging_the_child()
    {
        var fixture = CreateFixture();
        var store = fixture.UserStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var acceptedClaim = new Claim("capacity", "accepted-at-512");
        var rejectedClaim = new Claim("capacity", "rejected-at-513");
        Assert.True((await store.CreateAsync(user, CancellationToken.None)).Succeeded);

        var envelope = await fixture.Documents.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(user.TenantId, user.Id),
            CancellationToken.None);
        Assert.NotNull(envelope);
        var document = JsonSerializer.Deserialize<IdentityUserDocument>(envelope!.ContentJson, IdentityGroundworkJson.Options)!;
        var atLimit = document with
        {
            ClaimIds = Enumerable.Range(0, IdentityStorageManifest.MaxAggregateRelationshipEntries - 1)
                .Select(index => $"existing-{index:D4}")
                .ToArray()
        };
        var saved = await fixture.Documents.SaveAsync(
            new SaveDocumentRequest(
                envelope.DocumentKind,
                envelope.Id,
                IdentityStorageManifest.SchemaVersion,
                JsonSerializer.Serialize(atLimit, IdentityGroundworkJson.Options),
                envelope.Version),
            CancellationToken.None);
        Assert.Equal(DocumentStoreWriteStatus.Saved, saved.Status);
        user = (await store.FindByIdAsync(user.Id, CancellationToken.None))!;

        await store.AddClaimsAsync(user, [acceptedClaim], CancellationToken.None);
        Assert.Equal(
            IdentityStorageManifest.MaxAggregateRelationshipEntries,
            (await LoadUserDocumentAsync(fixture.Documents, user)).ClaimIds?.Count);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AddClaimsAsync(user, [rejectedClaim], CancellationToken.None));

        await AssertMissingAsync(
            fixture.Documents,
            IdentityStorageManifest.UserClaimDocumentKind,
            IdentityDocumentId.From(user.TenantId, user.Id, rejectedClaim.Type, rejectedClaim.Value));
    }

    [Fact]
    public async Task Relationship_admission_counts_same_id_entries_in_different_user_registries_separately()
    {
        var fixture = CreateFixture();
        var store = fixture.UserStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        const string sharedTypeOrProvider = "shared-kind";
        const string sharedValueOrName = "shared-value";
        var sharedId = IdentityDocumentId.From(
            user.TenantId,
            user.Id,
            sharedTypeOrProvider,
            sharedValueOrName);
        Assert.True((await store.CreateAsync(user, CancellationToken.None)).Succeeded);

        var envelope = await fixture.Documents.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(user.TenantId, user.Id),
            CancellationToken.None);
        Assert.NotNull(envelope);
        var document = JsonSerializer.Deserialize<IdentityUserDocument>(envelope!.ContentJson, IdentityGroundworkJson.Options)!;
        var seeded = document with
        {
            ClaimIds =
            [
                sharedId,
                .. Enumerable.Range(0, IdentityStorageManifest.MaxAggregateRelationshipEntries - 2)
                    .Select(index => $"existing-{index:D4}")
            ]
        };
        Assert.Equal(
            DocumentStoreWriteStatus.Saved,
            (await fixture.Documents.SaveAsync(
                new SaveDocumentRequest(
                    envelope.DocumentKind,
                    envelope.Id,
                    IdentityStorageManifest.SchemaVersion,
                    JsonSerializer.Serialize(seeded, IdentityGroundworkJson.Options),
                    envelope.Version),
                CancellationToken.None)).Status);
        user = (await store.FindByIdAsync(user.Id, CancellationToken.None))!;

        await store.SetTokenAsync(
            user,
            sharedTypeOrProvider,
            sharedValueOrName,
            "accepted-at-512",
            CancellationToken.None);
        var rejected = new Claim("capacity", "rejected-mixed-kind-at-513");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AddClaimsAsync(user, [rejected], CancellationToken.None));
        await AssertMissingAsync(
            fixture.Documents,
            IdentityStorageManifest.UserClaimDocumentKind,
            IdentityDocumentId.From(user.TenantId, user.Id, rejected.Type, rejected.Value));
    }

    [Fact]
    public async Task Relationship_admission_counts_same_id_entries_in_different_role_registries_separately()
    {
        var fixture = CreateFixture();
        var users = fixture.UserStore();
        var roles = fixture.RoleStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        Assert.True((await users.CreateAsync(user, CancellationToken.None)).Succeeded);
        Assert.True((await roles.CreateAsync(role, CancellationToken.None)).Succeeded);
        var linkId = IdentityDocumentId.From(user.TenantId, user.Id, role.Id);

        var envelope = await fixture.Documents.LoadAsync(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From(user.TenantId, role.Id),
            CancellationToken.None);
        Assert.NotNull(envelope);
        var document = JsonSerializer.Deserialize<IdentityRoleDocument>(envelope!.ContentJson, IdentityGroundworkJson.Options)!;
        var seeded = document with
        {
            ClaimIds =
            [
                linkId,
                .. Enumerable.Range(0, IdentityStorageManifest.MaxAggregateRelationshipEntries - 2)
                    .Select(index => $"existing-role-{index:D4}")
            ]
        };
        Assert.Equal(
            DocumentStoreWriteStatus.Saved,
            (await fixture.Documents.SaveAsync(
                new SaveDocumentRequest(
                    envelope.DocumentKind,
                    envelope.Id,
                    IdentityStorageManifest.SchemaVersion,
                    JsonSerializer.Serialize(seeded, IdentityGroundworkJson.Options),
                    envelope.Version),
                CancellationToken.None)).Status);
        user = (await users.FindByIdAsync(user.Id, CancellationToken.None))!;
        await users.AddToRoleAsync(user, role.NormalizedName!, CancellationToken.None);
        role = (await roles.FindByIdAsync(role.Id, CancellationToken.None))!;

        var rejected = new Claim("capacity", "rejected-role-mixed-kind-at-513");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            roles.AddClaimAsync(role, rejected, CancellationToken.None));
        await AssertMissingAsync(
            fixture.Documents,
            IdentityStorageManifest.RoleClaimDocumentKind,
            IdentityDocumentId.From(user.TenantId, role.Id, rejected.Type, rejected.Value));
    }

    [Fact]
    public async Task Aggregate_delete_rejects_a_registered_child_owned_by_another_user_without_partial_deletion()
    {
        var fixture = CreateFixture();
        var store = fixture.UserStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        Assert.True((await store.CreateAsync(user, CancellationToken.None)).Succeeded);
        var childId = IdentityDocumentId.From(user.TenantId, "foreign", "claim", "value");
        var foreignChild = new IdentityUserClaimDocument(user.TenantId, "foreign", "claim", "value", childId);
        Assert.Equal(
            DocumentStoreWriteStatus.Saved,
            (await fixture.Documents.SaveAsync(
                new SaveDocumentRequest(
                    IdentityStorageManifest.UserClaimDocumentKind,
                    childId,
                    IdentityStorageManifest.SchemaVersion,
                    JsonSerializer.Serialize(foreignChild, IdentityGroundworkJson.Options),
                    0),
                CancellationToken.None)).Status);

        var ownerEnvelope = await fixture.Documents.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(user.TenantId, user.Id),
            CancellationToken.None);
        Assert.NotNull(ownerEnvelope);
        var owner = JsonSerializer.Deserialize<IdentityUserDocument>(ownerEnvelope!.ContentJson, IdentityGroundworkJson.Options)!;
        Assert.Equal(
            DocumentStoreWriteStatus.Saved,
            (await fixture.Documents.SaveAsync(
                new SaveDocumentRequest(
                    ownerEnvelope.DocumentKind,
                    ownerEnvelope.Id,
                    IdentityStorageManifest.SchemaVersion,
                    JsonSerializer.Serialize(owner with { ClaimIds = [childId] }, IdentityGroundworkJson.Options),
                    ownerEnvelope.Version),
                CancellationToken.None)).Status);
        user = (await store.FindByIdAsync(user.Id, CancellationToken.None))!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync(user, CancellationToken.None));

        Assert.NotNull(await fixture.Documents.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(user.TenantId, user.Id),
            CancellationToken.None));
        Assert.NotNull(await fixture.Documents.LoadAsync(
            IdentityStorageManifest.UserClaimDocumentKind,
            childId,
            CancellationToken.None));
    }

    private static AspNetCoreIdentityGroundworkStoreFixture CreateFixture() =>
        new(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);

    private static async Task AssertMissingAsync(IDocumentStore store, string documentKind, string id) =>
        Assert.Null(await store.LoadAsync(documentKind, id, CancellationToken.None));

    private static async Task<IdentityUserDocument> LoadUserDocumentAsync(IDocumentStore store, AspNetCoreIdentityUser user)
    {
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(user.TenantId, user.Id),
            CancellationToken.None);
        Assert.NotNull(envelope);
        return JsonSerializer.Deserialize<IdentityUserDocument>(envelope!.ContentJson, IdentityGroundworkJson.Options)!;
    }

    private static async Task<IdentityRoleDocument> LoadRoleDocumentAsync(IDocumentStore store, string tenantId, string roleId)
    {
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, roleId),
            CancellationToken.None);
        Assert.NotNull(envelope);
        return JsonSerializer.Deserialize<IdentityRoleDocument>(envelope!.ContentJson, IdentityGroundworkJson.Options)!;
    }
}
