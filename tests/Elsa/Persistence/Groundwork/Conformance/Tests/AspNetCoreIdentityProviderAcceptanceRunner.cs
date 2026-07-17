using System.Security.Claims;
using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Conformance.Tests.Probes;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Queries;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Executes the provider-independent behavior shared by the four physical Identity provider entry points.
/// Provider-native plan, topology, and key-budget assertions remain with their provider adapters.
/// </summary>
internal sealed class AspNetCoreIdentityProviderAcceptanceRunner(GroundworkProviderDriver driver)
{
    public const string TenantId = "tenant-alpha";
    public const string UserId = "user-ada";
    public const string UserName = "ada";
    public const string NormalizedUserName = "ADA";
    public const string Email = "ada@example.test";
    public const string NormalizedEmail = "ADA@EXAMPLE.TEST";

    private const string CrossTenantId = "tenant-beta";
    private const string RoleId = "role-admin";
    private static readonly UserLoginInfo Login = new("github", "ada-external", "GitHub");
    private static readonly Claim PermissionClaim = new("permission", "identity.users.read");

    private static readonly IReadOnlyDictionary<Type, string> FrameworkCapabilityIds =
        new Dictionary<Type, string>
        {
            [typeof(IRoleStore<>)] = "role-store",
            [typeof(IRoleClaimStore<>)] = "role-claim-store",
            [typeof(IUserStore<>)] = "user-store",
            [typeof(IUserPasswordStore<>)] = "user-password-store",
            [typeof(IUserSecurityStampStore<>)] = "user-security-stamp-store",
            [typeof(IUserEmailStore<>)] = "user-email-store",
            [typeof(IUserLockoutStore<>)] = "user-lockout-store",
            [typeof(IUserPhoneNumberStore<>)] = "user-phone-number-store",
            [typeof(IUserTwoFactorStore<>)] = "user-two-factor-store",
            [typeof(IUserLoginStore<>)] = "user-login-store",
            [typeof(IUserClaimStore<>)] = "user-claim-store",
            [typeof(IUserRoleStore<>)] = "user-role-store",
            [typeof(IUserAuthenticationTokenStore<>)] = "user-authentication-token-store",
            [typeof(IUserAuthenticatorKeyStore<>)] = "user-authenticator-key-store",
            [typeof(IUserTwoFactorRecoveryCodeStore<>)] = "user-two-factor-recovery-code-store"
        };

    public async Task<AspNetCoreIdentityProviderAcceptanceResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        var completed = new HashSet<string>(StringComparer.Ordinal);
        await driver.InitializeAsync(cancellationToken);
        await driver.ResetPhysicalAsync([new IdentityGroundworkStorageManifestSource()], cancellationToken);

        var primaryAccess = Access(TenantId);
        var user = CreateUser(UserId, UserName, NormalizedUserName, Email, NormalizedEmail);
        var role = CreateRole();

