using System.Text.Json;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Groundwork.Core.Text;
using Groundwork.Documents.Store;

namespace Elsa.Secrets.Persistence.Groundwork.Stores;

public sealed class GroundworkSecretRepository(
    IDocumentStore store,
    IBoundedDocumentStore? boundedStore = null) : ISecretRepository, IRevisionAwareSecretRepository, IPagedSecretRepository
{
    private IBoundedDocumentStore BoundedStore => boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
        "Secret queries require an admitted bounded document-store runtime.");

    public async ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        SecretNameConstraints.Validate(normalizedName);

        var envelope = await store.LoadAsync(
            SecretsStorageManifest.SecretDocumentKind,
            DocumentId(tenantId, normalizedName),
            cancellationToken);

        return envelope is null ? null : Map(envelope, tenantId);
    }

    public async ValueTask<SecretRevisionedRecord?> FindWithRevisionAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        SecretNameConstraints.Validate(normalizedName);

        var envelope = await store.LoadAsync(
            SecretsStorageManifest.SecretDocumentKind,
            DocumentId(tenantId, normalizedName),
            cancellationToken);

        return envelope is null ? null : new SecretRevisionedRecord(Map(envelope, tenantId), SecretRevisionMapper.Revision(envelope));
    }

    public async ValueTask<SecretRepositoryPage> ListPageAsync(string tenantId, SecretRepositoryListRequest request, CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        if (IsContradictory(request))
            return new SecretRepositoryPage([], 0);

        var result = await BoundedStore.QueryAsync(ListQuery(tenantId, request), cancellationToken);
        return new SecretRepositoryPage(result.Documents.Select(document => Map(document, tenantId)).ToArray(), result.TotalCount);
    }

    public async ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var result = await SaveCoreAsync(secret, expectedVersion: 0, cancellationToken);
        return result.Status is DocumentStoreWriteStatus.Saved;
    }

    public async ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        var result = await SaveCoreAsync(secret, expectedVersion: null, cancellationToken);
        if (result.Status is not DocumentStoreWriteStatus.Saved)
            throw new InvalidOperationException($"Could not save secret '{secret.Name}'; Groundwork returned {result.Status}.");
    }

    public async ValueTask<SecretRevisionSaveResult> SaveWithRevisionAsync(Secret secret, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!SecretRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return SecretRevisionMapper.InvalidRevision();

        var result = await SaveCoreAsync(secret, expectedVersion, cancellationToken);
        return SecretRevisionMapper.ToResult(result);
    }

    private static DocumentQuery ListQuery(string tenantId, SecretRepositoryListRequest request)
    {
        var clauses = new List<DocumentQueryClause> { Equal(SecretsStorageManifest.TenantIdField, tenantId) };
        if (request.Search is not null)
        {
            var searchKey = SearchKey(request.Search);
            clauses.Add(DocumentQueryClause.AnyOf(
                DocumentQueryComparison.Contains(
                    SecretsStorageManifest.NameSearchKeyField,
                    searchKey),
                DocumentQueryComparison.Contains(
                    SecretsStorageManifest.DisplayNameSearchKeyField,
                    searchKey)));
        }
        if (request.TypeName is not null)
        {
            clauses.Add(Equal(
                SecretsStorageManifest.TypeNameLookupKeyField,
                LookupKey(request.TypeName)));
        }
        if (request.TypeNames.Count > 0)
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.In(
                SecretsStorageManifest.TypeNameLookupKeyField,
                request.TypeNames.Select(LookupKey))));
        }
        if (request.StoreName is not null)
        {
            clauses.Add(Equal(
                SecretsStorageManifest.StoreNameLookupKeyField,
                LookupKey(request.StoreName)));
        }
        if (request.StoreNames.Count > 0)
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.In(
                SecretsStorageManifest.StoreNameLookupKeyField,
                request.StoreNames.Select(LookupKey))));
        }
        if (request.Scope is not null)
        {
            clauses.Add(Equal(
                SecretsStorageManifest.ScopeLookupKeyField,
                LookupKey(request.Scope)));
        }

        if (request.ActiveOnly)
        {
            clauses.Add(Status(SecretStatus.Active));
            clauses.Add(DocumentQueryClause.AnyOf(
                DocumentQueryComparison.Equal(
                    SecretsStorageManifest.HasNonExpiringActiveVersionField,
                    bool.TrueString.ToLowerInvariant()),
                DocumentQueryComparison.GreaterThan(
                    SecretsStorageManifest.MaxActiveVersionExpiresAtField,
                    request.Now!.Value.ToString("O"))));
        }
        else if (request.Status is not null)
        {
            clauses.Add(Status(request.Status.Value));
        }
        else
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.NotEqual(
                SecretsStorageManifest.StatusField,
                null)));
        }

        if (request.ExcludedStatus is not null &&
            !request.ActiveOnly &&
            request.Status is null)
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.NotEqual(
                SecretsStorageManifest.StatusField,
                StatusValue(request.ExcludedStatus.Value))));
        }

        return new DocumentQuery(
            SecretsStorageManifest.SecretDocumentKind,
            request.Search is not null
                ? SecretsStorageManifest.SearchFilteredQuery
                : request.ActiveOnly || request.Status is not null
                    ? SecretsStorageManifest.ListFilteredQuery
                    : SecretsStorageManifest.ListUnfilteredQuery,
            clauses,
            [new DocumentQueryOrder(SecretsStorageManifest.NormalizedNameField)],
            skip: request.Skip,
            take: request.Take);
    }

    private async ValueTask<DocumentStoreWriteResult> SaveCoreAsync(Secret secret, long? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ValidateTenantId(secret.TenantId);
        SecretNameConstraints.Validate(secret.Name);

        var document = SecretDocument.FromSecret(secret);
        var content = JsonSerializer.Serialize(document, SecretsGroundworkJson.Options);

        return await store.SaveAsync(
            new SaveDocumentRequest(
                SecretsStorageManifest.SecretDocumentKind,
                DocumentId(secret.TenantId, secret.Name),
                SecretsStorageManifest.SchemaVersion,
                content,
                expectedVersion),
            cancellationToken);
    }

    internal static Secret Map(DocumentEnvelope envelope, string tenantId)
    {
        var document = Deserialize(envelope.ContentJson);
        if (!string.IsNullOrWhiteSpace(document.TenantId) &&
            !string.IsNullOrWhiteSpace(document.Secret.TenantId) &&
            !string.Equals(document.TenantId, document.Secret.TenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Secret document contains conflicting tenant identities.");
        var persistedTenant = string.IsNullOrWhiteSpace(document.TenantId)
            ? document.Secret.TenantId
            : document.TenantId;
        if (!string.Equals(persistedTenant, tenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Secret document tenant does not match its storage identity.");
        document.Secret.TenantId = tenantId;
        return document.Secret;
    }

    private static bool IsContradictory(SecretRepositoryListRequest request) =>
        request.Status is not null && request.Status == request.ExcludedStatus ||
        request.ActiveOnly &&
        (request.Status is not null && request.Status != SecretStatus.Active ||
         request.ExcludedStatus == SecretStatus.Active);

    private static DocumentQueryClause Equal(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value));

    private static DocumentQueryClause Status(SecretStatus status) =>
        Equal(SecretsStorageManifest.StatusField, StatusValue(status));

    private static string StatusValue(SecretStatus status) =>
        status.ToString().ToLowerInvariant();

    private static string SearchKey(string value) =>
        PortableStringComparison.CreateSearchKey(
            value,
            PortableStringComparisonPolicy.UnicodeOrdinalIgnoreCase);

    private static string LookupKey(string value) =>
        PortableStringComparison.ProjectIdentity(
            value,
            PortableStringComparisonPolicy.UnicodeOrdinalIgnoreCase).LookupKey;

    internal static string DocumentId(string tenantId, string name) => $"{tenantId.Length}:{tenantId}{name}";

    private static void ValidateTenantId(string tenantId) => ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

    internal static SecretDocument Deserialize(string contentJson) =>
        JsonSerializer.Deserialize<SecretDocument>(contentJson, SecretsGroundworkJson.Options)
        ?? throw new InvalidOperationException("Secret document content is invalid.");

    internal sealed record SecretDocument(
        string Collection,
        string? TenantId,
        string NormalizedName,
        string NameSearchKey,
        string DisplayNameSearchKey,
        string TypeNameLookupKey,
        string StoreNameLookupKey,
        string? ScopeLookupKey,
        string Status,
        bool HasNonExpiringActiveVersion,
        DateTimeOffset? MaxActiveVersionExpiresAt,
        Secret Secret)
    {
        public static SecretDocument FromSecret(Secret secret)
        {
            var activeVersions = secret.Versions
                .Where(version => version.Status == SecretStatus.Active)
                .ToArray();
            return new SecretDocument(
                SecretsStorageManifest.SecretCollection,
                secret.TenantId,
                secret.Name,
                SearchKey(secret.Name),
                SearchKey(secret.DisplayName),
                LookupKey(secret.TypeName),
                LookupKey(secret.StoreName),
                secret.Scope is null ? null : LookupKey(secret.Scope),
                StatusValue(secret.Status),
                activeVersions.Any(version => version.ExpiresAt is null),
                activeVersions
                    .Where(version => version.ExpiresAt is not null)
                    .Select(version => version.ExpiresAt)
                    .Max(),
                secret);
        }
    }
}
