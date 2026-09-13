using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

public sealed class IdentityIamEntityFrameworkCoreBehaviorTests
{
    [Fact]
    public async Task Application_all_fields_and_arbitrary_utf16_round_trip_after_restart()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var expected = new ApplicationRecord(
                "Application\ud800",
                "Tenant\udc00",
                "Client\ud800",
                "Display 😀\udc00",
                ApplicationType.Confidential,
                ResourceOwnership.External,
                Set("grant\udc00", "authorization_code", ""),
                Set("scope\ud800", "openid", "😀"));

            await using (var context = CreateContext(databasePath))
                await ApplicationStore(context, expected.TenantId).SaveAsync(expected);

            await using var reopened = CreateContext(databasePath);
            var actual = await ApplicationStore(reopened, expected.TenantId)
                .FindAsync(expected.TenantId, "APPLICATION\ud800");

            AssertApplication(expected, Assert.IsType<ApplicationRecord>(actual));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Credential_all_fields_and_raw_utf16_round_trip_with_exact_offset_and_ticks()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var expiry = DateTimeOffset.ParseExact(
                "2027-05-06T07:08:09.1234567-03:30",
                "O",
                System.Globalization.CultureInfo.InvariantCulture);
            var expected = new CredentialRecord(
                "Credential\ud800",
                "Tenant\udc00",
                CredentialSubjectType.Application,
                "Subject\udc00",
                CredentialKind.ClientSecret,
                "hash\ud800😀",
                "Argon2id\udc00",
                CredentialStatus.Rotating,
                expiry);

            await using (var context = CreateContext(databasePath))
                await CredentialStore(context, expected.TenantId).SaveAsync(expected);

            await using var reopened = CreateContext(databasePath);
            var actual = await CredentialStore(reopened, expected.TenantId)
                .FindAsync(expected.TenantId, "CREDENTIAL\ud800");

            AssertCredential(expected, Assert.IsType<CredentialRecord>(actual));
            var actualExpiresAt = Assert.IsType<DateTimeOffset>(actual.ExpiresAt);
            Assert.Equal(expiry.Offset, actualExpiresAt.Offset);
            Assert.Equal(expiry.Ticks, actualExpiresAt.Ticks);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Credential_null_expiry_and_enum_values_round_trip_without_changing_hash_material()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var expected = Credential("acme", "nullable", "hash:unchanged") with
            {
                SubjectType = CredentialSubjectType.User,
                Kind = CredentialKind.ApiKey,
                Status = CredentialStatus.Revoked,
                ExpiresAt = null
            };

            await using (var context = CreateContext(databasePath))
                await CredentialStore(context, expected.TenantId).SaveAsync(expected);

