using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;

public sealed partial class GroundworkIdentityUserStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IBoundedDocumentStore? boundedStore = null,
    GroundworkIdentityAuthorityRelationshipCoordinator? relationshipCoordinator = null,
    GroundworkIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null,
    IIdentityEmailUniquenessPolicy? emailUniquenessPolicy = null) :
    IUserPasswordStore<AspNetCoreIdentityUser>,
    IUserSecurityStampStore<AspNetCoreIdentityUser>,
    IUserEmailStore<AspNetCoreIdentityUser>,
    IUserLockoutStore<AspNetCoreIdentityUser>,
    IUserPhoneNumberStore<AspNetCoreIdentityUser>,
    IUserTwoFactorStore<AspNetCoreIdentityUser>,
    IUserLoginStore<AspNetCoreIdentityUser>,
    IUserClaimStore<AspNetCoreIdentityUser>,
    IUserRoleStore<AspNetCoreIdentityUser>,
    IUserAuthenticationTokenStore<AspNetCoreIdentityUser>,
    IUserAuthenticatorKeyStore<AspNetCoreIdentityUser>,
    IUserTwoFactorRecoveryCodeStore<AspNetCoreIdentityUser>
{
    private const string AuthenticatorStoreLoginProvider = "[AspNetAuthenticatorStore]";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";
    private const string RecoveryCodeTokenProvider = "[AspNetUserStore]";
    private const string RecoveryCodeTokenName = "RecoveryCodes";
    private const int SingleResultTake = 1;
    private const int AmbiguousEmailTake = 2;
    private const int RelationshipPageSize = 512;
    private const int MaxRelationshipMaterialization = 100_000;
    private const int LockoutTransitionMaxAttempts = 5;

    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;
    private readonly GroundworkIdentityAuthorityRelationshipCoordinator _relationships =
        relationshipCoordinator ?? GroundworkIdentityAuthorityRelationshipCoordinator.ForDirectStore(store);
    private readonly GroundworkIdentityAuthorityAggregateCoordinator _aggregates =
        aggregateCoordinator ?? GroundworkIdentityAuthorityAggregateCoordinator.ForDirectStore(store);
    private readonly IIdentityEmailUniquenessPolicy _emailUniquenessPolicy =
        emailUniquenessPolicy ?? IdentityEmailUniquenessPolicy.NonUnique;

    public void Dispose()
    {
    }

    public Task<string> GetUserIdAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.UserName);

    public Task SetUserNameAsync(AspNetCoreIdentityUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(AspNetCoreIdentityUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        accessContextAccessor.Current.EnsureTenantScope(user.TenantId);
        var result = await SaveUserAggregateAsync(
            user, expectedVersion: 0, cancellationToken, Guid.NewGuid().ToString("N"));
        return ToIdentityResult(user, result);
    }

    public async Task<IdentityResult> UpdateAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        accessContextAccessor.Current.EnsureTenantScope(user.TenantId);
        if (!TryExpectedVersion(user, out var expectedVersion))
            return ConcurrencyFailure();
        var result = await SaveUserAggregateAsync(
            user, expectedVersion, cancellationToken, Guid.NewGuid().ToString("N"));
        return ToIdentityResult(user, result);
    }

    public async Task<IdentityResult> DeleteAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        accessContextAccessor.Current.EnsureTenantScope(user.TenantId);
        if (!TryExpectedVersion(user, out var expectedVersion) || expectedVersion is not { } version)
            return ConcurrencyFailure();

        var result = await _aggregates.DeleteUserAsync(
            user.TenantId,
            user.Id,
            version,
            cancellationToken);
        return ToIdentityResult(result);
    }

    public async Task<AspNetCoreIdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            UserDocumentId(tenantId, userId),
            cancellationToken);
        return envelope is null ? null : MapUser(envelope);
    }

    public async Task<AspNetCoreIdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        var envelope = await FirstAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityStorageManifest.FindUserByNormalizedNameQuery,
            (IdentityStorageManifest.NormalizedUserNameKeyField, ScopedLookupKey(tenantId, normalizedUserName)!),
            cancellationToken);
        return envelope is null ? null : MapUserInScope(envelope, tenantId);
    }

    public Task SetPasswordHashAsync(AspNetCoreIdentityUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash is not null);

    public Task SetSecurityStampAsync(AspNetCoreIdentityUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.SecurityStamp);

    public Task SetEmailAsync(AspNetCoreIdentityUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(AspNetCoreIdentityUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public async Task<AspNetCoreIdentityUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        var envelopes = await QueryAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityStorageManifest.FindUserByNormalizedEmailQuery,
            (IdentityStorageManifest.NormalizedEmailKeyField, ScopedLookupKey(tenantId, normalizedEmail)!),
            AmbiguousEmailTake,
            cancellationToken);
        return envelopes.Count switch
        {
            0 => null,
            1 => MapUserInScope(envelopes[0], tenantId),
            _ => null
        };
    }

    public Task<string?> GetNormalizedEmailAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(AspNetCoreIdentityUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnd);

    public async Task SetLockoutEndDateAsync(AspNetCoreIdentityUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken) =>
        await MutateLockoutAsync(
            user,
            current =>
            {
                current.LockoutEnd = lockoutEnd;
                return current.AccessFailedCount;
            },
            cancellationToken);

    public async Task<int> IncrementAccessFailedCountAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        await MutateLockoutAsync(
            user,
            current => ++current.AccessFailedCount,
            cancellationToken);

    public async Task ResetAccessFailedCountAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        await MutateLockoutAsync(
            user,
            current =>
            {
                current.AccessFailedCount = 0;
                return current.AccessFailedCount;
            },
            cancellationToken);

    public Task<int> GetAccessFailedCountAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnabled);

    public async Task SetLockoutEnabledAsync(AspNetCoreIdentityUser user, bool enabled, CancellationToken cancellationToken) =>
        await MutateLockoutAsync(
            user,
            current =>
            {
                current.LockoutEnabled = enabled;
                return current.AccessFailedCount;
            },
            cancellationToken);

    public Task SetPhoneNumberAsync(AspNetCoreIdentityUser user, string? phoneNumber, CancellationToken cancellationToken)
    {
        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    public Task<string?> GetPhoneNumberAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PhoneNumber);

    public Task<bool> GetPhoneNumberConfirmedAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PhoneNumberConfirmed);

    public Task SetPhoneNumberConfirmedAsync(AspNetCoreIdentityUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task SetTwoFactorEnabledAsync(AspNetCoreIdentityUser user, bool enabled, CancellationToken cancellationToken)
    {
        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task<bool> GetTwoFactorEnabledAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.TwoFactorEnabled);

    private async Task<DocumentStoreWriteResult> SaveUserAsync(
        AspNetCoreIdentityUser user,
        long? expectedVersion,
        CancellationToken cancellationToken) =>
        (await SaveUserAggregateAsync(user, expectedVersion, cancellationToken)).WriteResult;

    private async Task<GroundworkIdentityAuthorityWriteResult> SaveUserAggregateAsync(
        AspNetCoreIdentityUser user,
        long? expectedVersion,
        CancellationToken cancellationToken,
        string? requestIdentity = null)
    {
        var documentId = UserDocumentId(user.TenantId, user.Id);
        var existingEnvelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            documentId,
            cancellationToken);
        var existingDocument = existingEnvelope is null ? null : Deserialize<IdentityUserDocument>(existingEnvelope);
        var document = new IdentityUserDocument(
            Normalize(user.TenantId),
            Normalize(user.Id),
            Normalize(user.NormalizedUserName),
            Normalize(user.NormalizedEmail),
            ScopedLookupKey(user.TenantId, user.NormalizedUserName),
            ScopedLookupKey(user.TenantId, user.NormalizedEmail),
            MergeUserRecord(existingDocument?.User, user),
            AspNetCoreIdentityAuthorityMapper.ToFrameworkState(user),
            existingDocument?.ClaimIds,
            existingDocument?.LoginIds,
            existingDocument?.RoleLinkIds,
            existingDocument?.TokenIds,
            existingDocument?.TenantMembershipIds);
        var result = await _aggregates.SaveUserAsync(
            document,
            expectedVersion,
            _emailUniquenessPolicy.RequireUniqueEmail,
            cancellationToken,
            requestIdentity);
        ApplyRevisionStamp(user, result.WriteResult);
        return result;
    }

    private async Task<int> MutateLockoutAsync(
        AspNetCoreIdentityUser user,
        Func<AspNetCoreIdentityUser, int> mutate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        accessContextAccessor.Current.EnsureTenantScope(user.TenantId);

        if (!TryExpectedVersion(user, out var expectedVersion))
            return mutate(user);

        var candidate = user;
        // Two independent relative increments can have identical content and expected versions. Give each
        // invocation a stable contender identity so one caller cannot replay another caller's receipt.
        var requestIdentity = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < LockoutTransitionMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 0)
            {
                candidate = await FindByIdAsync(user.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The requested user does not exist in the current persistence scope.");
                if (!TryExpectedVersion(candidate, out expectedVersion))
                    throw new InvalidOperationException("The requested user has no valid Groundwork revision stamp.");
            }

            var value = mutate(candidate);
            var result = (await SaveUserAggregateAsync(
                candidate,
                expectedVersion,
                cancellationToken,
                requestIdentity)).WriteResult;
            if (result.Status is DocumentStoreWriteStatus.Saved)
            {
                CopyLockoutState(candidate, user);
                return value;
            }

            if (result.Status is not DocumentStoreWriteStatus.ConcurrencyConflict)
                throw new InvalidOperationException($"Groundwork Identity lockout transition returned {result.Status}.");
        }

        throw new InvalidOperationException("Groundwork Identity lockout transition exceeded the bounded retry limit.");
    }

    private async Task<IdentityRole?> FindRoleByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From(CurrentTenantId(), roleId),
            cancellationToken);
        return envelope is null ? null : AspNetCoreIdentityAuthorityMapper.ToRole(Deserialize<IdentityRoleDocument>(envelope));
    }

    private async Task<IdentityRole?> FindRoleByNormalizedNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        var envelope = await FirstAsync(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityStorageManifest.FindRoleByNormalizedNameQuery,
            (IdentityStorageManifest.NormalizedRoleNameKeyField, ScopedLookupKey(tenantId, normalizedRoleName)!),
            cancellationToken);
        return envelope is null ? null : AspNetCoreIdentityAuthorityMapper.ToRole(Deserialize<IdentityRoleDocument>(envelope));
    }

    private async Task<HashSet<string>> GetRecoveryCodesAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        var raw = await GetTokenAsync(user, RecoveryCodeTokenProvider, RecoveryCodeTokenName, cancellationToken);
        return (raw ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<DocumentStoreWriteResult> SaveWriteAsync<TDocument>(
        string documentKind,
        string id,
        TDocument document,
        CancellationToken cancellationToken,
        long? expectedVersion)
    {
        var content = JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);
        return await store.SaveAsync(
            new SaveDocumentRequest(documentKind, id, IdentityStorageManifest.SchemaVersion, content, expectedVersion),
            cancellationToken);
    }

    private async Task<DocumentEnvelope?> FirstAsync(
        string documentKind,
        string queryIdentity,
        (string Field, string Value) comparison,
        CancellationToken cancellationToken) =>
        (await QueryAsync(documentKind, queryIdentity, comparison, SingleResultTake, cancellationToken)).FirstOrDefault();

    private async Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
        string documentKind,
        string queryIdentity,
        (string Field, string Value) comparison,
        CancellationToken cancellationToken) =>
        await QueryAllPagesAsync(documentKind, queryIdentity, comparison, null, cancellationToken);

    private async Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
        string documentKind,
        string queryIdentity,
        (string Field, string Value) comparison,
        int take,
        CancellationToken cancellationToken) =>
        await QueryPageAsync(documentKind, queryIdentity, comparison, null, skip: null, take, cancellationToken);

    private async Task EnsureUserExistsAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            UserDocumentId(user.TenantId, user.Id),
            cancellationToken);
        if (envelope is null)
            throw new InvalidOperationException("The requested user does not exist in the current persistence scope.");
    }

    private async Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
        string documentKind,
        string queryIdentity,
        (string Field, string Value) comparison,
        (string Field, string Value)? secondComparison,
        CancellationToken cancellationToken) =>
        await QueryAllPagesAsync(documentKind, queryIdentity, comparison, secondComparison, cancellationToken);

    private async Task<IReadOnlyList<DocumentEnvelope>> QueryAllPagesAsync(
        string documentKind,
        string queryIdentity,
        (string Field, string Value) comparison,
        (string Field, string Value)? secondComparison,
        CancellationToken cancellationToken)
    {
        var documents = new List<DocumentEnvelope>();
        for (var skip = 0; ; skip += RelationshipPageSize)
        {
            var page = await QueryPageAsync(
                documentKind,
                queryIdentity,
                comparison,
                secondComparison,
                skip,
                RelationshipPageSize,
                cancellationToken);
            documents.AddRange(page);
            if (page.Count < RelationshipPageSize)
                return documents;
            if (documents.Count >= MaxRelationshipMaterialization)
                throw new InvalidOperationException(
                    $"Groundwork ASP.NET Core Identity route '{queryIdentity}' exceeded the bounded relationship materialization limit.");
        }
    }

    private async Task<IReadOnlyList<DocumentEnvelope>> QueryPageAsync(
        string documentKind,
        string queryIdentity,
        (string Field, string Value) comparison,
        (string Field, string Value)? secondComparison,
        int? skip,
        int take,
        CancellationToken cancellationToken) =>
        (await BoundedStore.QueryAsync(
            new DocumentQuery(
                documentKind,
                queryIdentity,
                secondComparison is { } second
                    ? [DocumentQueryClause.Of(DocumentQueryComparison.Equal(comparison.Field, comparison.Value)), DocumentQueryClause.Of(DocumentQueryComparison.Equal(second.Field, second.Value))]
                    : [DocumentQueryClause.Of(DocumentQueryComparison.Equal(comparison.Field, comparison.Value))],
                [],
                skip,
                take,
                null,
                null,
                BoundedQueryResultOperation.Documents),
            cancellationToken)).Documents;

    private AspNetCoreIdentityUser? MapUserInScope(DocumentEnvelope envelope, string tenantId)
    {
        var document = Deserialize<IdentityUserDocument>(envelope);
        return string.Equals(document.TenantId, Normalize(tenantId), StringComparison.Ordinal)
            ? MapUser(envelope)
            : null;
    }

    private static AspNetCoreIdentityUser MapUser(DocumentEnvelope envelope)
    {
        var user = AspNetCoreIdentityAuthorityMapper.ToUser(Deserialize<IdentityUserDocument>(envelope));
        user.ConcurrencyStamp = IdentityRevisionStamp.FromUser(user.TenantId, user.Id, envelope.Version).ToString();
        return user;
    }

    private static TDocument Deserialize<TDocument>(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<TDocument>(envelope.ContentJson, IdentityGroundworkJson.Options)!;

    private static IdentityResult ToIdentityResult(DocumentStoreWriteResult result) =>
        result.Status switch
        {
            DocumentStoreWriteStatus.Saved or DocumentStoreWriteStatus.Deleted => IdentityResult.Success,
            DocumentStoreWriteStatus.ConcurrencyConflict => IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure()),
            _ => IdentityResult.Failed(new IdentityError
            {
                Code = $"GroundworkIdentity{result.Status}",
                Description = $"Groundwork Identity write returned {result.Status}."
            })
        };

    private static IdentityResult ToIdentityResult(
        AspNetCoreIdentityUser user,
        GroundworkIdentityAuthorityWriteResult result) =>
        result.Conflict switch
        {
            GroundworkIdentityAuthorityConflict.UserName => IdentityResult.Failed(
                new IdentityErrorDescriber().DuplicateUserName(user.NormalizedUserName ?? user.UserName ?? user.Id)),
            GroundworkIdentityAuthorityConflict.Email => DuplicateEmail(user),
            _ => ToIdentityResult(result.WriteResult)
        };

    private static bool TryExpectedVersion(AspNetCoreIdentityUser user, out long? expectedVersion)
    {
        if (IdentityRevisionStamp.TryGetUserVersion(user.ConcurrencyStamp, user.TenantId, user.Id, out var version))
        {
            expectedVersion = version;
            return true;
        }

        expectedVersion = null;
        return false;
    }

    private static void ApplyRevisionStamp(AspNetCoreIdentityUser user, DocumentStoreWriteResult result)
    {
        if (result.Status == DocumentStoreWriteStatus.Saved && result.Document is not null)
            user.ConcurrencyStamp = IdentityRevisionStamp.FromUser(user.TenantId, user.Id, result.Document.Version).ToString();
    }

    private static void ApplyRevisionStamp(AspNetCoreIdentityUser user, DocumentEnvelope envelope) =>
        user.ConcurrencyStamp = IdentityRevisionStamp.FromUser(user.TenantId, user.Id, envelope.Version).ToString();

    private static DocumentEnvelope RequireSavedEnvelope(DocumentStoreWriteResult result, string operation)
    {
        if (result.Status is DocumentStoreWriteStatus.Saved && result.Document is not null)
            return result.Document;

        throw new InvalidOperationException($"Groundwork Identity {operation} returned {result.Status}.");
    }

    private static long RequireExpectedVersion(AspNetCoreIdentityUser user)
    {
        if (TryExpectedVersion(user, out var expectedVersion) && expectedVersion is { } version)
            return version;

        throw new InvalidOperationException("The requested user has no valid Groundwork revision stamp.");
    }

    private static IdentityResult ConcurrencyFailure() =>
        IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());

    private static IdentityResult DuplicateEmail(AspNetCoreIdentityUser user) =>
        IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(user.NormalizedEmail ?? user.Email ?? user.Id));

    private static void CopyLockoutState(AspNetCoreIdentityUser source, AspNetCoreIdentityUser target)
    {
        target.AccessFailedCount = source.AccessFailedCount;
        target.LockoutEnd = source.LockoutEnd;
        target.LockoutEnabled = source.LockoutEnabled;
        target.ConcurrencyStamp = source.ConcurrencyStamp;
    }

    private static UserRecord MergeUserRecord(UserRecord? existing, AspNetCoreIdentityUser user)
    {
        var next = AspNetCoreIdentityAuthorityMapper.ToUserRecord(user);
        return existing is null
            ? next
            : existing with
            {
                UserName = next.UserName,
                Email = next.Email,
                DisplayName = next.DisplayName
            };
    }

    private string CurrentTenantId() =>
        accessContextAccessor.Current.Scope?.Value ?? PersistenceScope.DefaultValue;

    private void EnsureUserScope(AspNetCoreIdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        accessContextAccessor.Current.EnsureTenantScope(user.TenantId);
    }

    private static string UserLookupKey(string tenantId, string userId) =>
        IdentityDocumentId.From(tenantId, userId);

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("Groundwork ASP.NET Core Identity queries require a bounded document-store runtime.");

    private static string UserDocumentId(string tenantId, string userId) =>
        IdentityCompositeDocumentId.From(tenantId, userId);

    private static string TokenDocumentId(string tenantId, string userId, string loginProvider, string name) =>
        IdentityDocumentId.From(tenantId, userId, loginProvider, name);

    private static string? ScopedLookupKey(string tenantId, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : IdentityDocumentId.From(tenantId, value);

    private static string Normalize(string? value) => IdentityCompositeDocumentId.Normalize(value);
}
