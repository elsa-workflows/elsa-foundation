using System.Data.Common;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Contract-level behavior for the EF authority and relationship stores. The tests resolve only
/// provider-neutral IAM interfaces so they remain useful when the concrete EF store wiring evolves.
/// </summary>
public sealed class IdentityAuthorityEntityFrameworkCoreBehaviorTests
{
    [Fact]
    public async Task User_and_role_records_round_trip_lossless_fields_after_a_store_reopen()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            var user = User("tenant-a", "user-\ud800", "Ada\udc00", "Ada\ud800@example.test") with
            {
                DisplayName = "Ada 😀\udc00",
                RoleIds = Set("role-\ud800", "role-2"),
                DirectPermissions = Set("identity.users.read", "permission\udc00")
            };
            var role = Role("tenant-a", "role-\udc00", "Reviewers\ud800") with
            {
                Description = "Reviewers 😀\udc00",
                Permissions = Set("identity.users.read", "permission\ud800")
            };

            await using (var scope = await EfIdentityScope.OpenAsync(databasePath, user.TenantId))
            {
                await scope.Users.SaveAsync(user);
                await scope.Roles.SaveAsync(role);
            }

            await using var reopened = await EfIdentityScope.OpenAsync(databasePath, user.TenantId);
            var loadedUser = await reopened.Users.FindAsync(user.TenantId, user.Id);
            var loadedRole = await reopened.Roles.FindAsync(role.TenantId, role.Id);

            AssertUser(user, Assert.IsType<UserRecord>(loadedUser));
            AssertRole(role, Assert.IsType<RoleRecord>(loadedRole));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Unconditional_saves_apply_a_later_matching_payload_after_intervening_state()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");

            var userA = User("tenant-a", "aba-user", "User A", "a@example.test");
            var userB = userA with { DisplayName = "User B" };
            await scope.Users.SaveAsync(userA);
            await scope.Users.SaveAsync(userB);
            await scope.Users.SaveAsync(userA);
            AssertUser(userA, Assert.IsType<UserRecord>(await scope.Users.FindAsync(userA.TenantId, userA.Id)));
            Assert.Equal(3, (await scope.Context.Users.AsNoTracking().SingleAsync()).Revision);

            var roleA = Role("tenant-a", "aba-role", "Role A");
            var roleB = roleA with { Description = "Role B" };
            await scope.Roles.SaveAsync(roleA);
            await scope.Roles.SaveAsync(roleB);
            await scope.Roles.SaveAsync(roleA);
            AssertRole(roleA, Assert.IsType<RoleRecord>(await scope.Roles.FindAsync(roleA.TenantId, roleA.Id)));
            Assert.Equal(3, (await scope.Context.Roles.AsNoTracking().SingleAsync()).Revision);

            var mappingA = ClaimMapping("tenant-a", "oidc", "aba-mapping", order: 10);
            var mappingB = mappingA with { Order = 20 };
            await scope.ClaimMappings.SaveAsync(mappingA);
            await scope.ClaimMappings.SaveAsync(mappingB);
            await scope.ClaimMappings.SaveAsync(mappingA);
            Assert.Equal(10, Assert.Single(await scope.ClaimMappings.ListForProviderAsync(mappingA.TenantId, mappingA.Provider)).Order);
            Assert.Equal(3, (await scope.Context.ClaimMappings.AsNoTracking().SingleAsync()).Revision);

            var externalA = ExternalIdentity("tenant-a", userA.Id, "oidc", "aba-subject");
            var externalB = externalA with { LastSeenAt = DateTimeOffset.UnixEpoch.AddDays(1) };
            await scope.ExternalIdentities.SaveAsync(externalA);
            await scope.ExternalIdentities.SaveAsync(externalB);
            await scope.ExternalIdentities.SaveAsync(externalA);
            Assert.Equal(
                externalA.LastSeenAt,
                (await scope.ExternalIdentities.FindBySubjectAsync(externalA.TenantId, externalA.Provider, externalA.ProviderSubject))!.LastSeenAt);
            Assert.Equal(3, (await scope.Context.ExternalIdentities.AsNoTracking().SingleAsync()).Revision);

            var membershipA = Membership("tenant-a", userA.Id, roleA.Id);
            var membershipB = membershipA with { Status = TenantMembershipStatus.Suspended };
            await scope.Memberships.SaveAsync(membershipA);
            await scope.Memberships.SaveAsync(membershipB);
            await scope.Memberships.SaveAsync(membershipA);
            Assert.Equal(
                membershipA.Status,
                (await scope.Memberships.FindAsync(membershipA.TenantId, membershipA.UserId))!.Status);
            Assert.Equal(3, (await scope.Context.TenantMemberships.AsNoTracking().SingleAsync()).Revision);

