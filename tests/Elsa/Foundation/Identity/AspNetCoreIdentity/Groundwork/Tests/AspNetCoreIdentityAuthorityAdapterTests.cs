using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityAuthorityAdapterTests
{
    [Fact]
    public async Task Framework_user_and_role_writes_are_visible_through_elsa_iam_stores()
    {
        var fixture = CreateFixture();
        var frameworkUsers = fixture.UserStore();
        var frameworkRoles = fixture.RoleStore();
        var elsaUsers = fixture.ElsaUserStore();
        var elsaRoles = fixture.ElsaRoleStore();
        var sourceUser = AspNetCoreIdentityScenarioData.PrimaryUser;
        var sourceRole = AspNetCoreIdentityScenarioData.PrimaryRole;

        Assert.True((await frameworkUsers.CreateAsync(
            AspNetCoreIdentityScenarioData.CreateIdentityUser(sourceUser),
            CancellationToken.None)).Succeeded);
        Assert.True((await frameworkRoles.CreateAsync(
            AspNetCoreIdentityScenarioData.CreateIdentityRole(sourceRole),
            CancellationToken.None)).Succeeded);

        var projectedUser = await elsaUsers.FindAsync(sourceUser.TenantId, sourceUser.Id, CancellationToken.None);
        var projectedRole = await elsaRoles.FindAsync(sourceRole.TenantId, sourceRole.Id, CancellationToken.None);

        Assert.NotNull(projectedUser);
        Assert.Equal(sourceUser.Id, projectedUser!.Id);
        Assert.Equal(sourceUser.TenantId, projectedUser.TenantId);
        Assert.Equal(sourceUser.UserName, projectedUser.UserName);
        Assert.Equal(sourceUser.Email, projectedUser.Email);
        Assert.Equal(sourceUser.DisplayName, projectedUser.DisplayName);
        Assert.NotNull(projectedRole);
        Assert.Equal(sourceRole.Id, projectedRole!.Id);
        Assert.Equal(sourceRole.TenantId, projectedRole.TenantId);
        Assert.Equal(sourceRole.Name, projectedRole.Name);
    }

    [Fact]
    public async Task Elsa_user_and_role_writes_are_visible_through_framework_stores()
    {
        var fixture = CreateFixture();
        var frameworkUsers = fixture.UserStore();
        var frameworkRoles = fixture.RoleStore();
        var user = AspNetCoreIdentityScenarioData.PrimaryUser;
        var role = AspNetCoreIdentityScenarioData.PrimaryRole;

        await fixture.ElsaUserStore().SaveAsync(AspNetCoreIdentityScenarioData.CreateUserRecord(user), CancellationToken.None);
        await fixture.ElsaRoleStore().SaveAsync(AspNetCoreIdentityScenarioData.CreateRoleRecord(role), CancellationToken.None);

        var projectedUser = await frameworkUsers.FindByIdAsync(user.Id, CancellationToken.None);
        var projectedRole = await frameworkRoles.FindByIdAsync(role.Id, CancellationToken.None);

        Assert.NotNull(projectedUser);
        Assert.Equal(user.Id, projectedUser!.Id);
        Assert.Equal(user.TenantId, projectedUser.TenantId);
        Assert.Equal(user.UserName, projectedUser.UserName);
        Assert.Equal(user.Email, projectedUser.Email);
        Assert.Equal(user.DisplayName, projectedUser.DisplayName);
        Assert.NotNull(projectedRole);
        Assert.Equal(role.Id, projectedRole!.Id);
        Assert.Equal(role.Name, projectedRole.Name);
    }

    [Fact]
    public async Task Framework_and_elsa_user_updates_preserve_each_others_authoritative_fields()
    {
        var fixture = CreateFixture();
        var frameworkUsers = fixture.UserStore();
        var user = AspNetCoreIdentityScenarioData.PrimaryUser;
        var frameworkUser = AspNetCoreIdentityScenarioData.CreateIdentityUser(user);

        Assert.True((await frameworkUsers.CreateAsync(frameworkUser, CancellationToken.None)).Succeeded);
        await fixture.ElsaUserStore().SaveAsync(AspNetCoreIdentityScenarioData.CreateUserRecord(user), CancellationToken.None);

        var afterElsaWrite = await frameworkUsers.FindByIdAsync(user.Id, CancellationToken.None);
        Assert.NotNull(afterElsaWrite);
        Assert.Equal(user.PasswordHash!.Reveal(), afterElsaWrite!.PasswordHash);

        await frameworkUsers.SetPhoneNumberAsync(afterElsaWrite, "+31000000444", CancellationToken.None);
        Assert.True((await frameworkUsers.UpdateAsync(afterElsaWrite, CancellationToken.None)).Succeeded);

        var afterFrameworkWrite = await fixture.ElsaUserStore().FindAsync(user.TenantId, user.Id, CancellationToken.None);
        Assert.NotNull(afterFrameworkWrite);
        Assert.Equal(user.DirectPermissions, afterFrameworkWrite!.DirectPermissions);
        Assert.Equal(user.RoleIds, afterFrameworkWrite.RoleIds);
        Assert.Equal(user.Ownership, afterFrameworkWrite.Ownership);
    }

    [Fact]
    public async Task Framework_external_login_writes_are_visible_through_elsa_external_identity_store()
    {
        var fixture = CreateFixture();
        var frameworkUsers = fixture.UserStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var login = AspNetCoreIdentityScenarioData.Logins.First(value => value.UserId == user.Id);

        Assert.True((await frameworkUsers.CreateAsync(user, CancellationToken.None)).Succeeded);
        await frameworkUsers.AddLoginAsync(user, AspNetCoreIdentityScenarioData.CreateLoginInfo(login), CancellationToken.None);

        var projectedLogin = await fixture.ElsaExternalIdentityStore().FindBySubjectAsync(
            login.TenantId,
            login.LoginProvider,
            login.ProviderKey,
            CancellationToken.None);
        var userLogins = await fixture.ElsaExternalIdentityStore().ListForUserAsync(login.TenantId, user.Id, CancellationToken.None);

        Assert.NotNull(projectedLogin);
        Assert.Equal(login.UserId, projectedLogin!.UserId);
        Assert.Equal(login.LoginProvider, projectedLogin.Provider);
        Assert.Equal(login.ProviderKey, projectedLogin.ProviderSubject);
        Assert.Equal(login.UserId, Assert.Single(userLogins).UserId);
    }

    [Fact]
    public async Task Elsa_tenant_membership_uses_the_same_identity_authority_manifest()
    {
        var fixture = CreateFixture();
        var membership = AspNetCoreIdentityScenarioData.CreateMembershipRecord(AspNetCoreIdentityScenarioData.PrimaryMembership);
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        Assert.True((await fixture.UserStore().CreateAsync(user, CancellationToken.None)).Succeeded);

        await fixture.ElsaTenantMembershipStore().SaveAsync(membership, CancellationToken.None);

        var reloaded = await fixture.ElsaTenantMembershipStore().FindAsync(
            membership.TenantId,
            membership.UserId,
            CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(membership.RoleIds, reloaded!.RoleIds);
        Assert.Equal(membership.DirectPermissions, reloaded.DirectPermissions);
        Assert.Equal(TenantMembershipStatus.Active, reloaded.Status);
        Assert.Contains(
            IdentityCompositeDocumentId.From(membership.TenantId, membership.UserId),
            (await LoadUserDocumentAsync(fixture, membership.UserId)).TenantMembershipIds ?? []);
    }

    [Fact]
    public async Task Elsa_revision_aware_external_identity_relink_atomically_moves_old_and_new_owner_registries()
    {
        var fixture = CreateFixture();
        var oldUser = AspNetCoreIdentityScenarioData.PrimaryUser;
        var newUser = AspNetCoreIdentityScenarioData.Users.Single(user =>
            user.Id == AspNetCoreIdentityScenarioData.Ids.UnicodeUser);
        await fixture.ElsaUserStore().SaveAsync(AspNetCoreIdentityScenarioData.CreateUserRecord(oldUser), CancellationToken.None);
        await fixture.ElsaUserStore().SaveAsync(AspNetCoreIdentityScenarioData.CreateUserRecord(newUser), CancellationToken.None);
        var login = AspNetCoreIdentityScenarioData.Logins.First(value => value.UserId == oldUser.Id);
        var external = AspNetCoreIdentityScenarioData.CreateExternalIdentityRecord(login);
        var externalStore = fixture.ElsaExternalIdentityStore();
        await externalStore.SaveAsync(external, CancellationToken.None);
        var revisionStore = Assert.IsAssignableFrom<IRevisionAwareExternalIdentityStore>(externalStore);
        var current = await revisionStore.FindBySubjectWithRevisionAsync(
            login.TenantId,
            login.LoginProvider,
            login.ProviderKey,
            CancellationToken.None);
        Assert.NotNull(current);

        var result = await revisionStore.SaveWithRevisionAsync(
            external with { UserId = newUser.Id },
            current.Revision,
            CancellationToken.None);

        var linkId = IdentityCompositeDocumentId.From(login.TenantId, login.LoginProvider, login.ProviderKey);
        Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
        Assert.DoesNotContain(linkId, (await LoadUserDocumentAsync(fixture, oldUser.Id)).LoginIds ?? []);
        Assert.Contains(linkId, (await LoadUserDocumentAsync(fixture, newUser.Id)).LoginIds ?? []);
        Assert.Equal(newUser.Id, (await externalStore.FindBySubjectAsync(
            login.TenantId, login.LoginProvider, login.ProviderKey, CancellationToken.None))!.UserId);
    }

    [Fact]
    public async Task Elsa_nonrevision_external_identity_save_rejects_foreign_owner_without_transfer()
    {
        var fixture = CreateFixture();
        var oldUser = AspNetCoreIdentityScenarioData.PrimaryUser;
        var newUser = AspNetCoreIdentityScenarioData.Users.Single(user =>
            user.Id == AspNetCoreIdentityScenarioData.Ids.UnicodeUser);
        await fixture.ElsaUserStore().SaveAsync(AspNetCoreIdentityScenarioData.CreateUserRecord(oldUser), CancellationToken.None);
        await fixture.ElsaUserStore().SaveAsync(AspNetCoreIdentityScenarioData.CreateUserRecord(newUser), CancellationToken.None);
        var login = AspNetCoreIdentityScenarioData.Logins.First(value => value.UserId == oldUser.Id);
        var external = AspNetCoreIdentityScenarioData.CreateExternalIdentityRecord(login);
        var externalStore = fixture.ElsaExternalIdentityStore();
        await externalStore.SaveAsync(external, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            externalStore.SaveAsync(external with { UserId = newUser.Id }, CancellationToken.None).AsTask());

        var linkId = IdentityCompositeDocumentId.From(login.TenantId, login.LoginProvider, login.ProviderKey);
        Assert.Contains(linkId, (await LoadUserDocumentAsync(fixture, oldUser.Id)).LoginIds ?? []);
        Assert.DoesNotContain(linkId, (await LoadUserDocumentAsync(fixture, newUser.Id)).LoginIds ?? []);
        Assert.Equal(oldUser.Id, (await externalStore.FindBySubjectAsync(
            login.TenantId, login.LoginProvider, login.ProviderKey, CancellationToken.None))!.UserId);
    }

    private static async Task<IdentityUserDocument> LoadUserDocumentAsync(
        AspNetCoreIdentityGroundworkStoreFixture fixture,
        string userId)
    {
        var envelope = await fixture.Documents.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant, userId),
            CancellationToken.None);
        Assert.NotNull(envelope);
        return JsonSerializer.Deserialize<IdentityUserDocument>(envelope!.ContentJson, IdentityGroundworkJson.Options)!;
    }

    private static AspNetCoreIdentityGroundworkStoreFixture CreateFixture() =>
        new(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
}