        await using (var firstClient = await driver.OpenPhysicalClientAsync(primaryAccess, cancellationToken))
        {
            var stores = CreateStores(firstClient, TenantId);
            ObserveAdvertisedFrameworkCapabilities(stores, completed);
            RequireSucceeded(await stores.Users.CreateAsync(user, cancellationToken), "create canonical user");

            for (var index = 0; index < 512; index++)
            {
                var suffix = index.ToString("D4");
                RequireSucceeded(
                    await stores.Users.CreateAsync(
                        CreateUser(
                            $"user-plan-noise-{suffix}",
                            $"plan-noise-{suffix}",
                            $"PLAN-NOISE-{suffix}",
                            $"plan-noise-{suffix}@example.test",
                            $"PLAN-NOISE-{suffix}@EXAMPLE.TEST"),
                        cancellationToken),
                    $"create plan-noise user {suffix}");
            }

            RequireSucceeded(await stores.Roles.CreateAsync(role, cancellationToken), "create canonical role");
            await stores.Users.AddToRoleAsync(user, role.NormalizedName!, cancellationToken);
            await stores.Users.AddClaimsAsync(user, [PermissionClaim], cancellationToken);
            await stores.Users.AddLoginAsync(user, Login, cancellationToken);
            await stores.Users.SetTokenAsync(user, Login.LoginProvider, "refresh-token", "refresh-1", cancellationToken);

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                stores.Users.FindByIdAsync(UserId, cancelled.Token));
            completed.Add("cancellation.pre-cancelled-load-is-cancelled");
        }

        await AssertCrossTenantReadsDoNotDiscloseAsync(completed, cancellationToken);
        await AssertCloseReopenPreservesAuthorityAsync(primaryAccess, completed, cancellationToken);
        await AssertAtomicUserDeleteAsync(primaryAccess, role, completed, cancellationToken);
        await AssertDuplicateCreateRaceAsync(primaryAccess, completed, cancellationToken);
        await AssertExternalLoginOwnershipRaceAsync(primaryAccess, completed, cancellationToken);
        await AssertInjectedFailureLeavesNoPartialStateAsync(primaryAccess, completed, cancellationToken);
        await AssertLostAcknowledgementReconcilesCommittedResultAsync(primaryAccess, completed, cancellationToken);
        await AssertExpiredMutationReceiptIsCleanedAsync(primaryAccess, completed, cancellationToken);
        var processIds = await AssertProcessRestartAsync(completed, cancellationToken);

        return new AspNetCoreIdentityProviderAcceptanceResult(
            completed.Order(StringComparer.Ordinal).ToArray(),
            AspNetCoreIdentityProviderAcceptanceCatalog.ComputeObjectiveResultDigest(completed),
            processIds.FindProcessId,
            processIds.DuplicateProcessId);
    }

    private async Task AssertCrossTenantReadsDoNotDiscloseAsync(
        ISet<string> completed,
        CancellationToken cancellationToken)
    {
        await using var client = await driver.OpenPhysicalClientAsync(Access(CrossTenantId), cancellationToken);
        var stores = CreateStores(client, CrossTenantId);

        Assert.Null(await stores.Users.FindByIdAsync(UserId, cancellationToken));
        Assert.Null(await stores.Users.FindByNameAsync(NormalizedUserName, cancellationToken));
        Assert.Null(await stores.Users.FindByEmailAsync(NormalizedEmail, cancellationToken));
        Assert.Null(await stores.Users.FindByLoginAsync(Login.LoginProvider, Login.ProviderKey, cancellationToken));
        Assert.Null(await stores.Roles.FindByIdAsync(RoleId, cancellationToken));
        Assert.Null(await stores.Roles.FindByNameAsync("ADMINISTRATORS", cancellationToken));
        completed.Add("tenancy.cross-scope-read-is-not-disclosed");
    }

    private async Task AssertCloseReopenPreservesAuthorityAsync(
        DocumentStoreAccess access,
        ISet<string> completed,
        CancellationToken cancellationToken)
    {
        await using var client = await driver.OpenPhysicalClientAsync(access, cancellationToken);
        var stores = CreateStores(client, TenantId);
        var user = await stores.Users.FindByNameAsync(NormalizedUserName, cancellationToken);
        var byLogin = await stores.Users.FindByLoginAsync(Login.LoginProvider, Login.ProviderKey, cancellationToken);

        Assert.NotNull(user);
        Assert.Equal(UserId, user!.Id);
        Assert.Equal(Email, user.Email);
        Assert.Equal(UserId, byLogin?.Id);
        Assert.Contains(await stores.Users.GetRolesAsync(user, cancellationToken), value => value == "Administrators");
        Assert.Contains(
            await stores.Users.GetClaimsAsync(user, cancellationToken),
            value => value.Type == PermissionClaim.Type && value.Value == PermissionClaim.Value);
        Assert.Equal(
            "refresh-1",
            await stores.Users.GetTokenAsync(user, Login.LoginProvider, "refresh-token", cancellationToken));
        completed.Add("lifecycle.close-reopen-preserves-authority");
    }

    private async Task AssertAtomicUserDeleteAsync(
        DocumentStoreAccess access,
        IdentityRole role,
        ISet<string> completed,
        CancellationToken cancellationToken)
    {
        await using var client = await driver.OpenPhysicalClientAsync(access, cancellationToken);
        var stores = CreateStores(client, TenantId);
        var user = CreateUser(
            "user-delete",
            "delete-me",
            "DELETE-ME",
            "delete-me@example.test",
            "DELETE-ME@EXAMPLE.TEST");
        var login = new UserLoginInfo("github", "delete-me-external", "GitHub");

        RequireSucceeded(await stores.Users.CreateAsync(user, cancellationToken), "create delete candidate");
        await stores.Users.AddToRoleAsync(user, role.NormalizedName!, cancellationToken);
        await stores.Users.AddClaimsAsync(user, [PermissionClaim], cancellationToken);
        await stores.Users.AddLoginAsync(user, login, cancellationToken);
        await stores.Users.SetTokenAsync(user, login.LoginProvider, "refresh-token", "delete-me-refresh", cancellationToken);

        var current = await stores.Users.FindByIdAsync(user.Id, cancellationToken)
                      ?? throw new InvalidOperationException("The delete candidate could not be reloaded.");
        RequireSucceeded(await stores.Users.DeleteAsync(current, cancellationToken), "delete candidate with relationships");

        Assert.Null(await stores.Users.FindByIdAsync(user.Id, cancellationToken));
        Assert.Null(await stores.Users.FindByLoginAsync(login.LoginProvider, login.ProviderKey, cancellationToken));
        Assert.Empty(await stores.Users.GetRolesAsync(current, cancellationToken));
        Assert.Empty(await stores.Users.GetClaimsAsync(current, cancellationToken));
        Assert.Empty(await stores.Users.GetLoginsAsync(current, cancellationToken));
        Assert.Null(await stores.Users.GetTokenAsync(current, login.LoginProvider, "refresh-token", cancellationToken));
        completed.Add("delete.user-delete-removes-relationships-after-success");
    }

    private async Task AssertDuplicateCreateRaceAsync(
        DocumentStoreAccess access,
        ISet<string> completed,
        CancellationToken cancellationToken)
    {
        await using var firstClient = await driver.OpenPhysicalClientAsync(access, cancellationToken);
        await using var secondClient = await driver.OpenPhysicalClientAsync(access, cancellationToken);
        var firstStore = CreateStores(firstClient, TenantId).Users;
        var secondStore = CreateStores(secondClient, TenantId).Users;
        var first = CreateUser("user-race-a", "race-a", "RACE", "race-a@example.test", "RACE-A@EXAMPLE.TEST");
        var second = CreateUser("user-race-b", "race-b", "RACE", "race-b@example.test", "RACE-B@EXAMPLE.TEST");

        var results = await Task.WhenAll(
            firstStore.CreateAsync(first, cancellationToken),
            secondStore.CreateAsync(second, cancellationToken));

        Assert.Equal(1, results.Count(result => result.Succeeded));
        Assert.Single(results.Where(result => !result.Succeeded), result =>
            result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.DuplicateUserName)));
        completed.Add("concurrency.duplicate-normalized-user-name-has-one-winner");
    }

    private async Task AssertExternalLoginOwnershipRaceAsync(
        DocumentStoreAccess access,
        ISet<string> completed,
        CancellationToken cancellationToken)
    {
        await using var firstClient = await driver.OpenPhysicalClientAsync(access, cancellationToken);
        await using var secondClient = await driver.OpenPhysicalClientAsync(access, cancellationToken);
        var firstStore = CreateStores(firstClient, TenantId).Users;
        var secondStore = CreateStores(secondClient, TenantId).Users;
        var first = CreateUser(
            "user-login-race-a",
            "login-race-a",
            "LOGIN-RACE-A",
            "login-race-a@example.test",
            "LOGIN-RACE-A@EXAMPLE.TEST");
        var second = CreateUser(
            "user-login-race-b",
            "login-race-b",
            "LOGIN-RACE-B",
            "login-race-b@example.test",
            "LOGIN-RACE-B@EXAMPLE.TEST");
        var login = new UserLoginInfo("shared-race-provider", "shared-race-subject", "Shared race login");
        RequireSucceeded(await firstStore.CreateAsync(first, cancellationToken), "create first external-login contender");
        RequireSucceeded(await secondStore.CreateAsync(second, cancellationToken), "create second external-login contender");

        var attempts = await Task.WhenAll(
            CaptureAsync(() => firstStore.AddLoginAsync(first, login, cancellationToken)),
            CaptureAsync(() => secondStore.AddLoginAsync(second, login, cancellationToken)));

        Assert.Single(attempts, exception => exception is null);
        Assert.Single(attempts, exception => exception is InvalidOperationException);
        var resolved = await firstStore.FindByLoginAsync(login.LoginProvider, login.ProviderKey, cancellationToken);
        Assert.NotNull(resolved);
        Assert.Contains(resolved!.Id, new[] { first.Id, second.Id });
        var currentFirst = await firstStore.FindByIdAsync(first.Id, cancellationToken)
                           ?? throw new InvalidOperationException("The first external-login contender disappeared.");
        var currentSecond = await secondStore.FindByIdAsync(second.Id, cancellationToken)
                            ?? throw new InvalidOperationException("The second external-login contender disappeared.");
        var firstOwns = (await firstStore.GetLoginsAsync(currentFirst, cancellationToken)).Any(value =>
            value.LoginProvider == login.LoginProvider && value.ProviderKey == login.ProviderKey);
        var secondOwns = (await secondStore.GetLoginsAsync(currentSecond, cancellationToken)).Any(value =>
            value.LoginProvider == login.LoginProvider && value.ProviderKey == login.ProviderKey);
        Assert.NotEqual(firstOwns, secondOwns);
        Assert.Equal(resolved.Id == first.Id, firstOwns);
        Assert.Equal(resolved.Id == second.Id, secondOwns);
        completed.Add("concurrency.external-login-owner-has-one-winner");
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private async Task<(int FindProcessId, int DuplicateProcessId)> AssertProcessRestartAsync(
        ISet<string> completed,
        CancellationToken cancellationToken)
    {
        var restartUser = new AspNetCoreIdentityRestartProbeUser(
            TenantId,
            UserId,
            UserName,
            NormalizedUserName,
            Email,
            NormalizedEmail,
            "Ada Lovelace");
        var find = await driver.RunInNewProcessAsync(
            AspNetCoreIdentityRestartProbe.FindByNormalizedUserName(restartUser),
            cancellationToken);
        var duplicate = await driver.RunInNewProcessAsync(
            AspNetCoreIdentityRestartProbe.DuplicateCreate(restartUser),
            cancellationToken);

        Assert.Equal(GroundworkProcessProbeOperation.IdentityFindByNormalizedUserName, find.Operation);
        Assert.Equal(GroundworkProcessProbeOperation.IdentityDuplicateCreate, duplicate.Operation);
        Assert.True(find.ProcessId > 0);
        Assert.True(duplicate.ProcessId > 0);
        Assert.NotEqual(Environment.ProcessId, find.ProcessId);
        Assert.NotEqual(Environment.ProcessId, duplicate.ProcessId);
        completed.Add("lifecycle.process-restart-preserves-authority");
        return (find.ProcessId, duplicate.ProcessId);
    }

    private async Task AssertInjectedFailureLeavesNoPartialStateAsync(
        DocumentStoreAccess access,
        ISet<string> completed,
        CancellationToken cancellationToken)
    {
        const string userId = "user-fail-before-commit";
        const string normalizedUserName = "FAIL-BEFORE-COMMIT";
        const string normalizedEmail = "FAIL-BEFORE-COMMIT@EXAMPLE.TEST";
        var failures = NewFailureController();
        failures.FailAt(GroundworkFailureInjectingDocumentStore.BeforeUnderlyingCommit);
        string receiptId;

        await using (var client = await driver.OpenPhysicalClientAsync(access, cancellationToken))
        {
            var decorated = Decorate(client, access, failures);
            var stores = CreateStores(decorated, TenantId, IdentityEmailUniquenessPolicy.Unique);
            var user = CreateUser(
                userId,
                "fail-before-commit",
                normalizedUserName,
                "fail-before-commit@example.test",
                normalizedEmail);

            var exception = await Assert.ThrowsAsync<InjectedGroundworkFailureException>(() =>
                stores.Users.CreateAsync(user, cancellationToken));

            Assert.Equal(GroundworkFailureInjectingDocumentStore.BeforeUnderlyingCommit, exception.Window);
            Assert.Equal(
                [GroundworkFailureInjectingDocumentStore.BeforeUnderlyingCommit],
                failures.ReachedWindows);
            receiptId = Assert.Single(decorated.StagedSaves, request =>
                request.DocumentKind == IdentityStorageManifest.IdentityMutationReceiptDocumentKind).Id;
        }

        await using (var reopened = await driver.OpenPhysicalClientAsync(access, cancellationToken))
        {
            Assert.Null(await reopened.DocumentStore.LoadAsync(
                IdentityStorageManifest.IdentityUserDocumentKind,
                IdentityCompositeDocumentId.From(TenantId, userId),
                cancellationToken));
            Assert.Null(await reopened.DocumentStore.LoadAsync(
                IdentityStorageManifest.UserNameReservationDocumentKind,
                IdentityDocumentId.From(TenantId, normalizedUserName),
                cancellationToken));
            Assert.Null(await reopened.DocumentStore.LoadAsync(
                IdentityStorageManifest.EmailReservationDocumentKind,
                IdentityDocumentId.From(TenantId, normalizedEmail),
                cancellationToken));
            Assert.Null(await reopened.DocumentStore.LoadAsync(
                IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                receiptId,
                cancellationToken));
        }

        completed.Add("atomicity.injected-failure-does-not-leave-partial-state");
    }

    private async Task AssertLostAcknowledgementReconcilesCommittedResultAsync(
        DocumentStoreAccess access,
        ISet<string> completed,
        CancellationToken cancellationToken)
    {
        const string userId = "user-lost-acknowledgement";
        const string normalizedUserName = "LOST-ACKNOWLEDGEMENT";
        const string normalizedEmail = "LOST-ACKNOWLEDGEMENT@EXAMPLE.TEST";
        var failures = NewFailureController();
        failures.FailAt(GroundworkFailureInjectingDocumentStore.AfterUnderlyingCommit);
        string receiptId;

        await using (var client = await driver.OpenPhysicalClientAsync(access, cancellationToken))
        {
            var decorated = Decorate(client, access, failures);
            var stores = CreateStores(decorated, TenantId, IdentityEmailUniquenessPolicy.Unique);
            var user = CreateUser(
                userId,
                "lost-acknowledgement",
                normalizedUserName,
                "lost-acknowledgement@example.test",
                normalizedEmail);

            RequireSucceeded(
                await stores.Users.CreateAsync(user, cancellationToken),
                "reconcile a committed user after lost acknowledgement");

            Assert.Equal(
                [
                    GroundworkFailureInjectingDocumentStore.BeforeUnderlyingCommit,
                    GroundworkFailureInjectingDocumentStore.AfterUnderlyingCommit
                ],
                failures.ReachedWindows);
            receiptId = Assert.Single(decorated.StagedSaves, request =>
                request.DocumentKind == IdentityStorageManifest.IdentityMutationReceiptDocumentKind).Id;
        }

        await using (var reopened = await driver.OpenPhysicalClientAsync(access, cancellationToken))
        {
            var userDocumentId = IdentityCompositeDocumentId.From(TenantId, userId);
            Assert.NotNull(await reopened.DocumentStore.LoadAsync(
                IdentityStorageManifest.IdentityUserDocumentKind,
                userDocumentId,
                cancellationToken));
            Assert.NotNull(await reopened.DocumentStore.LoadAsync(
                IdentityStorageManifest.UserNameReservationDocumentKind,
                IdentityDocumentId.From(TenantId, normalizedUserName),
                cancellationToken));
            Assert.NotNull(await reopened.DocumentStore.LoadAsync(
                IdentityStorageManifest.EmailReservationDocumentKind,
                IdentityDocumentId.From(TenantId, normalizedEmail),
                cancellationToken));
            var receipt = await reopened.DocumentStore.LoadAsync(
                IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                receiptId,
                cancellationToken);
            Assert.NotNull(receipt);
            AssertCommittedReceipt(receipt!, userDocumentId);
        }

        completed.Add("failure-window.lost-acknowledgement-reconciles-committed-result");
    }

    private async Task AssertExpiredMutationReceiptIsCleanedAsync(
        DocumentStoreAccess access,
        ISet<string> completed,
        CancellationToken cancellationToken)
    {
        const string staleReceiptId = "identity:stale-provider-acceptance-receipt";
        const string userId = "user-receipt-cleanup";
        var now = DateTimeOffset.UtcNow;
        var staleReceiptJson = JsonSerializer.Serialize(
            new
            {
                mutationReceiptId = staleReceiptId,
                operationId = "identity:stale-provider-acceptance-operation",
                requestFingerprint = "stale-provider-acceptance-fingerprint",
                outcome = new
                {
                    status = "Saved",
                    document = (object?)null,
                    authoritativeId = (string?)null
                },
                createdAt = now.AddDays(-2),
                expiresAt = now.AddDays(-1)
            },
            IdentityGroundworkJson.Options);

        await using (var seedClient = await driver.OpenPhysicalClientAsync(access, cancellationToken))
        {
            var seeded = await seedClient.DocumentStore.SaveAsync(
                new SaveDocumentRequest(
                    IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                    staleReceiptId,
                    IdentityStorageManifest.SchemaVersion,
                    staleReceiptJson,
                    ExpectedVersion: 0),
                cancellationToken);
            Assert.Equal(DocumentStoreWriteStatus.Saved, seeded.Status);
        }

        var failures = NewFailureController();
        await using (var mutationClient = await driver.OpenPhysicalClientAsync(access, cancellationToken))
        {
            var decorated = Decorate(mutationClient, access, failures);
            var stores = CreateStores(decorated, TenantId, IdentityEmailUniquenessPolicy.Unique);
            RequireSucceeded(
                await stores.Users.CreateAsync(
                    CreateUser(
                        userId,
                        "receipt-cleanup",
                        "RECEIPT-CLEANUP",
                        "receipt-cleanup@example.test",
                        "RECEIPT-CLEANUP@EXAMPLE.TEST"),
                    cancellationToken),
                "clean an expired mutation receipt before an authority mutation");

            var query = Assert.Single(decorated.BoundedQueries, candidate =>
                candidate.QueryIdentity == IdentityStorageManifest.ListExpiredMutationReceiptsQuery);
            Assert.Equal(IdentityStorageManifest.IdentityMutationReceiptDocumentKind, query.DocumentKind);
            Assert.Null(query.Skip);
            Assert.Equal(64, query.Take);
            var order = Assert.Single(query.Order);
            Assert.Equal(IdentityStorageManifest.MutationReceiptExpiresAtField, order.Path);
            Assert.Equal(
                global::Groundwork.Core.PhysicalStorage.PhysicalSortDirection.Ascending,
                order.Direction);
            var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
            Assert.Equal(IdentityStorageManifest.MutationReceiptExpiresAtField, comparison.Path);
            Assert.Equal(QueryComparisonOperator.LessThanOrEqual, comparison.Operator);

            var relevantEvents = decorated.Events.Where(value =>
                (value.Kind == GroundworkDocumentStoreEventKind.BoundedQuery &&
                 value.Identity == IdentityStorageManifest.ListExpiredMutationReceiptsQuery) ||
                (value.Kind == GroundworkDocumentStoreEventKind.DirectDelete && value.Identity == staleReceiptId) ||
                value.Kind is GroundworkDocumentStoreEventKind.BeginUnitOfWork or
                    GroundworkDocumentStoreEventKind.CommitUnderlying).ToArray();
            Assert.Equal(
                [
                    GroundworkDocumentStoreEventKind.BoundedQuery,
                    GroundworkDocumentStoreEventKind.DirectDelete,
                    GroundworkDocumentStoreEventKind.BeginUnitOfWork,
                    GroundworkDocumentStoreEventKind.CommitUnderlying
                ],
                relevantEvents.Select(value => value.Kind));
            var deleted = Assert.Single(relevantEvents, value =>
                value.Kind == GroundworkDocumentStoreEventKind.DirectDelete);
            Assert.Equal(1, deleted.ExpectedVersion);
        }

        await using (var reopened = await driver.OpenPhysicalClientAsync(access, cancellationToken))
        {
            Assert.Null(await reopened.DocumentStore.LoadAsync(
                IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                staleReceiptId,
                cancellationToken));
            Assert.NotNull(await reopened.DocumentStore.LoadAsync(
                IdentityStorageManifest.IdentityUserDocumentKind,
                IdentityCompositeDocumentId.From(TenantId, userId),
                cancellationToken));
        }

        completed.Add("lifecycle.expired-mutation-receipt-is-cleaned");
    }

    private static void ObserveAdvertisedFrameworkCapabilities(
        IdentityStores stores,
        ISet<string> completed)
    {
        var observed = new[] { stores.Users.GetType(), stores.Roles.GetType() }
            .SelectMany(type => type.GetInterfaces())
            .Where(type => type.IsGenericType)
            .Select(type => type.GetGenericTypeDefinition())
            .Where(FrameworkCapabilityIds.ContainsKey)
            .Select(type => FrameworkCapabilityIds[type])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        AspNetCoreIdentityProviderAcceptanceCatalog.RequireExactAdvertisedFrameworkCapabilities(observed);
        foreach (var capabilityId in observed)
            completed.Add($"framework-capability.{capabilityId}");
    }

    private static IdentityStores CreateStores(GroundworkProviderClient client, string tenantId)
    {
        var bounded = client.BoundedDocumentStore
                      ?? throw new InvalidOperationException("Physical Identity acceptance requires a bounded query runtime.");
        var accessor = new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
        return new IdentityStores(
            new GroundworkIdentityUserStore(client.DocumentStore, accessor, bounded),
            new GroundworkIdentityRoleStore(client.DocumentStore, accessor, bounded));
    }

    private static IdentityStores CreateStores(
        GroundworkFailureInjectingDocumentStore store,
        string tenantId,
        IIdentityEmailUniquenessPolicy emailUniquenessPolicy)
    {
        var accessor = new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
        return new IdentityStores(
            new GroundworkIdentityUserStore(
                store,
                accessor,
                store,
                emailUniquenessPolicy: emailUniquenessPolicy),
            new GroundworkIdentityRoleStore(store, accessor, store));
    }

    private static GroundworkFailureInjectingDocumentStore Decorate(
        GroundworkProviderClient client,
        DocumentStoreAccess access,
        GroundworkFailureController failures)
    {
        var bounded = client.BoundedDocumentStore
                      ?? throw new InvalidOperationException("Physical Identity acceptance requires a bounded query runtime.");
        return new GroundworkFailureInjectingDocumentStore(client.DocumentStore, bounded, access, failures);
    }

    private static GroundworkFailureController NewFailureController()
    {
        var failures = new GroundworkFailureController();
        failures.Reset();
        return failures;
    }

    private static void AssertCommittedReceipt(DocumentEnvelope receipt, string expectedDocumentId)
    {
        using var json = JsonDocument.Parse(receipt.ContentJson);
        var root = json.RootElement;
        var mutationReceiptId = root.GetProperty("mutationReceiptId").GetString();
        var operationId = root.GetProperty("operationId").GetString();
        var requestFingerprint = root.GetProperty("requestFingerprint").GetString();
        Assert.Equal(receipt.Id, mutationReceiptId);
        Assert.False(string.IsNullOrWhiteSpace(operationId));
        Assert.False(string.IsNullOrWhiteSpace(requestFingerprint));
        Assert.Equal(IdentityDocumentId.From("mutation-receipt", operationId), mutationReceiptId);
        Assert.Equal(
            IdentityDocumentId.From("atomic-mutation", "save-user-authority-aggregate", requestFingerprint),
            operationId);
        var outcome = root.GetProperty("outcome");
        Assert.Equal("Saved", outcome.GetProperty("status").GetString());
        var document = outcome.GetProperty("document");
        Assert.Equal(IdentityStorageManifest.IdentityUserDocumentKind, document.GetProperty("documentKind").GetString());
        Assert.Equal(expectedDocumentId, document.GetProperty("id").GetString());
    }

    private static DocumentStoreAccess Access(string tenantId) =>
        DocumentStoreAccess.Scoped(new StorageScope(tenantId));

    private static void RequireSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
            return;

        throw new InvalidOperationException(
            $"Identity acceptance operation '{operation}' failed: " +
            string.Join(", ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));
    }

    private static AspNetCoreIdentityUser CreateUser(
        string id,
        string userName,
        string normalizedUserName,
        string email,
        string normalizedEmail) => new()
        {
            Id = id,
            TenantId = TenantId,
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            NormalizedEmail = normalizedEmail,
            DisplayName = "Ada Lovelace",
            EmailConfirmed = true,
            PhoneNumber = "+31000000001",
            PhoneNumberConfirmed = true,
            LockoutEnabled = true,
            TwoFactorEnabled = true,
            SecurityStamp = $"security-{id}",
            ConcurrencyStamp = $"revision-{id}"
        };

    private static IdentityRole CreateRole() => new()
    {
        Id = RoleId,
        Name = "Administrators",
        NormalizedName = "ADMINISTRATORS",
        ConcurrencyStamp = "revision-role-admin-v1"
    };

    private sealed record IdentityStores(
        GroundworkIdentityUserStore Users,
        GroundworkIdentityRoleStore Roles);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}

internal sealed record AspNetCoreIdentityProviderAcceptanceResult(
    IReadOnlyList<string> CompletedObjectiveIds,
    string ObjectiveResultDigest,
    int FindProcessId,
    int DuplicateProcessId);