            Assert.Equal(15, await scope.Context.MutationReceipts.CountAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task User_lookup_is_normalized_and_ambiguous_email_is_not_resolved()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var first = User("tenant-a", "user-a", "Ada", "shared@example.test");
            var second = User("tenant-a", "user-b", "Grace", "shared@example.test");

            await scope.Users.SaveAsync(first);
            await scope.Users.SaveAsync(second);

            AssertUser(first, Assert.IsType<UserRecord>(await scope.Users.FindAsync("tenant-a", "USER-A")));
            Assert.Null(await scope.Users.FindByEmailAsync("tenant-a", "SHARED@EXAMPLE.TEST"));

            var duplicateId = await scope.RevisionUsers.SaveWithRevisionAsync(
                first with { Id = "USER-A", DisplayName = "must-not-win" },
                expectedRevision: null);
            Assert.Equal(IamRevisionSaveStatus.Conflict, duplicateId.Status);
            Assert.Equal("Ada Example", (await scope.Users.FindAsync("tenant-a", first.Id))!.DisplayName);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Same_normalized_user_name_and_email_are_tenant_local()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            var tenantA = User("tenant-a", "user-a", "Shared", "shared@example.test");
            var tenantB = User("tenant-b", "user-b", "shared", "SHARED@EXAMPLE.TEST");
            await using (var first = await EfIdentityScope.OpenAsync(databasePath, tenantA.TenantId))
                await first.Users.SaveAsync(tenantA);
            await using (var second = await EfIdentityScope.OpenAsync(databasePath, tenantB.TenantId))
                await second.Users.SaveAsync(tenantB);

            await using var verifyA = await EfIdentityScope.OpenAsync(databasePath, tenantA.TenantId);
            await using var verifyB = await EfIdentityScope.OpenAsync(databasePath, tenantB.TenantId);
            AssertUser(tenantA, Assert.IsType<UserRecord>(await verifyA.Users.FindByEmailAsync(tenantA.TenantId, tenantA.Email!)));
            AssertUser(tenantB, Assert.IsType<UserRecord>(await verifyB.Users.FindByEmailAsync(tenantB.TenantId, tenantB.Email!)));
            Assert.Equal(2, await verifyA.Context.Users.CountAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Unique_email_policy_rejects_collisions_and_rename_releases_old_reservations()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var uniqueUsers = new EfUserStore(
                scope.Context,
                scope.Access,
                emailUniquenessPolicy: IdentityEmailUniquenessPolicy.Unique);
            var first = User("tenant-a", "unique-a", "Ada", "ada@example.test");
            await uniqueUsers.SaveAsync(first);

            var duplicate = await uniqueUsers.SaveWithRevisionAsync(
                first with { Id = "unique-b", UserName = "Grace", Email = "ada@example.test" },
                expectedRevision: null);
            Assert.Equal(IamRevisionSaveStatus.Conflict, duplicate.Status);
            Assert.Single(await scope.Context.Users.AsNoTracking().ToListAsync());

            var current = await uniqueUsers.FindWithRevisionAsync(first.TenantId, first.Id);
            Assert.NotNull(current);
            var renamed = await uniqueUsers.SaveWithRevisionAsync(
                first with { UserName = "Grace", Email = "grace@example.test" },
                current!.Revision);
            Assert.Equal(IamRevisionSaveStatus.Saved, renamed.Status);

            var reused = await uniqueUsers.SaveWithRevisionAsync(
                first with { Id = "unique-b", UserName = "Ada", Email = "ada@example.test" },
                expectedRevision: null);
            Assert.Equal(IamRevisionSaveStatus.Saved, reused.Status);
            Assert.Equal(2, await scope.Context.UserNameReservations.CountAsync());
            Assert.Equal(2, await scope.Context.EmailReservations.CountAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    /// <summary>
    /// The wrapped counterpart of the case below: a save failure re-raised under an
    /// <see cref="InvalidOperationException"/> must still be classified by its constraint name.
    /// </summary>
    /// <remarks>
    /// This passes on the unfixed code too, and deliberately says so rather than implying more coverage than it has.
    /// Classification here runs through <c>ClassifyProviderUniqueConstraint(exception.ToString())</c>, and
    /// <see cref="Exception.ToString"/> already renders the whole inner chain, so the constraint name survives a
    /// wrapper without help. What it pins is that no clause on the way out rejects the wrapper before classification
    /// is reached, which is what the type-keyed clauses used to do. The chain-walking branch of
    /// <c>IsMutationReceiptConflict</c> and <c>UniqueConflictUnit</c>, which reads the save's Entries, is reached only
    /// from <c>EfIdentityAtomicWrite.ReconcileOrConflictAsync</c> and is still unguarded; see #1814's review notes.
    /// </remarks>
    [Theory]
    [InlineData("PK_identity_users", EfIdentityAuthorityConflict.None)]
    [InlineData("ux_identity_user_name_reservations_key", EfIdentityAuthorityConflict.UserName)]
    public Task A_wrapped_provider_constraint_identity_still_wins(string constraintName, EfIdentityAuthorityConflict expectedConflict) =>
        Provider_constraint_identity_wins_over_mixed_pending_entries(constraintName, expectedConflict, wrapped: true);

    [Theory]
    [InlineData("PK_identity_users", EfIdentityAuthorityConflict.None)]
    [InlineData("ux_identity_user_name_reservations_key", EfIdentityAuthorityConflict.UserName)]
    public async Task Provider_constraint_identity_wins_over_mixed_pending_entries(
        string constraintName,
        EfIdentityAuthorityConflict expectedConflict,
        bool wrapped = false)
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using (var schema = CreateContext(databasePath))
                await schema.Database.EnsureCreatedAsync();

            var interceptor = new NamedUniqueConstraintFailureInterceptor(constraintName, wrapped);
            await using var context = CreateContext(databasePath, interceptor);
            var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var coordinator = new EfIdentityAuthorityAggregateCoordinator(context, access);

            var result = await coordinator.SaveUserAsync(
                User("tenant-a", "user-mixed-conflict", "Mixed Conflict", null),
                expectedVersion: null,
                requireUniqueEmail: false);

            Assert.Equal(EfIdentityWriteStatus.Conflict, result.WriteResult.Status);
            Assert.Equal(expectedConflict, result.Conflict);
            Assert.Contains(typeof(UserEntity), interceptor.PendingEntityTypes);
            Assert.Contains(typeof(UserNameReservationEntity), interceptor.PendingEntityTypes);
            Assert.Contains(typeof(MutationReceiptEntity), interceptor.PendingEntityTypes);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Unconditional_claim_mapping_save_that_loses_a_create_race_fails_instead_of_reporting_success()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using (var schema = CreateContext(databasePath))
                await schema.Database.EnsureCreatedAsync();

            var interceptor = new LostClaimMappingCreateInterceptor();
            await using var context = CreateContext(databasePath, interceptor);
            var store = new EfClaimMappingStore(context, new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
            var rule = ClaimMapping("tenant-a", "oidc", "lost-create", order: 1);

            var failure = await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() => store.SaveAsync(rule).AsTask());
            Assert.Equal("Unable to save the Identity claim mapping.", failure.Message);
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Empty(context.ChangeTracker.Entries());
            Assert.Empty(await context.ClaimMappings.AsNoTracking().ToListAsync());

            // A revision-aware create reports the same lost race as a conflict, not as a failure.
            Assert.Equal(IamRevisionSaveStatus.Conflict, (await store.SaveWithRevisionAsync(rule, expectedRevision: null)).Status);

            interceptor.Armed = false;
            await store.SaveAsync(rule);
            Assert.Equal(rule.Order, Assert.Single(await store.ListForProviderAsync(rule.TenantId, rule.Provider)).Order);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Concurrent_same_name_creates_have_one_duplicate_name_loser()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using (var schema = CreateContext(databasePath))
                await schema.Database.EnsureCreatedAsync();

            await using var firstContext = CreateContext(databasePath);
            await using var secondContext = CreateContext(databasePath);
            var firstAccess = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var secondAccess = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var first = new EfIdentityAuthorityAggregateCoordinator(firstContext, firstAccess);
            var second = new EfIdentityAuthorityAggregateCoordinator(secondContext, secondAccess);

            var results = await Task.WhenAll(
                first.SaveUserAsync(
                    User("tenant-a", "concurrent-name-a", "Shared Name", null),
                    expectedVersion: null,
                    requireUniqueEmail: false),
                second.SaveUserAsync(
                    User("tenant-a", "concurrent-name-b", "shared name", null),
                    expectedVersion: null,
                    requireUniqueEmail: false));

            Assert.Single(results, result => result.WriteResult.Succeeded);
            var loser = Assert.Single(results, result => !result.WriteResult.Succeeded);
            Assert.Equal(EfIdentityWriteStatus.Conflict, loser.WriteResult.Status);
            Assert.Equal(EfIdentityAuthorityConflict.UserName, loser.Conflict);

            await using var verification = CreateContext(databasePath);
            Assert.Equal(1, await verification.Users.CountAsync());
            Assert.Equal(1, await verification.UserNameReservations.CountAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Role_pages_are_bounded_and_stably_ordered_by_the_contract_key()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            foreach (var id in new[] { "role-z", "role-a", "role-m", "role-b" })
                await scope.Roles.SaveAsync(Role("tenant-a", id, id));

            var roles = Assert.IsAssignableFrom<IPagedRoleStore>(scope.Roles);
            var firstPage = await roles.ListPageAsync("tenant-a", new IamPageRequest(skip: 0, take: 2));
            var secondPage = await roles.ListPageAsync("tenant-a", new IamPageRequest(skip: 2, take: 2));

            Assert.Equal(4, firstPage.TotalCount);
            Assert.Equal(4, secondPage.TotalCount);
            Assert.Equal(["role-a", "role-b"], firstPage.Items.Select(role => role.Id));
            Assert.Equal(["role-m", "role-z"], secondPage.Items.Select(role => role.Id));
            Assert.Equal(2, firstPage.Items.Count);
            Assert.Equal(2, secondPage.Items.Count);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Claim_mapping_and_external_identity_pages_are_bounded_and_deterministic()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "user-a", "Ada", null);
            await scope.Users.SaveAsync(user);
            foreach (var order in new[] { 30, 10, 20 })
                await scope.ClaimMappings.SaveAsync(ClaimMapping("tenant-a", "google", $"rule-{order}", order));
            foreach (var subject in new[] { "subject-z", "subject-a", "subject-m" })
                await scope.ExternalIdentities.SaveAsync(ExternalIdentity("tenant-a", user.Id, "google", subject));

            var claimPages = Assert.IsAssignableFrom<IPagedClaimMappingStore>(scope.ClaimMappings);
            var firstClaims = await claimPages.ListForProviderPageAsync(
                "tenant-a", "google", new IamPageRequest(skip: 0, take: 2));
            var secondClaims = await claimPages.ListForProviderPageAsync(
                "tenant-a", "google", new IamPageRequest(skip: 2, take: 2));
            Assert.Equal(3, firstClaims.TotalCount);
            Assert.Equal([10, 20], firstClaims.Items.Select(rule => rule.Order));
            Assert.Equal([30], secondClaims.Items.Select(rule => rule.Order));

            var externalPages = Assert.IsAssignableFrom<IPagedExternalIdentityStore>(scope.ExternalIdentities);
            var firstExternal = await externalPages.ListForUserPageAsync(
                "tenant-a", user.Id, new IamPageRequest(skip: 0, take: 2));
            var secondExternal = await externalPages.ListForUserPageAsync(
                "tenant-a", user.Id, new IamPageRequest(skip: 2, take: 2));
            Assert.Equal(3, firstExternal.TotalCount);
            Assert.Equal(["subject-a", "subject-m"], firstExternal.Items.Select(login => login.ProviderSubject));
            Assert.Equal(["subject-z"], secondExternal.Items.Select(login => login.ProviderSubject));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Sortable_identity_keys_accept_the_schema_boundary_and_reject_over_limit_before_provider_io()
    {
        const int maximumSortableCodeUnits = 400; // IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength.
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using (var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a"))
            {
                var roleId = new string('r', maximumSortableCodeUnits);
                await scope.Roles.SaveAsync(Role("tenant-a", roleId, "Boundary role"));

                var ruleId = new string('c', maximumSortableCodeUnits);
                await scope.ClaimMappings.SaveAsync(ClaimMapping("tenant-a", "boundary", ruleId, 1));

                var user = User("tenant-a", "boundary-user", "Boundary user", null);
                await scope.Users.SaveAsync(user);
                var provider = new string('p', maximumSortableCodeUnits);
                var subject = new string('s', maximumSortableCodeUnits);
                await scope.ExternalIdentities.SaveAsync(ExternalIdentity("tenant-a", user.Id, provider, subject));

                Assert.NotNull(await scope.Roles.FindAsync("tenant-a", roleId));
                Assert.NotNull(await scope.RevisionClaimMappings.FindWithRevisionAsync("tenant-a", "boundary", ruleId));
                Assert.NotNull(await scope.ExternalIdentities.FindBySubjectAsync("tenant-a", provider, subject));
            }

            var interceptor = new CommandCaptureInterceptor();
            await using var context = CreateContext(databasePath, interceptor);
            var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var roles = new EfRoleStore(context, access);
            var claimMappings = new EfClaimMappingStore(context, access);
            var externalIdentities = new EfExternalIdentityStore(context, access);

            var overLimitRoleId = new string('r', maximumSortableCodeUnits + 1);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                roles.SaveAsync(Role("tenant-a", overLimitRoleId, "Too long")).AsTask());

            var overLimitRuleId = new string('c', maximumSortableCodeUnits + 1);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                claimMappings.SaveAsync(ClaimMapping("tenant-a", "boundary", overLimitRuleId, 2)).AsTask());

            var overLimitProvider = new string('p', maximumSortableCodeUnits + 1);
            var overLimitSubject = "subject";
            await Assert.ThrowsAsync<ArgumentException>(() =>
                externalIdentities.SaveAsync(ExternalIdentity("tenant-a", "boundary-user", overLimitProvider, overLimitSubject)).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() =>
                externalIdentities.SaveAsync(ExternalIdentity(
                    "tenant-a",
                    "boundary-user",
                    "provider",
                    new string('s', maximumSortableCodeUnits + 1))).AsTask());

            Assert.Empty(interceptor.Commands);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Paged_queries_push_the_take_to_the_sqlite_provider_for_all_bounded_contracts()
    {
        var databasePath = TemporaryDatabasePath();
        var interceptor = new ReaderCommandCaptureInterceptor();
        try
        {
            await using var context = CreateContext(databasePath, interceptor);
            await context.Database.EnsureCreatedAsync();
            var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var users = new EfUserStore(context, access);
            var roles = new EfRoleStore(context, access);
            var claimMappings = new EfClaimMappingStore(context, access);
            var externalIdentities = new EfExternalIdentityStore(context, access);
            var user = User("tenant-a", "bounded-page-user", "Page User", null);
            await users.SaveAsync(user);
            foreach (var id in new[] { "role-a", "role-b", "role-c" })
                await roles.SaveAsync(Role("tenant-a", id, id));
            foreach (var order in new[] { 10, 20, 30 })
                await claimMappings.SaveAsync(ClaimMapping("tenant-a", "google", $"rule-{order}", order));
            foreach (var subject in new[] { "subject-a", "subject-b", "subject-c" })
                await externalIdentities.SaveAsync(ExternalIdentity("tenant-a", user.Id, "google", subject));

            interceptor.Commands.Clear();
            await roles.ListPageAsync("tenant-a", new IamPageRequest(skip: 0, take: 2));
            await claimMappings.ListForProviderPageAsync("tenant-a", "google", new IamPageRequest(skip: 0, take: 2));
            await externalIdentities.ListForUserPageAsync("tenant-a", user.Id, new IamPageRequest(skip: 0, take: 2));

            Assert.Contains(
                interceptor.Commands,
                command => command.Contains("identity_roles", StringComparison.OrdinalIgnoreCase) &&
                           command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                interceptor.Commands,
                command => command.Contains("identity_claim_mappings", StringComparison.OrdinalIgnoreCase) &&
                           command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                interceptor.Commands,
                command => command.Contains("identity_external_logins", StringComparison.OrdinalIgnoreCase) &&
                           command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Normalized_role_name_reservation_is_tenant_local_and_create_only()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using (var first = await EfIdentityScope.OpenAsync(databasePath, "tenant-a"))
            {
                var role = Role("tenant-a", "role-a", "Operators");
                var created = await first.RevisionRoles.SaveWithRevisionAsync(role, expectedRevision: null);
                Assert.Equal(IamRevisionSaveStatus.Saved, created.Status);
            }

            await using (var sameTenant = await EfIdentityScope.OpenAsync(databasePath, "tenant-a"))
            {
                var duplicate = await sameTenant.RevisionRoles.SaveWithRevisionAsync(
                    Role("tenant-a", "role-b", "operators"), expectedRevision: null);
                Assert.Equal(IamRevisionSaveStatus.Conflict, duplicate.Status);

                var current = await sameTenant.RevisionRoles.FindWithRevisionAsync("tenant-a", "role-a");
                Assert.NotNull(current);
                var renamed = await sameTenant.RevisionRoles.SaveWithRevisionAsync(
                    Role("tenant-a", "role-a", "Operators V2"), current!.Revision);
                Assert.Equal(IamRevisionSaveStatus.Saved, renamed.Status);

                var reused = await sameTenant.RevisionRoles.SaveWithRevisionAsync(
                    Role("tenant-a", "role-b", "Operators"), expectedRevision: null);
                Assert.Equal(IamRevisionSaveStatus.Saved, reused.Status);
            }

            await using var otherTenant = await EfIdentityScope.OpenAsync(databasePath, "tenant-b");
            var independent = await otherTenant.RevisionRoles.SaveWithRevisionAsync(
                Role("tenant-b", "role-b", "OPERATORS"), expectedRevision: null);
            Assert.Equal(IamRevisionSaveStatus.Saved, independent.Status);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task External_identity_create_or_same_owner_is_idempotent_but_owner_change_requires_revision()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var firstOwner = User("tenant-a", "user-a", "Ada", null);
            var secondOwner = User("tenant-a", "user-b", "Grace", null);
            var external = ExternalIdentity("tenant-a", firstOwner.Id, "google", "subject-a");
            await scope.Users.SaveAsync(firstOwner);
            await scope.Users.SaveAsync(secondOwner);

            await scope.ExternalIdentities.SaveAsync(external);
            await scope.ExternalIdentities.SaveAsync(external with { LastSeenAt = DateTimeOffset.UnixEpoch.AddMinutes(1) });
            Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(1),
                (await scope.ExternalIdentities.FindBySubjectAsync("tenant-a", "google", "subject-a"))!.LastSeenAt);

            var ownerConflict = await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                scope.ExternalIdentities.SaveAsync(external with { UserId = secondOwner.Id }).AsTask());
            Assert.IsType<InvalidOperationException>(ownerConflict.InnerException);
            Assert.Equal(firstOwner.Id,
                (await scope.ExternalIdentities.FindBySubjectAsync("tenant-a", "google", "subject-a"))!.UserId);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Revision_aware_authority_and_relationship_records_reject_stale_writes_without_mutation()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "user-a", "Ada", "ada@example.test");
            var role = Role("tenant-a", "role-a", "Administrators");
            var rule = ClaimMapping("tenant-a", "google", "rule-a", order: 10);
            var membership = Membership("tenant-a", user.Id, role.Id);
            var external = ExternalIdentity("tenant-a", user.Id, "google", "subject-a");

            await scope.Users.SaveAsync(user);
            await scope.Roles.SaveAsync(role);
            await scope.ClaimMappings.SaveAsync(rule);
            await scope.Memberships.SaveAsync(membership);
            await scope.ExternalIdentities.SaveAsync(external);

            var userBefore = await scope.RevisionUsers.FindWithRevisionAsync(user.TenantId, user.Id);
            var roleBefore = await scope.RevisionRoles.FindWithRevisionAsync(role.TenantId, role.Id);
            var ruleBefore = await scope.RevisionClaimMappings.FindWithRevisionAsync(rule.TenantId, rule.Provider, rule.Id);
            var membershipBefore = await scope.RevisionMemberships.FindWithRevisionAsync(membership.TenantId, membership.UserId);
            var externalBefore = await scope.RevisionExternalIdentities.FindBySubjectWithRevisionAsync(
                external.TenantId, external.Provider, external.ProviderSubject);
            Assert.NotNull(userBefore);
            Assert.NotNull(roleBefore);
            Assert.NotNull(ruleBefore);
            Assert.NotNull(membershipBefore);
            Assert.NotNull(externalBefore);

            Assert.Equal(IamRevisionSaveStatus.Saved,
                (await scope.RevisionUsers.SaveWithRevisionAsync(user with { DisplayName = "winner" }, userBefore!.Revision)).Status);
            Assert.Equal(IamRevisionSaveStatus.Saved,
                (await scope.RevisionRoles.SaveWithRevisionAsync(role with { Description = "winner" }, roleBefore!.Revision)).Status);
            Assert.Equal(IamRevisionSaveStatus.Saved,
                (await scope.RevisionClaimMappings.SaveWithRevisionAsync(rule with { Order = 20 }, ruleBefore!.Revision)).Status);
            Assert.Equal(IamRevisionSaveStatus.Saved,
                (await scope.RevisionMemberships.SaveWithRevisionAsync(membership with { Status = TenantMembershipStatus.Suspended }, membershipBefore!.Revision)).Status);
            Assert.Equal(IamRevisionSaveStatus.Saved,
                (await scope.RevisionExternalIdentities.SaveWithRevisionAsync(external with { LastSeenAt = DateTimeOffset.UnixEpoch.AddMinutes(1) }, externalBefore!.Revision)).Status);

            Assert.Equal(IamRevisionSaveStatus.Conflict,
                (await scope.RevisionUsers.SaveWithRevisionAsync(user with { DisplayName = "stale" }, userBefore.Revision)).Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict,
                (await scope.RevisionRoles.SaveWithRevisionAsync(role with { Description = "stale" }, roleBefore.Revision)).Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict,
                (await scope.RevisionClaimMappings.SaveWithRevisionAsync(rule with { Order = 30 }, ruleBefore.Revision)).Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict,
                (await scope.RevisionMemberships.SaveWithRevisionAsync(membership with { Status = TenantMembershipStatus.Active }, membershipBefore.Revision)).Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict,
                (await scope.RevisionExternalIdentities.SaveWithRevisionAsync(external with { LastSeenAt = DateTimeOffset.UnixEpoch.AddMinutes(2) }, externalBefore.Revision)).Status);

            Assert.Equal("winner", (await scope.Users.FindAsync(user.TenantId, user.Id))!.DisplayName);
            Assert.Equal("winner", (await scope.Roles.FindAsync(role.TenantId, role.Id))!.Description);
            Assert.Equal(20, Assert.Single(await scope.ClaimMappings.ListForProviderAsync(rule.TenantId, rule.Provider)).Order);
            Assert.Equal(TenantMembershipStatus.Suspended, (await scope.Memberships.FindAsync(membership.TenantId, membership.UserId))!.Status);
            Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(1),
                (await scope.ExternalIdentities.FindBySubjectAsync(external.TenantId, external.Provider, external.ProviderSubject))!.LastSeenAt);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task External_identity_revision_write_can_rebind_to_another_existing_user()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var originalOwner = User("tenant-a", "user-a", "Ada", "ada@example.test");
            var newOwner = User("tenant-a", "user-b", "Grace", "grace@example.test");
            var external = ExternalIdentity("tenant-a", originalOwner.Id, "google", "subject-a");
            await scope.Users.SaveAsync(originalOwner);
            await scope.Users.SaveAsync(newOwner);
            await scope.ExternalIdentities.SaveAsync(external);

            var current = await scope.RevisionExternalIdentities.FindBySubjectWithRevisionAsync(
                external.TenantId, external.Provider, external.ProviderSubject);
            Assert.NotNull(current);
            var result = await scope.RevisionExternalIdentities.SaveWithRevisionAsync(
                external with { UserId = newOwner.Id }, current!.Revision);

            Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
            Assert.Empty(await scope.ExternalIdentities.ListForUserAsync(external.TenantId, originalOwner.Id));
            Assert.Equal(newOwner.Id, Assert.Single(await scope.ExternalIdentities.ListForUserAsync(external.TenantId, newOwner.Id)).UserId);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Tenant_mismatch_is_rejected_before_provider_io_and_clears_tracking()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore("tenant-a");
        services.AddIdentityIamEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=/path/that-must-not-be-opened/identity-authority.db"
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => users.FindAsync("tenant-b", "user-a").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => users.SaveAsync(User("tenant-b", "user-a", "Ada", null)).AsTask());
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Cancellation_before_provider_work_leaves_the_EF_tracker_empty()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                scope.Users.FindAsync("tenant-a", "missing", cancellation.Token).AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                scope.Roles.ListAsync("tenant-a", cancellation.Token).AsTask());
            Assert.Empty(scope.Context.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Authority_and_relationship_writes_rollback_as_one_EF_transaction()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "user-a", "Ada", "ada@example.test");
            var role = Role("tenant-a", "role-a", "Administrators");
            var external = ExternalIdentity("tenant-a", user.Id, "google", "subject-a");
            await using (var transaction = await scope.Context.Database.BeginTransactionAsync())
            {
                await scope.Users.SaveAsync(user);
                await scope.Roles.SaveAsync(role);
                await scope.ExternalIdentities.SaveAsync(external);
                await transaction.RollbackAsync();
            }