            await using var reopened = CreateContext(databasePath);
            var actual = Assert.IsType<CredentialRecord>(
                await CredentialStore(reopened, expected.TenantId).FindAsync(expected.TenantId, expected.Id));
            AssertCredential(expected, actual);
            Assert.Null(actual.ExpiresAt);
            Assert.Equal(expected.HashedSecret, actual.HashedSecret);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Tenant_local_keys_are_case_normalized_but_raw_values_are_preserved_and_framed()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);

            var originalApplication = Application("Acme", "Client", "original");
            await using (var context = CreateContext(databasePath))
                await ApplicationStore(context, originalApplication.TenantId).SaveAsync(originalApplication);

            await using (var lowerCaseContext = CreateContext(databasePath))
            {
                var lowerCaseStore = ApplicationStore(lowerCaseContext, "acme");
                var duplicate = originalApplication with { TenantId = "acme", Id = "client", DisplayName = "must-not-win" };
                var duplicateResult = await lowerCaseStore.SaveWithRevisionAsync(duplicate, expectedRevision: null);
                Assert.Equal(IamRevisionSaveStatus.Conflict, duplicateResult.Status);

                var loaded = Assert.IsType<ApplicationRecord>(await lowerCaseStore.FindAsync("acme", "CLIENT"));
                Assert.Equal("Acme", loaded.TenantId);
                Assert.Equal("Client", loaded.Id);
                Assert.Equal("original", loaded.DisplayName);
            }

            var originalCredential = Credential("Acme", "Credential", "first");
            await using (var context = CreateContext(databasePath))
                await CredentialStore(context, originalCredential.TenantId).SaveAsync(originalCredential);

            await using (var lowerCaseContext = CreateContext(databasePath))
            {
                var lowerCaseStore = CredentialStore(lowerCaseContext, "acme");
                var duplicate = originalCredential with { TenantId = "acme", Id = "credential", HashedSecret = "must-not-win" };
                var duplicateResult = await lowerCaseStore.SaveWithRevisionAsync(duplicate, expectedRevision: null);
                Assert.Equal(IamRevisionSaveStatus.Conflict, duplicateResult.Status);

                var loaded = Assert.IsType<CredentialRecord>(await lowerCaseStore.FindAsync("acme", "CREDENTIAL"));
                Assert.Equal("Acme", loaded.TenantId);
                Assert.Equal("Credential", loaded.Id);
                Assert.Equal("first", loaded.HashedSecret);
            }

            var firstApplication = Application("a", "b:c", "framed-first");
            var secondApplication = Application("a:b", "c", "framed-second");
            await using (var firstContext = CreateContext(databasePath))
                await ApplicationStore(firstContext, firstApplication.TenantId).SaveAsync(firstApplication);
            await using (var secondContext = CreateContext(databasePath))
                await ApplicationStore(secondContext, secondApplication.TenantId).SaveAsync(secondApplication);

            var firstCredential = Credential("a", "b:c", "framed-first");
            var secondCredential = Credential("a:b", "c", "framed-second");
            await using (var firstContext = CreateContext(databasePath))
                await CredentialStore(firstContext, firstCredential.TenantId).SaveAsync(firstCredential);
            await using (var secondContext = CreateContext(databasePath))
                await CredentialStore(secondContext, secondCredential.TenantId).SaveAsync(secondCredential);

            await using var verification = CreateContext(databasePath);
            Assert.Equal("framed-first", (await ApplicationStore(verification, "a").FindAsync("a", "b:c"))!.DisplayName);
            Assert.Equal("framed-second", (await ApplicationStore(verification, "a:b").FindAsync("a:b", "c"))!.DisplayName);
            Assert.Equal("framed-first", (await CredentialStore(verification, "a").FindAsync("a", "b:c"))!.HashedSecret);
            Assert.Equal("framed-second", (await CredentialStore(verification, "a:b").FindAsync("a:b", "c"))!.HashedSecret);

            Assert.Equal(3, await verification.Applications.CountAsync());
            Assert.Equal(3, await verification.Credentials.CountAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Application_collection_json_is_deterministic_for_equivalent_sets()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var first = Application("acme", "json", "first") with
            {
                AllowedGrantTypes = Set("z", "a", "grant\ud800"),
                Scopes = Set("scope-z", "scope-a", "scope\udc00")
            };
            var second = first with
            {
                AllowedGrantTypes = Set("grant\ud800", "z", "a"),
                Scopes = Set("scope\udc00", "scope-a", "scope-z")
            };

            await using var context = CreateContext(databasePath);
            var store = ApplicationStore(context, first.TenantId);
            await store.SaveAsync(first);
            var firstRow = await context.Applications.AsNoTracking().SingleAsync();
            var firstGrantJson = firstRow.AllowedGrantTypesJson;
            var firstScopeJson = firstRow.ScopesJson;

            await store.SaveAsync(second);
            var secondRow = await context.Applications.AsNoTracking().SingleAsync();
            Assert.Equal(firstGrantJson, secondRow.AllowedGrantTypesJson);
            Assert.Equal(firstScopeJson, secondRow.ScopesJson);
            AssertApplication(second, Assert.IsType<ApplicationRecord>(await store.FindAsync("acme", "json")));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Unconditional_save_is_last_write_wins_and_rotates_revisions_for_both_stores()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var applicationStore = ApplicationStore(context, "acme");
            var credentialStore = CredentialStore(context, "acme");
            var firstApplication = Application("acme", "lww-app", "first");
            var secondApplication = firstApplication with { DisplayName = "second" };
            var firstCredential = Credential("acme", "lww-credential", "first");
            var secondCredential = firstCredential with { HashedSecret = "second" };

            await applicationStore.SaveAsync(firstApplication);
            await credentialStore.SaveAsync(firstCredential);
            await applicationStore.SaveAsync(secondApplication);
            await credentialStore.SaveAsync(secondCredential);

            Assert.Equal("second", (await applicationStore.FindAsync("acme", firstApplication.Id))!.DisplayName);
            Assert.Equal("second", (await credentialStore.FindAsync("acme", firstCredential.Id))!.HashedSecret);
            Assert.Equal("gw:00000000000000000002", (await applicationStore.FindWithRevisionAsync("acme", firstApplication.Id))!.Revision);
            Assert.Equal("gw:00000000000000000002", (await credentialStore.FindWithRevisionAsync("acme", firstCredential.Id))!.Revision);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Revision_saves_support_create_only_rotation_stale_no_mutation_missing_and_malformed_for_both_stores()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var applicationStore = ApplicationStore(context, "acme");
            var credentialStore = CredentialStore(context, "acme");
            var firstApplication = Application("acme", "cas-app", "first");
            var firstCredential = Credential("acme", "cas-credential", "first");

            var applicationCreated = await applicationStore.SaveWithRevisionAsync(firstApplication, expectedRevision: null);
            var credentialCreated = await credentialStore.SaveWithRevisionAsync(firstCredential, expectedRevision: null);
            Assert.Equal(IamRevisionSaveStatus.Saved, applicationCreated.Status);
            Assert.Equal(IamRevisionSaveStatus.Saved, credentialCreated.Status);
            var applicationRevision = Assert.IsType<string>(applicationCreated.Revision);
            var credentialRevision = Assert.IsType<string>(credentialCreated.Revision);

            var applicationDuplicate = await applicationStore.SaveWithRevisionAsync(firstApplication with { DisplayName = "duplicate" }, null);
            var credentialDuplicate = await credentialStore.SaveWithRevisionAsync(firstCredential with { HashedSecret = "duplicate" }, null);
            Assert.Equal(IamRevisionSaveStatus.Conflict, applicationDuplicate.Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict, credentialDuplicate.Status);

            var applicationMalformed = await applicationStore.SaveWithRevisionAsync(firstApplication with { DisplayName = "malformed" }, "not-a-revision");
            var credentialMalformed = await credentialStore.SaveWithRevisionAsync(firstCredential with { HashedSecret = "malformed" }, "not-a-revision");
            Assert.Equal(IamRevisionSaveStatus.Conflict, applicationMalformed.Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict, credentialMalformed.Status);

            var missingApplication = await applicationStore.SaveWithRevisionAsync(Application("acme", "missing-app", "missing"), applicationRevision);
            var missingCredential = await credentialStore.SaveWithRevisionAsync(Credential("acme", "missing-credential", "missing"), credentialRevision);
            Assert.Equal(IamRevisionSaveStatus.NotFound, missingApplication.Status);
            Assert.Equal(IamRevisionSaveStatus.NotFound, missingCredential.Status);

            var applicationUpdated = await applicationStore.SaveWithRevisionAsync(firstApplication with { DisplayName = "winner" }, applicationRevision);
            var credentialUpdated = await credentialStore.SaveWithRevisionAsync(firstCredential with { HashedSecret = "winner" }, credentialRevision);
            Assert.Equal(IamRevisionSaveStatus.Saved, applicationUpdated.Status);
            Assert.Equal(IamRevisionSaveStatus.Saved, credentialUpdated.Status);
            Assert.NotEqual(applicationRevision, applicationUpdated.Revision);
            Assert.NotEqual(credentialRevision, credentialUpdated.Revision);

            var applicationStale = await applicationStore.SaveWithRevisionAsync(firstApplication with { DisplayName = "stale" }, applicationRevision);
            var credentialStale = await credentialStore.SaveWithRevisionAsync(firstCredential with { HashedSecret = "stale" }, credentialRevision);
            Assert.Equal(IamRevisionSaveStatus.Conflict, applicationStale.Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict, credentialStale.Status);
            Assert.Equal("winner", (await applicationStore.FindAsync("acme", firstApplication.Id))!.DisplayName);
            Assert.Equal("winner", (await credentialStore.FindAsync("acme", firstCredential.Id))!.HashedSecret);
            Assert.Null(await applicationStore.FindAsync("acme", "missing-app"));
            Assert.Null(await credentialStore.FindAsync("acme", "missing-credential"));
            Assert.Empty(context.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Cross_tenant_reads_and_writes_are_rejected_before_provider_io_for_both_stores()
    {
        var options = new DbContextOptionsBuilder<IdentityIamSqliteDbContext>()
            .UseSqlite("Data Source=/path/that/must/not/be/opened/identity-iam.db")
            .Options;
        await using var context = new IdentityIamSqliteDbContext(options);
        var applicationStore = ApplicationStore(context, "acme");
        var credentialStore = CredentialStore(context, "acme");

        await Assert.ThrowsAsync<InvalidOperationException>(() => applicationStore.FindAsync("other", "app").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => applicationStore.SaveAsync(Application("other", "app", "rejected")).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => credentialStore.FindAsync("other", "credential").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => credentialStore.SaveAsync(Credential("other", "credential", "rejected")).AsTask());
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Cancellation_during_writes_and_queries_clears_tracking_for_both_stores()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);

            using var applicationCancellation = new CancellationTokenSource();
            var applicationInterceptor = new CancelingSaveInterceptor(applicationCancellation);
            await using (var context = CreateContext(databasePath, applicationInterceptor))
            {
                var store = ApplicationStore(context, "acme");
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    store.SaveAsync(Application("acme", "canceled-app", "never-committed"), applicationCancellation.Token).AsTask());
                Assert.Empty(context.ChangeTracker.Entries());
            }

            using var credentialCancellation = new CancellationTokenSource();
            var credentialInterceptor = new CancelingSaveInterceptor(credentialCancellation);
            await using (var context = CreateContext(databasePath, credentialInterceptor))
            {
                var store = CredentialStore(context, "acme");
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    store.SaveAsync(Credential("acme", "canceled-credential", "never-committed"), credentialCancellation.Token).AsTask());
                Assert.Empty(context.ChangeTracker.Entries());
            }

            await using (var verification = CreateContext(databasePath))
            {
                Assert.Null(await ApplicationStore(verification, "acme").FindAsync("acme", "canceled-app"));
                Assert.Null(await CredentialStore(verification, "acme").FindAsync("acme", "canceled-credential"));
            }

            using var applicationQueryCancellation = new CancellationTokenSource();
            applicationQueryCancellation.Cancel();
            await using (var context = CreateContext(databasePath))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    ApplicationStore(context, "acme").FindAsync("acme", "missing", applicationQueryCancellation.Token).AsTask());
                Assert.Empty(context.ChangeTracker.Entries());
            }

            using var credentialQueryCancellation = new CancellationTokenSource();
            credentialQueryCancellation.Cancel();
            await using (var context = CreateContext(databasePath))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    CredentialStore(context, "acme").FindAsync("acme", "missing", credentialQueryCancellation.Token).AsTask());
                Assert.Empty(context.ChangeTracker.Entries());
            }
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Explicit_transaction_rollback_preserves_only_committed_application_and_credential_state()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var originalApplication = Application("acme", "rollback-app", "committed");
            var originalCredential = Credential("acme", "rollback-credential", "committed");
            await using (var seed = CreateContext(databasePath))
            {
                await ApplicationStore(seed, "acme").SaveAsync(originalApplication);
                await CredentialStore(seed, "acme").SaveAsync(originalCredential);
            }

            await using (var transactionContext = CreateContext(databasePath))
            {
                await using var transaction = await transactionContext.Database.BeginTransactionAsync();
                await ApplicationStore(transactionContext, "acme").SaveAsync(originalApplication with { DisplayName = "rolled-back" });
                await CredentialStore(transactionContext, "acme").SaveAsync(originalCredential with { HashedSecret = "rolled-back" });
                await transaction.RollbackAsync();
                Assert.Empty(transactionContext.ChangeTracker.Entries());
            }

            await using var reopened = CreateContext(databasePath);
            Assert.Equal("committed", (await ApplicationStore(reopened, "acme").FindAsync("acme", originalApplication.Id))!.DisplayName);
            Assert.Equal("committed", (await CredentialStore(reopened, "acme").FindAsync("acme", originalCredential.Id))!.HashedSecret);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Unconditional_transient_conflicts_retry_with_a_bound_and_leave_no_ghost_rows()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);

            var applicationRetry = new TransientSaveInterceptor(failures: 1);
            await using (var context = CreateContext(databasePath, applicationRetry))
            {
                await ApplicationStore(context, "acme").SaveAsync(Application("acme", "app-retry", "saved"));
                Assert.Equal(2, applicationRetry.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            var credentialRetry = new TransientSaveInterceptor(failures: 1);
            await using (var context = CreateContext(databasePath, credentialRetry))
            {
                await CredentialStore(context, "acme").SaveAsync(Credential("acme", "credential-retry", "saved"));
                Assert.Equal(2, credentialRetry.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            var applicationExhaustion = new TransientSaveInterceptor(failures: int.MaxValue);
            await using (var context = CreateContext(databasePath, applicationExhaustion))
            {
                await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                    ApplicationStore(context, "acme")
                        .SaveAsync(Application("acme", "app-retry-failure", "never-saved"))
                        .AsTask());
                Assert.Equal(3, applicationExhaustion.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            var credentialExhaustion = new TransientSaveInterceptor(failures: int.MaxValue);
            await using (var context = CreateContext(databasePath, credentialExhaustion))
            {
                await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                    CredentialStore(context, "acme")
                        .SaveAsync(Credential("acme", "credential-retry-failure", "never-saved"))
                        .AsTask());
                Assert.Equal(3, credentialExhaustion.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            await using var verification = CreateContext(databasePath);
            Assert.Null(await ApplicationStore(verification, "acme").FindAsync("acme", "app-retry-failure"));
            Assert.Null(await CredentialStore(verification, "acme").FindAsync("acme", "credential-retry-failure"));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Create_only_transient_conflicts_retry_with_a_bound_and_leave_no_ghost_rows()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);

            var applicationRetry = new TransientSaveInterceptor(failures: 1);
            await using (var context = CreateContext(databasePath, applicationRetry))
            {
                var result = await ApplicationStore(context, "acme")
                    .SaveWithRevisionAsync(Application("acme", "app-create-retry", "saved"), null);
                Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
                Assert.Equal(2, applicationRetry.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            var credentialRetry = new TransientSaveInterceptor(failures: 1);
            await using (var context = CreateContext(databasePath, credentialRetry))
            {
                var result = await CredentialStore(context, "acme")
                    .SaveWithRevisionAsync(Credential("acme", "credential-create-retry", "saved"), null);
                Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
                Assert.Equal(2, credentialRetry.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            var applicationExhaustion = new TransientSaveInterceptor(failures: int.MaxValue);
            await using (var context = CreateContext(databasePath, applicationExhaustion))
            {
                await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                    ApplicationStore(context, "acme")
                        .SaveWithRevisionAsync(Application("acme", "app-create-retry-failure", "never-saved"), null)
                        .AsTask());
                Assert.Equal(3, applicationExhaustion.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            var credentialExhaustion = new TransientSaveInterceptor(failures: int.MaxValue);
            await using (var context = CreateContext(databasePath, credentialExhaustion))
            {
                await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                    CredentialStore(context, "acme")
                        .SaveWithRevisionAsync(Credential("acme", "credential-create-retry-failure", "never-saved"), null)
                        .AsTask());
                Assert.Equal(3, credentialExhaustion.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            await using var verification = CreateContext(databasePath);
            Assert.Null(await ApplicationStore(verification, "acme").FindAsync("acme", "app-create-retry-failure"));
            Assert.Null(await CredentialStore(verification, "acme").FindAsync("acme", "credential-create-retry-failure"));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Cas_transient_conflicts_retry_with_a_bound_and_preserve_committed_rows()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var retryApplication = Application("acme", "app-cas-retry", "original");
            var failedApplication = Application("acme", "app-cas-retry-failure", "original");
            var retryCredential = Credential("acme", "credential-cas-retry", "original");
            var failedCredential = Credential("acme", "credential-cas-retry-failure", "original");
            string retryApplicationRevision;
            string failedApplicationRevision;
            string retryCredentialRevision;
            string failedCredentialRevision;
            await using (var seed = CreateContext(databasePath))
            {
                var applicationStore = ApplicationStore(seed, "acme");
                var credentialStore = CredentialStore(seed, "acme");
                retryApplicationRevision = Assert.IsType<string>((await applicationStore.SaveWithRevisionAsync(retryApplication, null)).Revision);
                failedApplicationRevision = Assert.IsType<string>((await applicationStore.SaveWithRevisionAsync(failedApplication, null)).Revision);
                retryCredentialRevision = Assert.IsType<string>((await credentialStore.SaveWithRevisionAsync(retryCredential, null)).Revision);
                failedCredentialRevision = Assert.IsType<string>((await credentialStore.SaveWithRevisionAsync(failedCredential, null)).Revision);
            }

            var applicationRetry = new TransientSaveInterceptor(failures: 1);
            await using (var context = CreateContext(databasePath, applicationRetry))
            {
                var result = await ApplicationStore(context, "acme")
                    .SaveWithRevisionAsync(retryApplication with { DisplayName = "updated" }, retryApplicationRevision);
                Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
                Assert.Equal(2, applicationRetry.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            var credentialRetry = new TransientSaveInterceptor(failures: 1);
            await using (var context = CreateContext(databasePath, credentialRetry))
            {
                var result = await CredentialStore(context, "acme")
                    .SaveWithRevisionAsync(retryCredential with { HashedSecret = "updated" }, retryCredentialRevision);
                Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
                Assert.Equal(2, credentialRetry.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            var applicationExhaustion = new TransientSaveInterceptor(failures: int.MaxValue);
            await using (var context = CreateContext(databasePath, applicationExhaustion))
            {
                await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                    ApplicationStore(context, "acme")
                        .SaveWithRevisionAsync(failedApplication with { DisplayName = "never-saved" }, failedApplicationRevision)
                        .AsTask());
                Assert.Equal(3, applicationExhaustion.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            var credentialExhaustion = new TransientSaveInterceptor(failures: int.MaxValue);
            await using (var context = CreateContext(databasePath, credentialExhaustion))
            {
                await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                    CredentialStore(context, "acme")
                        .SaveWithRevisionAsync(failedCredential with { HashedSecret = "never-saved" }, failedCredentialRevision)
                        .AsTask());
                Assert.Equal(3, credentialExhaustion.Attempts);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            await using var verification = CreateContext(databasePath);
            var applicationStoreVerification = ApplicationStore(verification, "acme");
            var credentialStoreVerification = CredentialStore(verification, "acme");
            Assert.Equal("updated", (await applicationStoreVerification.FindAsync("acme", retryApplication.Id))!.DisplayName);
            Assert.Equal("original", (await applicationStoreVerification.FindAsync("acme", failedApplication.Id))!.DisplayName);
            Assert.Equal("updated", (await credentialStoreVerification.FindAsync("acme", retryCredential.Id))!.HashedSecret);
            Assert.Equal("original", (await credentialStoreVerification.FindAsync("acme", failedCredential.Id))!.HashedSecret);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Theory]
    [InlineData("application-grants")]
    [InlineData("application-scopes")]
    [InlineData("credential-expiry")]
    [InlineData("credential-revision")]
    public async Task Corrupted_json_revision_and_expiry_values_are_wrapped_without_tracking(string corruption)
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            await using var context = CreateContext(databasePath);

            if (corruption.StartsWith("application", StringComparison.Ordinal))
            {
                var application = Application("acme", $"corrupt-{corruption}", "kind");
                var store = ApplicationStore(context, application.TenantId);
                await store.SaveAsync(application);
                var id = await context.Applications.AsNoTracking().Select(entity => entity.Id).SingleAsync();
                if (corruption.EndsWith("grants", StringComparison.Ordinal))
                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE \"identity_applications\" SET \"AllowedGrantTypesJson\" = {0} WHERE \"Id\" = {1}",
                        "null",
                        id);
                else
                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE \"identity_applications\" SET \"ScopesJson\" = {0} WHERE \"Id\" = {1}",
                        "null",
                        id);
                context.ChangeTracker.Clear();

                var exception = await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                    store.FindAsync(application.TenantId, application.Id).AsTask());
                Assert.IsType<FormatException>(exception.InnerException);
            }
            else
            {
                var credential = Credential("acme", $"corrupt-{corruption}", "hash");
                var store = CredentialStore(context, credential.TenantId);
                await store.SaveAsync(credential);
                var id = await context.Credentials.AsNoTracking().Select(entity => entity.Id).SingleAsync();
                if (corruption.EndsWith("expiry", StringComparison.Ordinal))
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE \"identity_credentials\" SET \"ExpiresAt\" = {0} WHERE \"Id\" = {1}",
                        "not-a-date",
                        id);
                    context.ChangeTracker.Clear();
                    var exception = await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                        store.FindAsync(credential.TenantId, credential.Id).AsTask());
                    Assert.IsType<FormatException>(exception.InnerException);
                }
                else
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE \"identity_credentials\" SET \"Revision\" = 0 WHERE \"Id\" = {0}",
                        id);
                    context.ChangeTracker.Clear();
                    var exception = await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                        store.FindWithRevisionAsync(credential.TenantId, credential.Id).AsTask());
                    Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
                }
            }

            Assert.Empty(context.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Theory]
    [InlineData("application")]
    [InlineData("application-revision")]
    [InlineData("credential")]
    [InlineData("credential-revision")]
    public async Task Provider_read_failures_are_wrapped_and_leave_no_tracked_state(string operation)
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            await using var context = CreateContext(databasePath, new FailingReadInterceptor());
            var applicationStore = ApplicationStore(context, "acme");
            var credentialStore = CredentialStore(context, "acme");

            async Task ReadAsync()
            {
                switch (operation)
                {
                    case "application":
                        await applicationStore.FindAsync("acme", "app");
                        break;
                    case "application-revision":
                        await applicationStore.FindWithRevisionAsync("acme", "app");
                        break;
                    case "credential":
                        await credentialStore.FindAsync("acme", "credential");
                        break;
                    case "credential-revision":
                        await credentialStore.FindWithRevisionAsync("acme", "credential");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
                }
            }

            var exception = await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(ReadAsync);
            Assert.IsType<SqliteException>(exception.InnerException);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Concurrent_create_only_has_one_deterministic_winner_for_both_stores()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var applicationResults = await RunConcurrentApplicationCreateAsync(databasePath);
            var credentialResults = await RunConcurrentCredentialCreateAsync(databasePath);

            Assert.Equal(IamRevisionSaveStatus.Saved, applicationResults[0].Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict, applicationResults[1].Status);
            Assert.Equal(IamRevisionSaveStatus.Saved, credentialResults[0].Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict, credentialResults[1].Status);

            await using var verification = CreateContext(databasePath);
            Assert.Equal("winner", (await ApplicationStore(verification, "acme").FindAsync("acme", "concurrent-app"))!.DisplayName);
            Assert.Equal("winner", (await CredentialStore(verification, "acme").FindAsync("acme", "concurrent-credential"))!.HashedSecret);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Concurrent_compare_and_swap_has_one_deterministic_winner_for_both_stores()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var applicationResults = await RunConcurrentApplicationCasAsync(databasePath);
            var credentialResults = await RunConcurrentCredentialCasAsync(databasePath);

            Assert.Equal(IamRevisionSaveStatus.Saved, applicationResults[0].Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict, applicationResults[1].Status);
            Assert.Equal(IamRevisionSaveStatus.Saved, credentialResults[0].Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict, credentialResults[1].Status);

            await using var verification = CreateContext(databasePath);
            Assert.Equal("winner", (await ApplicationStore(verification, "acme").FindAsync("acme", "concurrent-cas-app"))!.DisplayName);
            Assert.Equal("winner", (await CredentialStore(verification, "acme").FindAsync("acme", "concurrent-cas-credential"))!.HashedSecret);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static async Task<IamRevisionSaveResult[]> RunConcurrentApplicationCreateAsync(string databasePath)
    {
        var barrier = new DeterministicSaveBarrier();
        await using var winnerContext = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: true));
        await using var loserContext = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: false));
        return await Task.WhenAll(
            ApplicationStore(winnerContext, "acme").SaveWithRevisionAsync(Application("acme", "concurrent-app", "winner"), null).AsTask(),
            ApplicationStore(loserContext, "acme").SaveWithRevisionAsync(Application("acme", "concurrent-app", "loser"), null).AsTask());
    }

    private static async Task<IamRevisionSaveResult[]> RunConcurrentCredentialCreateAsync(string databasePath)
    {
        var barrier = new DeterministicSaveBarrier();
        await using var winnerContext = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: true));
        await using var loserContext = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: false));
        return await Task.WhenAll(
            CredentialStore(winnerContext, "acme").SaveWithRevisionAsync(Credential("acme", "concurrent-credential", "winner"), null).AsTask(),
            CredentialStore(loserContext, "acme").SaveWithRevisionAsync(Credential("acme", "concurrent-credential", "loser"), null).AsTask());
    }

    private static async Task<IamRevisionSaveResult[]> RunConcurrentApplicationCasAsync(string databasePath)
    {
        var original = Application("acme", "concurrent-cas-app", "original");
        string revision;
        await using (var seed = CreateContext(databasePath))
            revision = Assert.IsType<string>((await ApplicationStore(seed, "acme").SaveWithRevisionAsync(original, null)).Revision);

        var barrier = new DeterministicSaveBarrier();
        await using var winnerContext = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: true));
        await using var loserContext = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: false));
        return await Task.WhenAll(
            ApplicationStore(winnerContext, "acme").SaveWithRevisionAsync(original with { DisplayName = "winner" }, revision).AsTask(),
            ApplicationStore(loserContext, "acme").SaveWithRevisionAsync(original with { DisplayName = "loser" }, revision).AsTask());
    }

    private static async Task<IamRevisionSaveResult[]> RunConcurrentCredentialCasAsync(string databasePath)
    {
        var original = Credential("acme", "concurrent-cas-credential", "original");
        string revision;
        await using (var seed = CreateContext(databasePath))
            revision = Assert.IsType<string>((await CredentialStore(seed, "acme").SaveWithRevisionAsync(original, null)).Revision);

        var barrier = new DeterministicSaveBarrier();
        await using var winnerContext = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: true));
        await using var loserContext = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: false));
        return await Task.WhenAll(
            CredentialStore(winnerContext, "acme").SaveWithRevisionAsync(original with { HashedSecret = "winner" }, revision).AsTask(),
            CredentialStore(loserContext, "acme").SaveWithRevisionAsync(original with { HashedSecret = "loser" }, revision).AsTask());
    }

    private static ApplicationRecord Application(string tenantId, string id, string value) => new(
        id,
        tenantId,
        $"client-{value}",
        value,
        ApplicationType.Public,
        ResourceOwnership.Foundation,
        Set("client_credentials", "authorization_code"),
        Set("openid", "profile"));

    private static CredentialRecord Credential(string tenantId, string id, string hash) => new(
        id,
        tenantId,
        CredentialSubjectType.Application,
        "application-1",
        CredentialKind.ClientSecret,
        hash,
        "SHA256",
        CredentialStatus.Active,
        new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)).AddTicks(6789));

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

    private static void AssertApplication(ApplicationRecord expected, ApplicationRecord actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.TenantId, actual.TenantId);
        Assert.Equal(expected.ClientId, actual.ClientId);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Ownership, actual.Ownership);
        AssertSet(expected.AllowedGrantTypes, actual.AllowedGrantTypes);
        AssertSet(expected.Scopes, actual.Scopes);
    }

    private static void AssertCredential(CredentialRecord expected, CredentialRecord actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.TenantId, actual.TenantId);
        Assert.Equal(expected.SubjectType, actual.SubjectType);
        Assert.Equal(expected.SubjectId, actual.SubjectId);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.HashedSecret, actual.HashedSecret);
        Assert.Equal(expected.HashAlgorithm, actual.HashAlgorithm);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.ExpiresAt?.Offset, actual.ExpiresAt?.Offset);
        Assert.Equal(expected.ExpiresAt?.Ticks, actual.ExpiresAt?.Ticks);
    }

    private static void AssertSet(IReadOnlySet<string> expected, IReadOnlySet<string> actual) =>
        Assert.Equal(
            expected.OrderBy(value => value, StringComparer.Ordinal),
            actual.OrderBy(value => value, StringComparer.Ordinal));

    private static EfApplicationStore ApplicationStore(IdentityIamDbContext context, string tenantId) =>
        new(context, new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId))));

    private static EfCredentialStore CredentialStore(IdentityIamDbContext context, string tenantId) =>
        new(context, new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId))));

    private static IdentityIamSqliteDbContext CreateContext(string databasePath, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<IdentityIamSqliteDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=5");
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return new IdentityIamSqliteDbContext(builder.Options);
    }

    private static async Task EnsureDatabaseAsync(string databasePath)
    {
        await using var context = CreateContext(databasePath);
        await context.Database.EnsureCreatedAsync();
    }

    private static string TemporaryDatabasePath() =>
        Path.Join(Path.GetTempPath(), $"elsa-identity-iam-{Guid.NewGuid():N}.db");

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" }.Where(File.Exists))
            File.Delete(path);
    }

    private sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class CancelingSaveInterceptor(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
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

    private sealed class FailingReadInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<DbDataReader>>(
                new SqliteException("Injected provider read failure.", 1));
    }

    private sealed class DeterministicSaveBarrier
    {
        private readonly TaskCompletionSource loserArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource winnerCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SignalLoserArrived() => loserArrived.TrySetResult();
        public Task WaitForLoserAsync(CancellationToken cancellationToken) => loserArrived.Task.WaitAsync(cancellationToken);
        public void SignalWinnerCompleted() => winnerCompleted.TrySetResult();
        public Task WaitForWinnerAsync(CancellationToken cancellationToken) => winnerCompleted.Task.WaitAsync(cancellationToken);
    }

    private sealed class OrderedSaveChangesInterceptor(
        DeterministicSaveBarrier barrier,
        bool isWinner) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (isWinner)
                await barrier.WaitForLoserAsync(cancellationToken);
            else
            {
                barrier.SignalLoserArrived();
                await barrier.WaitForWinnerAsync(cancellationToken);
            }

            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (isWinner)
                barrier.SignalWinnerCompleted();
            return ValueTask.FromResult(result);
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (isWinner)
                barrier.SignalWinnerCompleted();
            return Task.CompletedTask;
        }
    }
}