            await using var reopened = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            Assert.Null(await reopened.Users.FindAsync(user.TenantId, user.Id));
            Assert.Null(await reopened.Roles.FindAsync(role.TenantId, role.Id));
            Assert.Null(await reopened.ExternalIdentities.FindBySubjectAsync(external.TenantId, external.Provider, external.ProviderSubject));
            Assert.Empty(reopened.Context.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Authority_entities_round_trip_lockout_tokens_recovery_relationships_and_receipts_losslessly()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using (var context = CreateContext(databasePath))
            {
                await context.Database.EnsureCreatedAsync();
                context.Users.Add(new UserEntity
                {
                    Id = "tenant-a\u001fuser-a",
                    TenantId = "tenant-a\ud800",
                    TenantLookupKey = "tenant-a",
                    UserId = "user-a\udc00",
                    UserName = "Ada\ud800",
                    NormalizedUserName = "ADA\ud800",
                    NormalizedUserNameKey = "tenant-a\u001fADA\ud800",
                    Email = "ada\ud800@example.test",
                    NormalizedEmail = "ADA\ud800@EXAMPLE.TEST",
                    NormalizedEmailKey = "tenant-a\u001fADA\ud800@EXAMPLE.TEST",
                    DisplayName = "Ada 😀\udc00",
                    Status = (int)UserStatus.Locked,
                    Ownership = (int)ResourceOwnership.External,
                    RoleIdsJson = "[\"role-a\"]",
                    DirectPermissionsJson = "[\"identity.users.read\"]",
                    ClaimIdsJson = "[\"claim-a\"]",
                    LoginIdsJson = "[\"login-a\"]",
                    RoleLinkIdsJson = "[\"link-a\"]",
                    TokenIdsJson = "[\"token-a\"]",
                    TenantMembershipIdsJson = "[\"membership-a\"]",
                    EmailConfirmed = true,
                    PasswordHash = "hash\ud800",
                    SecurityStamp = "security\udc00",
                    ConcurrencyStamp = "concurrency😀",
                    PhoneNumber = "+31\ud800",
                    PhoneNumberConfirmed = true,
                    TwoFactorEnabled = true,
                    LockoutEnd = DateTimeOffset.ParseExact("2030-04-05T06:07:08.1234567-03:30", "O", System.Globalization.CultureInfo.InvariantCulture),
                    LockoutEnabled = true,
                    AccessFailedCount = 7,
                    Revision = 1
                });
                context.Roles.Add(new RoleEntity
                {
                    Id = "tenant-a\u001frole-a",
                    TenantId = "tenant-a\ud800",
                    TenantLookupKey = "tenant-a",
                    RoleId = "role-a\udc00",
                    Name = "Operators\ud800",
                    NormalizedName = "OPERATORS\ud800",
                    NormalizedNameKey = "tenant-a\u001fOPERATORS\ud800",
                    Description = "Operators 😀\udc00",
                    PermissionsJson = "[\"identity.users.read\"]",
                    System = false,
                    ClaimIdsJson = "[\"role-claim-a\"]",
                    UserLinkIdsJson = "[\"link-a\"]",
                    ConcurrencyStamp = "role-concurrency\ud800",
                    Revision = 1
                });
                context.UserClaims.Add(new UserClaimEntity
                {
                    Id = "tenant-a\u001fclaim-a",
                    TenantId = "tenant-a\ud800",
                    TenantLookupKey = "tenant-a",
                    UserId = "user-a\udc00",
                    UserLookupKey = "tenant-a\u001fuser-a\udc00",
                    ClaimType = "preferred_username\ud800",
                    ClaimValue = "ada\udc00",
                    ClaimKey = "preferred_username\u001fada\udc00",
                    Revision = 1
                });
                context.RoleClaims.Add(new RoleClaimEntity
                {
                    Id = "tenant-a\u001frole-claim-a",
                    TenantId = "tenant-a\ud800",
                    TenantLookupKey = "tenant-a",
                    RoleId = "role-a\udc00",
                    RoleLookupKey = "tenant-a\u001frole-a\udc00",
                    ClaimType = "permission\ud800",
                    ClaimValue = "identity.users.read\udc00",
                    Revision = 1
                });
                context.UserRoles.Add(new UserRoleEntity
                {
                    Id = "link-a",
                    TenantId = "tenant-a\ud800",
                    TenantLookupKey = "tenant-a",
                    UserId = "user-a\udc00",
                    UserLookupKey = "tenant-a\u001fuser-a\udc00",
                    RoleId = "role-a\udc00",
                    RoleLookupKey = "tenant-a\u001frole-a\udc00",
                    Revision = 1
                });
                context.UserTokens.Add(new UserTokenEntity
                {
                    Id = "token-a",
                    TenantId = "tenant-a\ud800",
                    TenantLookupKey = "tenant-a",
                    UserId = "user-a\udc00",
                    UserLookupKey = "tenant-a\u001fuser-a\udc00",
                    LoginProvider = "Identity\ud800",
                    Name = "RecoveryCode\udc00",
                    Value = "recovery\ud800😀\udc00",
                    Revision = 1
                });
                context.TenantMemberships.Add(new TenantMembershipEntity
                {
                    Id = "tenant-a\u001fuser-a",
                    TenantId = "tenant-a\ud800",
                    TenantLookupKey = "tenant-a",
                    UserId = "user-a\udc00",
                    UserLookupKey = "tenant-a\u001fuser-a\udc00",
                    Status = (int)TenantMembershipStatus.Suspended,
                    RoleIdsJson = "[\"role-a\"]",
                    DirectPermissionsJson = "[\"identity.users.read\"]",
                    Revision = 1
                });
                context.UserNameReservations.Add(new UserNameReservationEntity
                {
                    Id = "tenant-a\u001fADA\ud800",
                    TenantId = "tenant-a\ud800",
                    TenantLookupKey = "tenant-a",
                    NormalizedUserName = "ADA\ud800",
                    NormalizedUserNameKey = "tenant-a\u001fADA\ud800",
                    UserId = "user-a\udc00",
                    Revision = 1
                });
                context.EmailReservations.Add(new EmailReservationEntity
                {
                    Id = "tenant-a\u001fADA\ud800@EXAMPLE.TEST",
                    TenantId = "tenant-a\ud800",
                    TenantLookupKey = "tenant-a",
                    NormalizedEmail = "ADA\ud800@EXAMPLE.TEST",
                    NormalizedEmailKey = "tenant-a\u001fADA\ud800@EXAMPLE.TEST",
                    UserId = "user-a\udc00",
                    Revision = 1
                });
                context.RoleNameReservations.Add(new RoleNameReservationEntity
                {
                    Id = "tenant-a\u001fOPERATORS\ud800",
                    TenantId = "tenant-a\ud800",
                    TenantLookupKey = "tenant-a",
                    NormalizedRoleName = "OPERATORS\ud800",
                    NormalizedRoleNameKey = "tenant-a\u001fOPERATORS\ud800",
                    RoleId = "role-a\udc00",
                    Revision = 1
                });
                context.ExternalIdentities.Add(new ExternalIdentityEntity
                {
                    Id = "tenant-a\u001fgoogle\u001fsubject-a",
                    TenantId = "tenant-a\ud800",
                    TenantLookupKey = "tenant-a",
                    Provider = "google\ud800",
                    ProviderLookupKey = "google",
                    ProviderSubject = "subject-a\udc00",
                    ProviderSubjectLookupKey = "subject-a",
                    UserId = "user-a\udc00",
                    UserLookupKey = "tenant-a\u001fuser-a\udc00",
                    LinkedAt = DateTimeOffset.UnixEpoch,
                    LastSeenAt = DateTimeOffset.ParseExact("2031-02-03T04:05:06.1234567+05:30", "O", System.Globalization.CultureInfo.InvariantCulture),
                    LinkPolicy = (int)ExternalIdentityLinkPolicy.Admin,
                    Revision = 1
                });
                context.MutationReceipts.Add(new MutationReceiptEntity
                {
                    Id = "receipt-a",
                    MutationReceiptId = "receipt-a\ud800",
                    OperationId = "operation-a\udc00",
                    RequestFingerprint = "fingerprint\ud800",
                    Status = 1,
                    Version = 3,
                    Message = "committed\udc00",
                    AuthoritativeId = "user-a\udc00",
                    CreatedAt = DateTimeOffset.UnixEpoch,
                    ExpiresAt = DateTimeOffset.ParseExact("2040-01-02T03:04:05.1234567Z", "O", System.Globalization.CultureInfo.InvariantCulture),
                    Revision = 1
                });

                await context.SaveChangesAsync();
            }

            await using var reopened = CreateContext(databasePath);
            var user = await reopened.Users.AsNoTracking().SingleAsync();
            Assert.Equal("tenant-a\ud800", user.TenantId);
            Assert.Equal("user-a\udc00", user.UserId);
            Assert.Equal("Ada\ud800", user.UserName);
            Assert.Equal("Ada 😀\udc00", user.DisplayName);
            Assert.Equal("hash\ud800", user.PasswordHash);
            Assert.Equal(UserStatus.Locked, (UserStatus)user.Status);
            Assert.Equal(7, user.AccessFailedCount);
            Assert.Equal(DateTimeOffset.ParseExact("2030-04-05T06:07:08.1234567-03:30", "O", System.Globalization.CultureInfo.InvariantCulture), user.LockoutEnd);

            var role = await reopened.Roles.AsNoTracking().SingleAsync();
            Assert.Equal("Operators\ud800", role.Name);
            Assert.Equal("role-a\udc00", role.RoleId);
            Assert.Equal("role-concurrency\ud800", role.ConcurrencyStamp);
            Assert.Equal("recovery\ud800😀\udc00", (await reopened.UserTokens.AsNoTracking().SingleAsync()).Value);
            Assert.Equal(TenantMembershipStatus.Suspended, (TenantMembershipStatus)(await reopened.TenantMemberships.AsNoTracking().SingleAsync()).Status);
            Assert.Equal("ada\udc00", (await reopened.UserClaims.AsNoTracking().SingleAsync()).ClaimValue);
            Assert.Equal("identity.users.read\udc00", (await reopened.RoleClaims.AsNoTracking().SingleAsync()).ClaimValue);
            Assert.Equal("google\ud800", (await reopened.ExternalIdentities.AsNoTracking().SingleAsync()).Provider);
            var receipt = await reopened.MutationReceipts.AsNoTracking().SingleAsync();
            Assert.Equal("receipt-a\ud800", receipt.MutationReceiptId);
            Assert.Equal("operation-a\udc00", receipt.OperationId);
            Assert.Equal(3, receipt.Version);
            Assert.Equal(DateTimeOffset.ParseExact("2040-01-02T03:04:05.1234567Z", "O", System.Globalization.CultureInfo.InvariantCulture), receipt.ExpiresAt);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Relationship_unique_keys_are_tenant_local_and_duplicate_receipts_are_rejected()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.EnsureCreatedAsync();
            context.ExternalIdentities.AddRange(
                ExternalEntity("tenant-a", "user-a", "google", "subject"),
                ExternalEntity("tenant-b", "user-a", "google", "subject"));
            context.UserRoles.AddRange(
                UserRoleEntity("tenant-a", "user-a", "role-a", "link-a"),
                UserRoleEntity("tenant-b", "user-a", "role-a", "link-b"));
            context.UserTokens.AddRange(
                UserTokenEntity("tenant-a", "user-a", "Identity", "Recovery", "a"),
                UserTokenEntity("tenant-b", "user-a", "Identity", "Recovery", "b"));
            context.MutationReceipts.Add(MutationReceiptEntity("receipt-unique", "operation-a"));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            context.ExternalIdentities.Add(ExternalEntity("tenant-a", "user-b", "google", "subject"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            context.ChangeTracker.Clear();
            Assert.Equal(2, await context.ExternalIdentities.CountAsync());

            context.MutationReceipts.Add(MutationReceiptEntity("receipt-unique", "operation-b"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            context.ChangeTracker.Clear();
            Assert.Equal(1, await context.MutationReceipts.CountAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Aggregate_delete_removes_relationships_reservations_and_role_links_atomically()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "user-delete", "Delete Me", "delete@example.test");
            var role = Role("tenant-a", "role-delete", "Delete Role");
            var users = new EfUserStore(scope.Context, scope.Access, emailUniquenessPolicy: IdentityEmailUniquenessPolicy.Unique);
            await users.SaveAsync(user);
            await scope.Roles.SaveAsync(role);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);
            var roleLink = await relationships.AddUserRoleAsync(
                "tenant-a", user.Id, role.Id, expectedUserVersion: 1,
                new UserRoleEntity { TenantId = "caller", UserId = "caller", RoleId = "caller" });
            Assert.Equal(EfIdentityWriteStatus.Updated, roleLink.Status);
            var deleteVersion = Assert.IsType<long>(roleLink.Version);
            var claims = await relationships.AddUserClaimsAsync(
                "tenant-a", user.Id, deleteVersion,
                [new UserClaimEntity { TenantId = "caller", UserId = "caller", ClaimType = "type", ClaimValue = "delete" }]);
            deleteVersion = Assert.IsType<long>(claims.Version);
            var login = await relationships.SaveExternalIdentityAsync(
                ExternalEntity("tenant-a", user.Id, "google", "delete-subject"),
                expectedNewOwnerVersion: deleteVersion, expectedLoginVersion: null,
                enforceLoginVersion: false, EfExternalLoginOwnershipPolicy.CreateOrSameOwner,
                returnOwnerResult: true);
            deleteVersion = Assert.IsType<long>(login.Version);
            var token = await relationships.SaveUserTokenAsync(
                "tenant-a", user.Id, deleteVersion,
                new UserTokenEntity { TenantId = "caller", UserId = "caller", LoginProvider = "Identity", Name = "Recovery", Value = "delete-token" });
            deleteVersion = Assert.IsType<long>(token.Version);
            var membership = await relationships.SaveTenantMembershipAsync(
                TenantMembershipEntity("tenant-a", user.Id, "role-delete"),
                expectedMembershipVersion: null, enforceMembershipVersion: false);
            Assert.Equal(EfIdentityWriteStatus.Updated, membership.Status);
            // Membership revision and owner revision are distinct counters; deletion guards the
            // user aggregate, so fetch the post-mutation owner revision for its CAS.
            deleteVersion = await scope.Context.Users.AsNoTracking()
                .Where(row => row.TenantId == user.TenantId && row.UserId == user.Id)
                .Select(row => row.Revision)
                .SingleAsync();

            var coordinator = new EfIdentityAuthorityAggregateCoordinator(scope.Context, scope.Access);
            var result = await coordinator.DeleteUserAsync("tenant-a", user.Id, expectedVersion: deleteVersion);
            Assert.Equal(EfIdentityWriteStatus.Deleted, result.Status);
            Assert.Empty(scope.Context.ChangeTracker.Entries());

            await using var reopened = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            Assert.Null(await reopened.Users.FindAsync("tenant-a", user.Id));
            Assert.NotNull(await reopened.Roles.FindAsync("tenant-a", role.Id));
            Assert.Empty(await reopened.Context.UserRoles.ToListAsync());
            Assert.Empty(await reopened.Context.UserClaims.ToListAsync());
            Assert.Empty(await reopened.Context.ExternalIdentities.ToListAsync());
            Assert.Empty(await reopened.Context.UserTokens.ToListAsync());
            Assert.Empty(await reopened.Context.TenantMemberships.ToListAsync());
            Assert.Empty(await reopened.Context.UserNameReservations.ToListAsync());
            Assert.Empty(await reopened.Context.EmailReservations.ToListAsync());
            Assert.Equal("[]", (await reopened.Context.Roles.SingleAsync()).UserLinkIdsJson);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    /// <summary>
    /// The aggregate delete now takes one read per child type rather than one per child, so this carries several
    /// children in every registry at once: with a single child per registry a batched read and a per-child read are
    /// indistinguishable. Both linked roles must still have their own registry entry removed and their own revision
    /// bumped, and the user's own revision must remain the externally visible fence.
    /// </summary>
    [Fact]
    public async Task Aggregate_delete_removes_several_children_per_registry_in_one_pass()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "user-batch", "Batch Me", "batch@example.test");
            var first = Role("tenant-a", "role-batch-one", "Batch Role One");
            var second = Role("tenant-a", "role-batch-two", "Batch Role Two");
            var users = new EfUserStore(scope.Context, scope.Access, emailUniquenessPolicy: IdentityEmailUniquenessPolicy.Unique);
            await users.SaveAsync(user);
            await scope.Roles.SaveAsync(first);
            await scope.Roles.SaveAsync(second);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);

            var version = 1L;
            foreach (var role in new[] { first, second })
                version = Assert.IsType<long>((await relationships.AddUserRoleAsync(
                    "tenant-a", user.Id, role.Id, version,
                    new UserRoleEntity { TenantId = "caller", UserId = "caller", RoleId = "caller" })).Version);
            version = Assert.IsType<long>((await relationships.AddUserClaimsAsync(
                "tenant-a", user.Id, version,
                [
                    new UserClaimEntity { TenantId = "caller", UserId = "caller", ClaimType = "type", ClaimValue = "one" },
                    new UserClaimEntity { TenantId = "caller", UserId = "caller", ClaimType = "type", ClaimValue = "two" },
                    new UserClaimEntity { TenantId = "caller", UserId = "caller", ClaimType = "other", ClaimValue = "three" }
                ])).Version);
            foreach (var subject in new[] { "batch-subject-one", "batch-subject-two" })
                version = Assert.IsType<long>((await relationships.SaveExternalIdentityAsync(
                    ExternalEntity("tenant-a", user.Id, "google", subject),
                    expectedNewOwnerVersion: version, expectedLoginVersion: null,
                    enforceLoginVersion: false, EfExternalLoginOwnershipPolicy.CreateOrSameOwner,
                    returnOwnerResult: true)).Version);
            foreach (var name in new[] { "Recovery", "Refresh" })
                version = Assert.IsType<long>((await relationships.SaveUserTokenAsync(
                    "tenant-a", user.Id, version,
                    new UserTokenEntity { TenantId = "caller", UserId = "caller", LoginProvider = "Identity", Name = name, Value = "value-" + name })).Version);
            Assert.Equal(EfIdentityWriteStatus.Updated, (await relationships.SaveTenantMembershipAsync(
                TenantMembershipEntity("tenant-a", user.Id, first.Id),
                expectedMembershipVersion: null, enforceMembershipVersion: false)).Status);

            var registries = await scope.Context.Users.AsNoTracking()
                .Where(row => row.TenantId == user.TenantId && row.UserId == user.Id)
                .Select(row => new { row.Revision, row.ClaimIdsJson, row.LoginIdsJson, row.TokenIdsJson, row.RoleLinkIdsJson })
                .SingleAsync();
            // Guards the test itself: with one child per registry the batching is untested.
            Assert.Equal((3, 2, 2, 2), (
                Count(registries.ClaimIdsJson), Count(registries.LoginIdsJson),
                Count(registries.TokenIdsJson), Count(registries.RoleLinkIdsJson)));
            var rolesBefore = await scope.Context.Roles.AsNoTracking()
                .OrderBy(row => row.RoleId).Select(row => row.Revision).ToArrayAsync();

            var coordinator = new EfIdentityAuthorityAggregateCoordinator(scope.Context, scope.Access);
            var result = await coordinator.DeleteUserAsync("tenant-a", user.Id, registries.Revision);

            Assert.Equal(EfIdentityWriteStatus.Deleted, result.Status);
            Assert.Empty(scope.Context.ChangeTracker.Entries());
            await using var reopened = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            Assert.Null(await reopened.Users.FindAsync("tenant-a", user.Id));
            Assert.Equal((0, 0, 0, 0, 0), (
                await reopened.Context.UserClaims.CountAsync(),
                await reopened.Context.ExternalIdentities.CountAsync(),
                await reopened.Context.UserTokens.CountAsync(),
                await reopened.Context.TenantMemberships.CountAsync(),
                await reopened.Context.UserRoles.CountAsync()));
            var rolesAfter = await reopened.Context.Roles.AsNoTracking()
                .OrderBy(row => row.RoleId).Select(row => new { row.Revision, row.UserLinkIdsJson }).ToArrayAsync();
            Assert.Equal(["[]", "[]"], rolesAfter.Select(row => row.UserLinkIdsJson));
            Assert.Equal(rolesBefore.Select(revision => revision + 1), rolesAfter.Select(row => row.Revision));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    /// <summary>
    /// A child the registry names but the database no longer holds is the reason this path loads rows instead of
    /// deleting by predicate: the failure has to name the child. Batching the read must not turn it into a silent
    /// delete miss.
    /// </summary>
    [Fact]
    public async Task Aggregate_delete_still_names_a_registered_child_that_no_longer_exists()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "user-missing-child", "Missing Child", "missing@example.test");
            var users = new EfUserStore(scope.Context, scope.Access, emailUniquenessPolicy: IdentityEmailUniquenessPolicy.Unique);
            await users.SaveAsync(user);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);
            var claims = await relationships.AddUserClaimsAsync(
                "tenant-a", user.Id, expectedUserVersion: 1,
                [
                    new UserClaimEntity { TenantId = "caller", UserId = "caller", ClaimType = "type", ClaimValue = "kept" },
                    new UserClaimEntity { TenantId = "caller", UserId = "caller", ClaimType = "type", ClaimValue = "vanished" }
                ]);
            var version = Assert.IsType<long>(claims.Version);

            // Delete one claim row behind the coordinator's back, leaving the user's registry naming it.
            var vanished = await scope.Context.UserClaims.SingleAsync(row => row.ClaimValue == "vanished");
            scope.Context.UserClaims.Remove(vanished);
            await scope.Context.SaveChangesAsync();
            scope.Context.ChangeTracker.Clear();

            var coordinator = new EfIdentityAuthorityAggregateCoordinator(scope.Context, scope.Access);
            var failure = await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(
                () => coordinator.DeleteUserAsync("tenant-a", user.Id, version));

            Assert.Contains($"user claim/{vanished.Id}", failure.Message, StringComparison.Ordinal);
            await using var reopened = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            Assert.NotNull(await reopened.Users.FindAsync("tenant-a", user.Id));
            Assert.Equal(1, await reopened.Context.UserClaims.CountAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Aggregate_delete_rollback_restores_relationships_and_reservations()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "user-rollback-delete", "Rollback", "rollback@example.test");
            var role = Role("tenant-a", "role-rollback-delete", "Rollback Role");
            var users = new EfUserStore(scope.Context, scope.Access, emailUniquenessPolicy: IdentityEmailUniquenessPolicy.Unique);
            await users.SaveAsync(user);
            await scope.Roles.SaveAsync(role);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);
            var roleLink = await relationships.AddUserRoleAsync(
                "tenant-a", user.Id, role.Id, expectedUserVersion: 1,
                new UserRoleEntity { TenantId = "caller", UserId = "caller", RoleId = "caller" });
            Assert.Equal(EfIdentityWriteStatus.Updated, roleLink.Status);
            var deleteVersion = Assert.IsType<long>(roleLink.Version);
            var login = await relationships.SaveExternalIdentityAsync(
                ExternalEntity("tenant-a", user.Id, "google", "rollback-subject"),
                expectedNewOwnerVersion: deleteVersion, expectedLoginVersion: null,
                enforceLoginVersion: false, EfExternalLoginOwnershipPolicy.CreateOrSameOwner,
                returnOwnerResult: true);
            deleteVersion = Assert.IsType<long>(login.Version);

            await using (var transaction = await scope.Context.Database.BeginTransactionAsync())
            {
                var result = await new EfIdentityAuthorityAggregateCoordinator(scope.Context, scope.Access)
                    .DeleteUserAsync("tenant-a", user.Id, expectedVersion: deleteVersion);
                Assert.Equal(EfIdentityWriteStatus.Deleted, result.Status);
                await transaction.RollbackAsync();
            }

            await using var reopened = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            Assert.NotNull(await reopened.Users.FindAsync("tenant-a", user.Id));
            Assert.Single(await reopened.Context.UserRoles.ToListAsync());
            Assert.Single(await reopened.Context.ExternalIdentities.ToListAsync());
            Assert.Single(await reopened.Context.UserNameReservations.ToListAsync());
            Assert.Single(await reopened.Context.EmailReservations.ToListAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task User_revision_contract_rejects_malformed_missing_and_stale_revisions_without_mutation()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "revision-user", "Revision", "revision@example.test");
            var created = await scope.RevisionUsers.SaveWithRevisionAsync(user, null);
            var revision = Assert.IsType<string>(created.Revision);
            Assert.Equal(IamRevisionSaveStatus.Saved, created.Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict,
                (await scope.RevisionUsers.SaveWithRevisionAsync(user with { DisplayName = "malformed" }, "not-a-revision")).Status);
            Assert.Equal(IamRevisionSaveStatus.NotFound,
                (await scope.RevisionUsers.SaveWithRevisionAsync(user with { Id = "missing-user" }, revision)).Status);
            Assert.Equal(IamRevisionSaveStatus.Saved,
                (await scope.RevisionUsers.SaveWithRevisionAsync(user with { DisplayName = "winner" }, revision)).Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict,
                (await scope.RevisionUsers.SaveWithRevisionAsync(user with { DisplayName = "stale" }, revision)).Status);
            Assert.Equal("winner", (await scope.Users.FindAsync(user.TenantId, user.Id))!.DisplayName);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task User_store_retries_transient_sqlite_conflicts_three_times_and_clears_tracking()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using (var schema = CreateContext(databasePath))
                await schema.Database.EnsureCreatedAsync();

            var interceptor = new TransientSaveInterceptor(failures: int.MaxValue);
            await using var context = CreateContext(databasePath, interceptor);
            var store = new EfUserStore(context, new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
            await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                store.SaveAsync(User("tenant-a", "retry-exhausted", "Retry", null)).AsTask());
            Assert.Equal(3, interceptor.Attempts);
            Assert.Empty(context.ChangeTracker.Entries());
            await using var reopened = CreateContext(databasePath);
            Assert.Null(await reopened.Users.AsNoTracking().SingleOrDefaultAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Atomic_relationship_receipt_replay_does_not_double_mutate_user_role_state()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "receipt-user", "Receipt", null) with { RoleIds = Set() };
            var role = Role("tenant-a", "receipt-role", "Receipt Role");
            await scope.Users.SaveAsync(user);
            await scope.Roles.SaveAsync(role);
            var atomic = new EfIdentityAtomicWrite(scope.Context);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(scope.Context, atomic, scope.Access);
            var link = new UserRoleEntity { TenantId = "caller-tenant", UserId = "caller-user", RoleId = "caller-role" };

            var first = await relationships.AddUserRoleAsync("tenant-a", user.Id, role.Id, 1, link);
            var replay = await relationships.AddUserRoleAsync(
                "tenant-a", user.Id, role.Id, 1,
                new UserRoleEntity { TenantId = "caller-tenant", UserId = "caller-user", RoleId = "caller-role" });

            Assert.Equal(EfIdentityWriteStatus.Updated, first.Status);
            Assert.Equal(first, replay);
            var storedUser = await scope.Context.Users.AsNoTracking().SingleAsync();
            Assert.Equal(2, storedUser.Revision);
            Assert.Equal([role.Id], JsonSet(storedUser.RoleIdsJson).OrderBy(id => id));
            Assert.Equal(1, await scope.Context.UserRoles.CountAsync());
            Assert.Equal(role.Id, (await scope.Context.UserRoles.AsNoTracking().SingleAsync()).RoleId);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task User_role_registry_replaces_and_removes_case_variant_role_ids()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "case-role-user", "Case Role User", null) with { RoleIds = Set() };
            var role = Role("tenant-a", "Role-Case", "Case Role");
            await scope.Users.SaveAsync(user);
            await scope.Roles.SaveAsync(role);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context,
                new EfIdentityAtomicWrite(scope.Context),
                scope.Access);

            var added = await relationships.AddUserRoleAsync(
                "tenant-a",
                user.Id,
                role.Id,
                expectedUserVersion: 1,
                new UserRoleEntity());
            var replaced = await relationships.AddUserRoleAsync(
                "tenant-a",
                user.Id,
                role.Id.ToLowerInvariant(),
                expectedUserVersion: Assert.IsType<long>(added.Version),
                new UserRoleEntity());
            var afterReplace = await scope.Context.Users.AsNoTracking().SingleAsync();
            Assert.Equal([role.Id.ToLowerInvariant()], JsonSet(afterReplace.RoleIdsJson));

            var removed = await relationships.DeleteUserRoleAsync(
                "tenant-a",
                user.Id,
                role.Id.ToUpperInvariant(),
                expectedUserVersion: Assert.IsType<long>(replaced.Version));

            Assert.Equal(EfIdentityWriteStatus.Updated, removed.Status);
            Assert.Empty(JsonSet((await scope.Context.Users.AsNoTracking().SingleAsync()).RoleIdsJson));
            Assert.Empty(await scope.Context.UserRoles.AsNoTracking().ToListAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public void Atomic_mutation_factory_separates_invocations_and_replays_explicit_tenant_scoped_keys()
    {
        var first = EfIdentityAtomicMutation.Create("save-authority", "same-payload", "tenant-a");
        var second = EfIdentityAtomicMutation.Create("save-authority", "same-payload", "tenant-a");
        Assert.NotEqual(first.OperationId, second.OperationId);
        Assert.NotEqual(first.MutationReceiptId, second.MutationReceiptId);

        var replay = EfIdentityAtomicMutation.Create("save-authority", "same-payload", "tenant-a", "request-1");
        var sameReplay = EfIdentityAtomicMutation.Create("save-authority", "same-payload", "tenant-a", "request-1");
        Assert.Equal(replay.OperationId, sameReplay.OperationId);
        Assert.Equal(replay.MutationReceiptId, sameReplay.MutationReceiptId);

        var otherTenant = EfIdentityAtomicMutation.Create("save-authority", "same-payload", "tenant-b", "request-1");
        Assert.NotEqual(replay.OperationId, otherTenant.OperationId);
        Assert.Throws<ArgumentException>(() =>
            EfIdentityAtomicMutation.Create("save-authority", "same-payload", "tenant-a", " "));
    }

    [Fact]
    public async Task Concurrent_identical_atomic_mutations_replay_one_committed_receipt()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using (var schema = CreateContext(databasePath))
                await schema.Database.EnsureCreatedAsync();

            var mutation = EfIdentityAtomicMutation.Create("concurrent-receipt", "same-request");
            var stageCalls = 0;

            async Task<EfIdentityWriteResult> StageAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref stageCalls);
                // Keep the first transaction open long enough for the contender to complete its
                // preflight receipt read before SQLite serializes the second write transaction.
                await Task.Delay(100, cancellationToken);
                return new EfIdentityWriteResult(EfIdentityWriteStatus.Updated, 7, "committed", "authority-a");
            }

            await using var firstContext = CreateContext(databasePath);
            await using var secondContext = CreateContext(databasePath);
            var first = new EfIdentityAtomicWrite(firstContext, reconciliationTimeout: TimeSpan.FromSeconds(2));
            var second = new EfIdentityAtomicWrite(secondContext, reconciliationTimeout: TimeSpan.FromSeconds(2));

            var results = await Task.WhenAll(
                first.ExecuteAsync(mutation, StageAsync).AsTask(),
                second.ExecuteAsync(mutation, StageAsync).AsTask());

            Assert.All(results, result => Assert.Equal(
                new EfIdentityWriteResult(EfIdentityWriteStatus.Updated, 7, "committed", "authority-a"),
                result));
            Assert.InRange(stageCalls, 1, 5); // A preflight replay may avoid staging the contender.

            await using var reopened = CreateContext(databasePath);
            Assert.Equal(1, await reopened.MutationReceipts.CountAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Theory]
    [InlineData(int.MaxValue, 1)]
    [InlineData((int)EfIdentityWriteStatus.Updated, 0)]
    public async Task Corrupted_mutation_receipts_fail_closed_before_domain_staging(int status, long version)
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.EnsureCreatedAsync();
            var mutation = EfIdentityAtomicMutation.Create("corrupted-receipt", $"request-{status}-{version}");
            context.MutationReceipts.Add(new MutationReceiptEntity
            {
                Id = mutation.MutationReceiptId,
                MutationReceiptId = mutation.MutationReceiptId,
                OperationId = mutation.OperationId,
                RequestFingerprint = mutation.RequestFingerprint,
                Status = status,
                Version = version,
                Message = "persisted",
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                Revision = 1
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var staged = false;
            var atomic = new EfIdentityAtomicWrite(context);
            await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                atomic.ExecuteAsync(
                    mutation,
                    _ =>
                    {
                        staged = true;
                        return Task.FromResult(new EfIdentityWriteResult(EfIdentityWriteStatus.Updated, 2));
                    }).AsTask());
            Assert.False(staged);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Theory]
    [InlineData("MutationReceiptId")]
    [InlineData("OperationId")]
    [InlineData("RequestFingerprint")]
    public async Task Mutation_receipt_identity_corruption_fails_closed_before_domain_staging(string corruptedProperty)
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.EnsureCreatedAsync();
            var mutation = EfIdentityAtomicMutation.Create("identity-corrupted-receipt", $"request-{corruptedProperty}");
            var receipt = new MutationReceiptEntity
            {
                Id = mutation.MutationReceiptId,
                MutationReceiptId = mutation.MutationReceiptId,
                OperationId = mutation.OperationId,
                RequestFingerprint = mutation.RequestFingerprint,
                Status = (int)EfIdentityWriteStatus.Updated,
                Version = 1,
                Message = "persisted",
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                Revision = 1
            };
            switch (corruptedProperty)
            {
                case "MutationReceiptId":
                    receipt.MutationReceiptId = "corrupt-receipt";
                    break;
                case "OperationId":
                    receipt.OperationId = "corrupt-operation";
                    break;
                case "RequestFingerprint":
                    receipt.RequestFingerprint = "corrupt-fingerprint";
                    break;
            }
            context.MutationReceipts.Add(receipt);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var staged = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new EfIdentityAtomicWrite(context).ExecuteAsync(
                    mutation,
                    _ =>
                    {
                        staged = true;
                        return Task.FromResult(new EfIdentityWriteResult(EfIdentityWriteStatus.Updated, 2));
                    }).AsTask());

            Assert.False(staged);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Expired_receipt_identity_corruption_fails_closed_before_cleanup_or_domain_staging()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.EnsureCreatedAsync();
            var mutation = EfIdentityAtomicMutation.Create("expired-corrupted-receipt", "request");
            context.MutationReceipts.Add(new MutationReceiptEntity
            {
                Id = mutation.MutationReceiptId,
                MutationReceiptId = "corrupt-receipt",
                OperationId = mutation.OperationId,
                RequestFingerprint = mutation.RequestFingerprint,
                Status = (int)EfIdentityWriteStatus.Updated,
                Version = 1,
                CreatedAt = DateTimeOffset.UnixEpoch,
                ExpiresAt = DateTimeOffset.UnixEpoch,
                Revision = 1
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var staged = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new EfIdentityAtomicWrite(context).ExecuteAsync(
                    mutation,
                    _ =>
                    {
                        staged = true;
                        return Task.FromResult(new EfIdentityWriteResult(EfIdentityWriteStatus.Updated, 2));
                    }).AsTask());

            Assert.False(staged);
            Assert.Equal("corrupt-receipt", (await context.MutationReceipts.AsNoTracking().SingleAsync()).MutationReceiptId);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Expired_receipts_are_reclaimed_and_cleanup_deletes_at_most_64_oldest_rows()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.EnsureCreatedAsync();
            var atomic = new EfIdentityAtomicWrite(context);
            var mutation = EfIdentityAtomicMutation.Create("receipt-reclaim", "request-1");
            var stageCalls = 0;
            var first = await atomic.ExecuteAsync(
                mutation,
                _ =>
                {
                    stageCalls++;
                    return Task.FromResult(new EfIdentityWriteResult(EfIdentityWriteStatus.Updated, 1, "done", "authority-1"));
                });
            var replay = await atomic.ExecuteAsync(
                mutation,
                _ =>
                {
                    stageCalls++;
                    return Task.FromResult(new EfIdentityWriteResult(EfIdentityWriteStatus.Updated, 2, "must-not-run", "authority-2"));
                });
            Assert.Equal(first, replay);
            Assert.Equal(1, stageCalls);

            var receipt = await context.MutationReceipts.SingleAsync(x => x.Id == mutation.MutationReceiptId);
            receipt.ExpiresAt = DateTimeOffset.UnixEpoch;
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var reclaimed = await atomic.ExecuteAsync(
                mutation,
                _ =>
                {
                    stageCalls++;
                    return Task.FromResult(new EfIdentityWriteResult(EfIdentityWriteStatus.Updated, 3, "reclaimed", "authority-3"));
                });
            Assert.Equal(EfIdentityWriteStatus.Updated, reclaimed.Status);
            Assert.Equal(2, stageCalls);
            Assert.Equal(3, (await context.MutationReceipts.SingleAsync(x => x.Id == mutation.MutationReceiptId)).Version);

            var expiredReceipts = Enumerable.Range(0, 70).Select(index => MutationReceiptEntity(
                $"expired-{index:D3}", $"operation-{index:D3}")).ToArray();
            foreach (var expiredReceipt in expiredReceipts)
                expiredReceipt.ExpiresAt = DateTimeOffset.UnixEpoch;
            context.MutationReceipts.AddRange(expiredReceipts);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var deletedFirstBatch = await atomic.CleanupExpiredAsync();
            var deletedSecondBatch = await atomic.CleanupExpiredAsync();
            Assert.Equal(64, deletedFirstBatch);
            Assert.Equal(6, deletedSecondBatch);
            Assert.Equal(1, await context.MutationReceipts.CountAsync(x => x.Id == mutation.MutationReceiptId));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task User_role_add_preserves_multiple_roles_and_claim_replace_generates_ids_for_caller_rows()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "relationship-user", "Relations", null) with { RoleIds = Set() };
            var firstRole = Role("tenant-a", "role-one", "One");
            var secondRole = Role("tenant-a", "role-two", "Two");
            await scope.Users.SaveAsync(user);
            await scope.Roles.SaveAsync(firstRole);
            await scope.Roles.SaveAsync(secondRole);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);

            var first = await relationships.AddUserRoleAsync(
                "tenant-a", user.Id, firstRole.Id, 1,
                new UserRoleEntity { TenantId = "caller", UserId = "caller", RoleId = "caller" });
            var second = await relationships.AddUserRoleAsync(
                "tenant-a", user.Id, secondRole.Id, Assert.IsType<long>(first.Version),
                new UserRoleEntity { TenantId = "caller", UserId = "caller", RoleId = "caller" });
            Assert.Equal(EfIdentityWriteStatus.Updated, first.Status);
            Assert.Equal(EfIdentityWriteStatus.Updated, second.Status);

            var storedUser = await scope.Context.Users.AsNoTracking().SingleAsync();
            Assert.Equal([firstRole.Id, secondRole.Id], JsonSet(storedUser.RoleIdsJson).OrderBy(id => id));
            Assert.Equal(2, await scope.Context.UserRoles.CountAsync());
            Assert.All(await scope.Context.UserRoles.AsNoTracking().ToListAsync(), link => Assert.False(string.IsNullOrEmpty(link.Id)));

            var claim = new UserClaimEntity { TenantId = "caller", UserId = "caller", ClaimType = "department", ClaimValue = "operations" };
            var claimResult = await relationships.AddUserClaimsAsync("tenant-a", user.Id, Assert.IsType<long>(second.Version), [claim]);
            Assert.Equal(EfIdentityWriteStatus.Updated, claimResult.Status);
            var replacement = new UserClaimEntity { TenantId = "caller", UserId = "caller", ClaimType = "department", ClaimValue = "finance" };
            var replacementResult = await relationships.ReplaceUserClaimAsync(
                "tenant-a", user.Id, Assert.IsType<long>(claimResult.Version), "department", "operations", replacement);
            Assert.Equal(EfIdentityWriteStatus.Updated, replacementResult.Status);
            var storedClaims = await scope.Context.UserClaims.AsNoTracking().ToListAsync();
            Assert.Single(storedClaims);
            Assert.Equal("finance", storedClaims[0].ClaimValue);
            Assert.False(string.IsNullOrEmpty(storedClaims[0].Id));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Authority_update_fails_closed_when_the_persisted_root_identity_is_corrupt()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "corrupt-root-user", "Original", null) with { RoleIds = Set() };
            await scope.Users.SaveAsync(user);
            var persisted = await scope.Context.Users.SingleAsync();
            persisted.TenantLookupKey = "corrupt-tenant-lookup";
            await scope.Context.SaveChangesAsync();
            scope.Context.ChangeTracker.Clear();
            var receiptCount = await scope.Context.MutationReceipts.CountAsync();

            var coordinator = new EfIdentityAuthorityAggregateCoordinator(scope.Context, scope.Access);
            await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() => coordinator.SaveUserAsync(
                user with { DisplayName = "Must not persist" },
                expectedVersion: 1,
                requireUniqueEmail: false));

            var unchanged = await scope.Context.Users.AsNoTracking().SingleAsync();
            Assert.Equal("Original Example", unchanged.DisplayName);
            Assert.Equal("corrupt-tenant-lookup", unchanged.TenantLookupKey);
            Assert.Equal(1, unchanged.Revision);
            Assert.Equal(receiptCount, await scope.Context.MutationReceipts.CountAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Theory]
    [InlineData("UserName")]
    [InlineData("Email")]
    [InlineData("RoleName")]
    public async Task Authority_update_fails_closed_when_a_persisted_reservation_identity_is_corrupt(string reservationKind)
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var coordinator = new EfIdentityAuthorityAggregateCoordinator(scope.Context, scope.Access);
            if (reservationKind == "RoleName")
            {
                var role = Role("tenant-a", "corrupt-reservation-role", "Reserved Role");
                await scope.Roles.SaveAsync(role);
                var reservation = await scope.Context.RoleNameReservations.SingleAsync();
                reservation.TenantLookupKey = "corrupt-tenant-lookup";
                await scope.Context.SaveChangesAsync();
                scope.Context.ChangeTracker.Clear();

                await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() => coordinator.SaveRoleAsync(
                    role with { Description = "Must not persist" },
                    expectedVersion: 1));
                Assert.Equal(1, (await scope.Context.Roles.AsNoTracking().SingleAsync()).Revision);
                return;
            }

            var user = User("tenant-a", "corrupt-reservation-user", "Reserved User", "reserved@example.test") with { RoleIds = Set() };
            await new EfUserStore(scope.Context, scope.Access, emailUniquenessPolicy: IdentityEmailUniquenessPolicy.Unique).SaveAsync(user);
            if (reservationKind == "UserName")
                (await scope.Context.UserNameReservations.SingleAsync()).TenantLookupKey = "corrupt-tenant-lookup";
            else
                (await scope.Context.EmailReservations.SingleAsync()).TenantLookupKey = "corrupt-tenant-lookup";
            await scope.Context.SaveChangesAsync();
            scope.Context.ChangeTracker.Clear();

            await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() => coordinator.SaveUserAsync(
                user with { DisplayName = "Must not persist" },
                expectedVersion: 1,
                requireUniqueEmail: true));
            Assert.Equal(1, (await scope.Context.Users.AsNoTracking().SingleAsync()).Revision);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Relationship_update_fails_closed_when_the_persisted_child_identity_is_corrupt()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "corrupt-child-user", "Child", null) with { RoleIds = Set() };
            await scope.Users.SaveAsync(user);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);
            var added = await relationships.AddUserClaimsAsync(
                user.TenantId,
                user.Id,
                expectedUserVersion: 1,
                [new UserClaimEntity { ClaimType = "department", ClaimValue = "operations" }]);
            Assert.Equal(EfIdentityWriteStatus.Updated, added.Status);

            var persisted = await scope.Context.UserClaims.SingleAsync();
            persisted.UserLookupKey = "corrupt-owner-lookup";
            await scope.Context.SaveChangesAsync();
            scope.Context.ChangeTracker.Clear();
            var receiptCount = await scope.Context.MutationReceipts.CountAsync();

            await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() => relationships.ReplaceUserClaimAsync(
                user.TenantId,
                user.Id,
                Assert.IsType<long>(added.Version),
                oldClaimType: "department",
                oldClaimValue: "operations",
                replacement: new UserClaimEntity { ClaimType = "department", ClaimValue = "finance" }));

            var unchangedClaim = await scope.Context.UserClaims.AsNoTracking().SingleAsync();
            var unchangedUser = await scope.Context.Users.AsNoTracking().SingleAsync();
            Assert.Equal("operations", unchangedClaim.ClaimValue);
            Assert.Equal("corrupt-owner-lookup", unchangedClaim.UserLookupKey);
            Assert.Equal(2, unchangedUser.Revision);
            Assert.Equal(receiptCount, await scope.Context.MutationReceipts.CountAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Replacing_a_user_claim_with_the_same_claim_preserves_the_child_and_registry()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "same-claim-user", "Same Claim", null) with { RoleIds = Set() };
            await scope.Users.SaveAsync(user);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);

            var added = await relationships.AddUserClaimsAsync(
                user.TenantId,
                user.Id,
                expectedUserVersion: 1,
                [new UserClaimEntity { TenantId = "caller", UserId = "caller", ClaimType = "department", ClaimValue = "operations" }]);
            Assert.Equal(EfIdentityWriteStatus.Updated, added.Status);

            var replaced = await relationships.ReplaceUserClaimAsync(
                user.TenantId,
                user.Id,
                Assert.IsType<long>(added.Version),
                oldClaimType: "department",
                oldClaimValue: "operations",
                replacement: new UserClaimEntity
                {
                    TenantId = "caller",
                    UserId = "caller",
                    ClaimType = "department",
                    ClaimValue = "operations"
                });

            Assert.Equal(EfIdentityWriteStatus.Updated, replaced.Status);
            var persistedClaim = Assert.Single(await scope.Context.UserClaims.AsNoTracking().ToListAsync());
            var persistedUser = await scope.Context.Users.AsNoTracking().SingleAsync();
            Assert.Equal("operations", persistedClaim.ClaimValue);
            Assert.Equal([persistedClaim.Id], JsonSet(persistedUser.ClaimIdsJson));
            Assert.Equal(3, persistedUser.Revision);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Duplicate_user_claim_input_is_coalesced_before_staging()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "duplicate-claim-user", "Duplicate Claim", null) with { RoleIds = Set() };
            await scope.Users.SaveAsync(user);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);
            var duplicate = new[]
            {
                new UserClaimEntity { TenantId = "caller-a", UserId = "caller-a", ClaimType = "department", ClaimValue = "operations" },
                new UserClaimEntity { TenantId = "caller-b", UserId = "caller-b", ClaimType = "department", ClaimValue = "operations" }
            };

            var result = await relationships.AddUserClaimsAsync(user.TenantId, user.Id, 1, duplicate);

            Assert.Equal(EfIdentityWriteStatus.Updated, result.Status);
            Assert.Single(await scope.Context.UserClaims.AsNoTracking().ToListAsync());
            var persistedUser = await scope.Context.Users.AsNoTracking().SingleAsync();
            Assert.Single(JsonSet(persistedUser.ClaimIdsJson));
            Assert.Equal(2, persistedUser.Revision);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task External_identity_same_owner_case_variants_update_the_owner_once()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "User-Case", "Case Owner", null) with { RoleIds = Set() };
            await scope.Users.SaveAsync(user);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);
            var first = await relationships.SaveExternalIdentityAsync(
                new ExternalIdentityEntity
                {
                    TenantId = user.TenantId,
                    UserId = user.Id,
                    Provider = "google",
                    ProviderSubject = "case-subject",
                    LinkedAt = DateTimeOffset.UnixEpoch,
                    LinkPolicy = (int)ExternalIdentityLinkPolicy.Auto
                },
                expectedNewOwnerVersion: 1,
                expectedLoginVersion: null,
                enforceLoginVersion: false,
                EfExternalLoginOwnershipPolicy.CreateOrSameOwner,
                returnOwnerResult: true);
            Assert.Equal(EfIdentityWriteStatus.Updated, first.Status);

            var second = await relationships.SaveExternalIdentityAsync(
                new ExternalIdentityEntity
                {
                    TenantId = user.TenantId,
                    UserId = "user-case",
                    Provider = "google",
                    ProviderSubject = "case-subject",
                    LinkedAt = DateTimeOffset.UnixEpoch,
                    LinkPolicy = (int)ExternalIdentityLinkPolicy.Auto
                },
                expectedNewOwnerVersion: Assert.IsType<long>(first.Version),
                expectedLoginVersion: null,
                enforceLoginVersion: false,
                EfExternalLoginOwnershipPolicy.CreateOrSameOwner,
                returnOwnerResult: true);

            Assert.Equal(EfIdentityWriteStatus.Updated, second.Status);
            Assert.Equal(3, second.Version);
            var persistedUser = await scope.Context.Users.AsNoTracking().SingleAsync();
            Assert.Equal(3, persistedUser.Revision);
            Assert.Single(JsonSet(persistedUser.LoginIdsJson));
            Assert.Equal("user-case", (await scope.Context.ExternalIdentities.AsNoTracking().SingleAsync()).UserId);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task User_delete_fails_closed_when_a_registered_claim_is_missing()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "missing-claim-user", "Missing Claim", null) with { RoleIds = Set() };
            await scope.Users.SaveAsync(user);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);
            var added = await relationships.AddUserClaimsAsync(
                user.TenantId,
                user.Id,
                expectedUserVersion: 1,
                [new UserClaimEntity { TenantId = user.TenantId, UserId = user.Id, ClaimType = "registered", ClaimValue = "child" }]);
            Assert.Equal(EfIdentityWriteStatus.Updated, added.Status);
            var claim = await scope.Context.UserClaims.SingleAsync();
            scope.Context.UserClaims.Remove(claim);
            await scope.Context.SaveChangesAsync();
            scope.Context.ChangeTracker.Clear();

            var coordinator = new EfIdentityAuthorityAggregateCoordinator(scope.Context, scope.Access);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => coordinator.DeleteUserAsync(
                user.TenantId,
                user.Id,
                Assert.IsType<long>(added.Version)));

            Assert.NotNull(await scope.Context.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Role_delete_fails_closed_when_a_registered_claim_is_missing()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var role = Role("tenant-a", "missing-role-claim", "Missing Role Claim");
            await scope.Roles.SaveAsync(role);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);
            var added = await relationships.SaveRoleClaimAsync(
                role.TenantId,
                role.Id,
                expectedRoleVersion: 1,
                new RoleClaimEntity { TenantId = role.TenantId, RoleId = role.Id, ClaimType = "registered", ClaimValue = "child" });
            Assert.Equal(EfIdentityWriteStatus.Updated, added.Status);
            var claim = await scope.Context.RoleClaims.SingleAsync();
            scope.Context.RoleClaims.Remove(claim);
            await scope.Context.SaveChangesAsync();
            scope.Context.ChangeTracker.Clear();

            var coordinator = new EfIdentityAuthorityAggregateCoordinator(scope.Context, scope.Access);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => coordinator.DeleteRoleAsync(
                role.TenantId,
                role.Id,
                Assert.IsType<long>(added.Version)));

            Assert.NotNull(await scope.Context.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.RoleId == role.Id));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Aggregate_delete_rejects_an_over_limit_registered_relationship_registry()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "over-limit-delete-user", "Over Limit", null) with { RoleIds = Set() };
            await scope.Users.SaveAsync(user);
            var persisted = await scope.Context.Users.SingleAsync();
            persisted.ClaimIdsJson = PersistedSet(Enumerable.Range(0, 513).Select(index => $"claim-{index:D3}").ToArray());
            await scope.Context.SaveChangesAsync();
            scope.Context.ChangeTracker.Clear();

            var coordinator = new EfIdentityAuthorityAggregateCoordinator(scope.Context, scope.Access);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => coordinator.DeleteUserAsync(
                user.TenantId,
                user.Id,
                expectedVersion: 1));

            var unchanged = await scope.Context.Users.AsNoTracking().SingleAsync();
            Assert.Equal(1, unchanged.Revision);
            Assert.Equal(513, JsonSet(unchanged.ClaimIdsJson).Count);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Role_claim_add_replace_and_delete_updates_the_role_registry_atomically()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var role = Role("tenant-a", "role-claims", "Claimed");
            await scope.Roles.SaveAsync(role);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);

            var added = await relationships.SaveRoleClaimAsync(
                role.TenantId,
                role.Id,
                expectedRoleVersion: 1,
                new RoleClaimEntity
                {
                    TenantId = "caller",
                    RoleId = "caller",
                    ClaimType = "permission",
                    ClaimValue = "identity.users.read"
                });
            Assert.Equal(EfIdentityWriteStatus.Updated, added.Status);

            var replaced = await relationships.ReplaceRoleClaimAsync(
                role.TenantId,
                role.Id,
                expectedRoleVersion: 2,
                oldClaimType: "permission",
                oldClaimValue: "identity.users.read",
                replacement: new RoleClaimEntity
                {
                    TenantId = "caller",
                    RoleId = "caller",
                    ClaimType = "permission",
                    ClaimValue = "identity.users.write"
                });
            Assert.Equal(EfIdentityWriteStatus.Updated, replaced.Status);

            var deleted = await relationships.DeleteRoleClaimAsync(
                role.TenantId,
                role.Id,
                expectedRoleVersion: 3,
                new RoleClaimEntity
                {
                    TenantId = "caller",
                    RoleId = "caller",
                    ClaimType = "permission",
                    ClaimValue = "identity.users.write"
                });
            Assert.Equal(EfIdentityWriteStatus.Updated, deleted.Status);

            var persistedRole = await scope.Context.Roles.AsNoTracking().SingleAsync();
            Assert.Equal(4, persistedRole.Revision);
            Assert.Equal([], JsonSet(persistedRole.ClaimIdsJson));
            Assert.Empty(await scope.Context.RoleClaims.AsNoTracking().ToListAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Recovery_code_redemption_is_one_time_and_updates_the_user_revision_atomically()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "recovery-user", "Recovery", null) with { RoleIds = Set() };
            await scope.Users.SaveAsync(user);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);

            var token = new UserTokenEntity
            {
                TenantId = "caller",
                UserId = "caller",
                LoginProvider = "Identity",
                Name = "RecoveryCodes",
                Value = "code-a;code-b"
            };
            var saved = await relationships.SaveUserTokenAsync("tenant-a", user.Id, 1, token);
            Assert.Equal(EfIdentityWriteStatus.Updated, saved.Status);
            var redeemed = await relationships.RedeemRecoveryCodeAsync(
                "tenant-a", user.Id, Assert.IsType<long>(saved.Version), "Identity", "RecoveryCodes", "code-a");
            Assert.Equal(EfIdentityWriteStatus.Updated, redeemed.Status);
            Assert.Equal(3, (await scope.Context.Users.AsNoTracking().SingleAsync()).Revision);
            Assert.Equal("code-b", (await scope.Context.UserTokens.AsNoTracking().SingleAsync()).Value);

            var replay = await relationships.RedeemRecoveryCodeAsync(
                "tenant-a", user.Id, Assert.IsType<long>(redeemed.Version), "Identity", "RecoveryCodes", "code-a");
            Assert.Equal(EfIdentityWriteStatus.NotFound, replay.Status);
            Assert.Equal(3, (await scope.Context.Users.AsNoTracking().SingleAsync()).Revision);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Recovery_code_contenders_with_the_same_original_revision_do_not_replay_a_success()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "recovery-contender", "Recovery", null);
            await scope.Users.SaveAsync(user);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);
            var saved = await relationships.SaveUserTokenAsync(
                user.TenantId,
                user.Id,
                expectedUserVersion: 1,
                new UserTokenEntity
                {
                    TenantId = "caller",
                    UserId = "caller",
                    LoginProvider = "Identity",
                    Name = "RecoveryCodes",
                    Value = "one-time-code"
                });
            Assert.Equal(EfIdentityWriteStatus.Updated, saved.Status);

            var contenders = new[]
            {
                await relationships.RedeemRecoveryCodeAsync(
                    user.TenantId, user.Id, expectedUserVersion: 2,
                    "Identity", "RecoveryCodes", "one-time-code"),
                await relationships.RedeemRecoveryCodeAsync(
                    user.TenantId, user.Id, expectedUserVersion: 2,
                    "Identity", "RecoveryCodes", "one-time-code")
            };

            Assert.Single(contenders, result => result.Status == EfIdentityWriteStatus.Updated);
            Assert.Single(contenders, result => result.Status is EfIdentityWriteStatus.Conflict or EfIdentityWriteStatus.NotFound);
            Assert.Equal(3, (await scope.Context.Users.AsNoTracking().SingleAsync()).Revision);
            Assert.Equal("", (await scope.Context.UserTokens.AsNoTracking().SingleAsync()).Value);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Composite_relationship_keys_do_not_collide_when_parts_contain_the_separator()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var firstUser = User("tenant-a", "user", "First", null) with { RoleIds = Set() };
            var secondUser = User("tenant-a", "user\u001frole", "Second", null) with { RoleIds = Set() };
            var firstRole = Role("tenant-a", "role\u001fA", "First Role");
            var secondRole = Role("tenant-a", "A", "Second Role");
            await scope.Users.SaveAsync(firstUser);
            await scope.Users.SaveAsync(secondUser);
            await scope.Roles.SaveAsync(firstRole);
            await scope.Roles.SaveAsync(secondRole);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(
                scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);

            var firstLink = await relationships.AddUserRoleAsync(
                "tenant-a", firstUser.Id, firstRole.Id, 1,
                new UserRoleEntity { TenantId = "caller", UserId = "caller", RoleId = "caller" });
            var secondLink = await relationships.AddUserRoleAsync(
                "tenant-a", secondUser.Id, secondRole.Id, 1,
                new UserRoleEntity { TenantId = "caller", UserId = "caller", RoleId = "caller" });
            Assert.Equal(EfIdentityWriteStatus.Updated, firstLink.Status);
            Assert.Equal(EfIdentityWriteStatus.Updated, secondLink.Status);
            Assert.Equal(2, await scope.Context.UserRoles.CountAsync());

            var firstExternal = new ExternalIdentityEntity
            {
                TenantId = "tenant-a",
                Provider = "google",
                ProviderSubject = "subject\u001fnext",
                UserId = firstUser.Id,
                LinkedAt = DateTimeOffset.UnixEpoch,
                LastSeenAt = DateTimeOffset.UnixEpoch,
                LinkPolicy = (int)ExternalIdentityLinkPolicy.Auto
            };
            var secondExternal = new ExternalIdentityEntity
            {
                TenantId = "tenant-a",
                Provider = "google\u001fsubject",
                ProviderSubject = "next",
                UserId = firstUser.Id,
                LinkedAt = DateTimeOffset.UnixEpoch,
                LastSeenAt = DateTimeOffset.UnixEpoch,
                LinkPolicy = (int)ExternalIdentityLinkPolicy.Auto
            };
            var externalOne = await relationships.SaveExternalIdentityAsync(
                firstExternal, expectedNewOwnerVersion: 2, expectedLoginVersion: null, enforceLoginVersion: false,
                EfExternalLoginOwnershipPolicy.CreateOrSameOwner, returnOwnerResult: false);
            var externalTwo = await relationships.SaveExternalIdentityAsync(
                secondExternal, expectedNewOwnerVersion: 3, expectedLoginVersion: null, enforceLoginVersion: false,
                EfExternalLoginOwnershipPolicy.CreateOrSameOwner, returnOwnerResult: false);
            Assert.Equal(EfIdentityWriteStatus.Updated, externalOne.Status);
            Assert.Equal(EfIdentityWriteStatus.Updated, externalTwo.Status);
            Assert.Equal(2, await scope.Context.ExternalIdentities.CountAsync());

            var firstToken = new UserTokenEntity
            {
                TenantId = "tenant-a",
                UserId = firstUser.Id,
                LoginProvider = "Identity",
                Name = "Recovery\u001fA",
                Value = "one"
            };
            var secondToken = new UserTokenEntity
            {
                TenantId = "tenant-a",
                UserId = firstUser.Id,
                LoginProvider = "Identity\u001fRecovery",
                Name = "A",
                Value = "two"
            };
            var tokenOne = await relationships.SaveUserTokenAsync("tenant-a", firstUser.Id, 4, firstToken);
            var tokenTwo = await relationships.SaveUserTokenAsync("tenant-a", firstUser.Id, 5, secondToken);
            Assert.Equal(EfIdentityWriteStatus.Updated, tokenOne.Status);
            Assert.Equal(EfIdentityWriteStatus.Updated, tokenTwo.Status);
            Assert.Equal(2, await scope.Context.UserTokens.CountAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Relationship_admission_refuses_the_513th_child_without_partial_rows_or_revision_change()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "bounded-user", "Bounded", null) with { RoleIds = Set() };
            await scope.Users.SaveAsync(user);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(scope.Context, new EfIdentityAtomicWrite(scope.Context), scope.Access);
            var claims = Enumerable.Range(0, 513).Select(index => new UserClaimEntity
            {
                TenantId = "caller",
                UserId = "caller",
                ClaimType = "type",
                ClaimValue = $"value-{index:D3}"
            }).ToArray();

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => relationships.AddUserClaimsAsync(
                "tenant-a", user.Id, 1, claims));
            Assert.Empty(await scope.Context.UserClaims.ToListAsync());
            var persisted = await scope.Context.Users.AsNoTracking().SingleAsync();
            Assert.Equal(1, persisted.Revision);
            Assert.Equal([], JsonSet(persisted.ClaimIdsJson));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Membership_and_external_revision_saves_are_create_only_when_revision_is_null()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(databasePath, "tenant-a");
            var user = User("tenant-a", "membership-user", "Membership", null);
            await scope.Users.SaveAsync(user);
            var membership = Membership("tenant-a", user.Id, "role-a");
            var membershipCreated = await scope.RevisionMemberships.SaveWithRevisionAsync(membership, null);
            Assert.Equal(IamRevisionSaveStatus.Saved, membershipCreated.Status);
            var membershipDuplicate = await scope.RevisionMemberships.SaveWithRevisionAsync(
                membership with { Status = TenantMembershipStatus.Suspended }, null);
            Assert.Equal(IamRevisionSaveStatus.Conflict, membershipDuplicate.Status);
            var membershipRevision = Assert.IsType<string>(membershipCreated.Revision);
            var membershipUpdated = await scope.RevisionMemberships.SaveWithRevisionAsync(
                membership with { Status = TenantMembershipStatus.Suspended }, membershipRevision);
            Assert.Equal(IamRevisionSaveStatus.Saved, membershipUpdated.Status);
            Assert.NotEqual(membershipRevision, membershipUpdated.Revision);

            var external = ExternalIdentity("tenant-a", user.Id, "google", "revision-subject");
            var externalCreated = await scope.RevisionExternalIdentities.SaveWithRevisionAsync(external, null);
            Assert.Equal(IamRevisionSaveStatus.Saved, externalCreated.Status);
            var externalDuplicate = await scope.RevisionExternalIdentities.SaveWithRevisionAsync(
                external with { LastSeenAt = DateTimeOffset.UnixEpoch.AddDays(1) }, null);
            Assert.Equal(IamRevisionSaveStatus.Conflict, externalDuplicate.Status);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static UserRecord User(string tenantId, string id, string name, string? email) => new(
        id,
        tenantId,
        name,
        email,
        $"{name} Example",
        UserStatus.Active,
        ResourceOwnership.Foundation,
        Set("role-1"),
        Set("identity.users.read"));

    private static RoleRecord Role(string tenantId, string id, string name) => new(
        id,
        tenantId,
        name,
        "Full access",
        Set("identity.users.read"),
        System: false);

    private static ClaimMappingRule ClaimMapping(string tenantId, string provider, string id, int order) => new(
        id,
        tenantId,
        provider,
        "groups",
        "admins",
        Set("role-1"),
        Set("identity.users.read"),
        order,
        StopOnMatch: true);

    private static TenantMembershipRecord Membership(string tenantId, string userId, string roleId) => new(
        tenantId,
        userId,
        TenantMembershipStatus.Active,
        Set(roleId),
        Set("identity.users.read"));

    private static ExternalIdentityRecord ExternalIdentity(string tenantId, string userId, string provider, string subject) => new(
        tenantId,
        provider,
        subject,
        userId,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        ExternalIdentityLinkPolicy.Auto);

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

    private static IReadOnlySet<string> JsonSet(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<string[]>(json)!
            .Select(DecodePersistedSetValue)
            .ToHashSet(StringComparer.Ordinal);

    private static string PersistedSet(params string[] values) =>
        System.Text.Json.JsonSerializer.Serialize(values
            .Select(value => Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(value)))
            .ToArray());

    private static string DecodePersistedSetValue(string value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length % sizeof(char) != 0)
                return value;
            return System.Text.Encoding.Unicode.GetString(bytes);
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private static void AssertUser(UserRecord expected, UserRecord actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.TenantId, actual.TenantId);
        Assert.Equal(expected.UserName, actual.UserName);
        Assert.Equal(expected.Email, actual.Email);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Ownership, actual.Ownership);
        Assert.Equal(expected.RoleIds.Order(StringComparer.Ordinal), actual.RoleIds.Order(StringComparer.Ordinal));
        Assert.Equal(expected.DirectPermissions.Order(StringComparer.Ordinal), actual.DirectPermissions.Order(StringComparer.Ordinal));
    }

    private static void AssertRole(RoleRecord expected, RoleRecord actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.TenantId, actual.TenantId);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.System, actual.System);
        Assert.Equal(expected.Permissions.Order(StringComparer.Ordinal), actual.Permissions.Order(StringComparer.Ordinal));
    }

    private static int Count(string? json) => System.Text.Json.JsonSerializer.Deserialize<string[]>(json ?? "[]")!.Length;

    private static string TemporaryDatabasePath() =>
        Path.Join(Path.GetTempPath(), $"elsa-identity-authority-{Guid.NewGuid():N}.db");

    private static IdentityIamSqliteDbContext CreateContext(string databasePath, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<IdentityIamSqliteDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=5");
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return new IdentityIamSqliteDbContext(builder.Options);
    }

    private static ExternalIdentityEntity ExternalEntity(string tenantId, string userId, string provider, string subject) => new()
    {
        Id = $"{tenantId}\u001f{provider}\u001f{subject}",
        TenantId = tenantId,
        TenantLookupKey = tenantId,
        Provider = provider,
        ProviderLookupKey = provider,
        ProviderSubject = subject,
        ProviderSubjectLookupKey = subject,
        UserId = userId,
        UserLookupKey = $"{tenantId}\u001f{userId}",
        LinkedAt = DateTimeOffset.UnixEpoch,
        LastSeenAt = DateTimeOffset.UnixEpoch,
        LinkPolicy = (int)ExternalIdentityLinkPolicy.Auto,
        Revision = 1
    };

    private static UserRoleEntity UserRoleEntity(string tenantId, string userId, string roleId, string id) => new()
    {
        Id = id,
        TenantId = tenantId,
        TenantLookupKey = tenantId,
        UserId = userId,
        UserLookupKey = $"{tenantId}\u001f{userId}",
        RoleId = roleId,
        RoleLookupKey = $"{tenantId}\u001f{roleId}",
        Revision = 1
    };

    private static UserClaimEntity UserClaimEntity(string tenantId, string userId, string id) => new()
    {
        Id = id,
        TenantId = tenantId,
        TenantLookupKey = tenantId,
        UserId = userId,
        UserLookupKey = $"{tenantId}\u001f{userId}",
        ClaimType = "type",
        ClaimValue = "value",
        ClaimKey = $"{tenantId}\u001f{userId}\u001ftype",
        Revision = 1
    };

    private static UserTokenEntity UserTokenEntity(string tenantId, string userId, string provider, string name, string value) => new()
    {
        Id = $"{tenantId}\u001f{userId}\u001f{provider}\u001f{name}",
        TenantId = tenantId,
        TenantLookupKey = tenantId,
        UserId = userId,
        UserLookupKey = $"{tenantId}\u001f{userId}",
        LoginProvider = provider,
        Name = name,
        Value = value,
        Revision = 1
    };

    private static TenantMembershipEntity TenantMembershipEntity(string tenantId, string userId, string roleId) => new()
    {
        Id = $"{tenantId}\u001f{userId}",
        TenantId = tenantId,
        TenantLookupKey = tenantId,
        UserId = userId,
        UserLookupKey = $"{tenantId}\u001f{userId}",
        Status = (int)TenantMembershipStatus.Active,
        RoleIdsJson = $"[\"{roleId}\"]",
        DirectPermissionsJson = "[]",
        Revision = 1
    };

    private static MutationReceiptEntity MutationReceiptEntity(string receiptId, string operationId) => new()
    {
        Id = receiptId,
        MutationReceiptId = receiptId,
        OperationId = operationId,
        RequestFingerprint = "fingerprint",
        Status = 1,
        Version = 1,
        Message = "committed",
        CreatedAt = DateTimeOffset.UnixEpoch,
        ExpiresAt = DateTimeOffset.UnixEpoch.AddDays(1),
        Revision = 1
    };

    private static void DeleteDatabaseFiles(string databasePath) => TemporarySqliteDatabase.ClearPoolAndDeleteFiles(databasePath);

    private sealed class EfIdentityScope : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;

        private EfIdentityScope(ServiceProvider provider, AsyncServiceScope scope)
        {
            this.provider = provider;
            this.scope = scope;
            Context = scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>();
            Access = scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>();
            Users = scope.ServiceProvider.GetRequiredService<IUserStore>();
            RevisionUsers = scope.ServiceProvider.GetRequiredService<IRevisionAwareUserStore>();
            Roles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
            RevisionRoles = scope.ServiceProvider.GetRequiredService<IRevisionAwareRoleStore>();
            ClaimMappings = scope.ServiceProvider.GetRequiredService<IClaimMappingStore>();
            RevisionClaimMappings = scope.ServiceProvider.GetRequiredService<IRevisionAwareClaimMappingStore>();
            ExternalIdentities = scope.ServiceProvider.GetRequiredService<IExternalIdentityStore>();
            RevisionExternalIdentities = scope.ServiceProvider.GetRequiredService<IRevisionAwareExternalIdentityStore>();
            Memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
            RevisionMemberships = scope.ServiceProvider.GetRequiredService<IRevisionAwareTenantMembershipStore>();
        }

        public IdentityIamDbContext Context { get; }
        public IPersistenceAccessContextAccessor Access { get; }
        public IUserStore Users { get; }
        public IRevisionAwareUserStore RevisionUsers { get; }
        public IRoleStore Roles { get; }
        public IRevisionAwareRoleStore RevisionRoles { get; }
        public IClaimMappingStore ClaimMappings { get; }
        public IRevisionAwareClaimMappingStore RevisionClaimMappings { get; }
        public IExternalIdentityStore ExternalIdentities { get; }
        public IRevisionAwareExternalIdentityStore RevisionExternalIdentities { get; }
        public ITenantMembershipStore Memberships { get; }
        public IRevisionAwareTenantMembershipStore RevisionMemberships { get; }

        public static async Task<EfIdentityScope> OpenAsync(string databasePath, string tenantId)
        {
            var services = new ServiceCollection();
            services.AddPersistenceCore(tenantId);
            services.AddIdentityIamEntityFrameworkCore(new()
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={databasePath};Default Timeout=5"
            });
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var scope = provider.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>().Database.EnsureCreatedAsync();
                return new EfIdentityScope(provider, scope);
            }
            catch
            {
                await scope.DisposeAsync();
                await provider.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }

    private sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class ReaderCommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TransientSaveInterceptor(int failures) : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts <= failures)
                throw new SqliteException("database is locked", 5);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class NamedUniqueConstraintFailureInterceptor(string constraintName, bool wrapped = false) : SaveChangesInterceptor
    {
        public IReadOnlyList<Type> PendingEntityTypes { get; private set; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context!.ChangeTracker.Entries().ToArray();
            if (!entries.Any(entry => entry.Entity is UserEntity) ||
                !entries.Any(entry => entry.Entity is MutationReceiptEntity))
                return ValueTask.FromResult(result);

            PendingEntityTypes = entries.Select(entry => entry.Entity.GetType()).ToArray();
            var failure = new DbUpdateException(
                $"Violation of unique constraint '{constraintName}'.",
                new SqlException(2627),
                entries);
            // Wrapped, the save failure sits under an InvalidOperationException, which is the shape a store
            // boundary or an execution strategy hands on. Everything that reads the save's own detail, the
            // constraint name and the entries, has to see through it.
            throw wrapped ? ProviderFailures.WrappedByExecutionStrategy(failure) : failure;
        }
    }

    /// <summary>
    /// Fails a claim-mapping insert with the provider error of a competing insert that committed first. SQLite cannot
    /// stage that race for real: it serializes writers, so a second connection waits on or is refused by the atomic
    /// writer's transaction instead of losing on the unique key.
    /// </summary>
    private sealed class LostClaimMappingCreateInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; } = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context!.ChangeTracker.Entries().ToArray();
            if (!Armed || !entries.Any(entry => entry is { Entity: ClaimMappingEntity, State: EntityState.Added }))
                return ValueTask.FromResult(result);

            throw new DbUpdateException(
                "An error occurred while saving the entity changes.",
                new SqliteException($"SQLite Error 19: 'UNIQUE constraint failed: {IdentityIamEfModule.ClaimMappingTableName}.Id'.", 19, 1555),
                entries);
        }
    }

    private sealed class SqlException(int number) : Exception("Synthetic SQL Server uniqueness conflict")
    {
        public int Number { get; } = number;
    }
}
