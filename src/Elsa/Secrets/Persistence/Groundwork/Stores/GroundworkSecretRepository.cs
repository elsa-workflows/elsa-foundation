using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Secrets.Persistence.Groundwork.Stores;

public sealed class GroundworkSecretRepository(
    IGroundworkStorageSessionSource sessions,
    string? targetName = null) : ISecretRepository, IRevisionAwareSecretRepository, IPagedSecretRepository
{
    public ValueTask<Secret?> FindAsync(
        string tenantId,
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIdentity(tenantId, normalizedName);
        var entry = Session(tenantId).Read(Key(tenantId, normalizedName));
        return ValueTask.FromResult(entry is null ? null : Map(entry.Values.Values, tenantId));
    }

    public ValueTask<SecretRevisionedRecord?> FindWithRevisionAsync(
        string tenantId,
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIdentity(tenantId, normalizedName);
        var entry = Session(tenantId).Read(Key(tenantId, normalizedName));
        if (entry is null)
            return ValueTask.FromResult<SecretRevisionedRecord?>(null);
        if (entry.Version is not { } version)
            throw new InvalidOperationException("The Groundwork secret row has no optimistic revision.");
        return ValueTask.FromResult<SecretRevisionedRecord?>(new(
            Map(entry.Values.Values, tenantId),
            SecretRevisionMapper.Revision(version)));
    }

    public ValueTask<SecretRepositoryPage> ListPageAsync(
        string tenantId,
        SecretRepositoryListRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTenantId(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        if (IsContradictory(request))
            return ValueTask.FromResult(new SecretRepositoryPage([], 0));

        var result = Session(tenantId).Query(ListQuery(tenantId, request));
        var items = result.Rows.Select(row => Map(row, tenantId)).ToArray();
        return ValueTask.FromResult(new SecretRepositoryPage(
            items,
            result.TotalCount ?? throw new InvalidOperationException("Groundwork did not return the requested secret total count.")));
    }

    public ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = SaveCore(secret, WriteOptions.CreateOnly);
        return ValueTask.FromResult(result.Status == WriteOutcomeStatus.Inserted);
    }

    public ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = SaveCore(secret, WriteOptions.Unconditional);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not save secret '{secret.Name}'; Groundwork returned {result.Status}.");
        return ValueTask.CompletedTask;
    }

    public ValueTask<SecretRevisionSaveResult> SaveWithRevisionAsync(
        Secret secret,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SecretRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return ValueTask.FromResult(SecretRevisionMapper.InvalidRevision());

        var options = expectedVersion == 0
            ? WriteOptions.CreateOnly
            : WriteOptions.IfVersion(expectedVersion!.Value);
        return ValueTask.FromResult(SecretRevisionMapper.ToResult(SaveCore(secret, options)));
    }

    private WriteOutcome SaveCore(Secret secret, WriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ValidateIdentity(secret.TenantId, secret.Name);
        var values = Values(SecretDocument.FromSecret(secret));
        var session = Session(secret.TenantId);
        if (options.Precondition.Kind == WritePreconditionKind.Unconditional)
            return session.Upsert(values, options);
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not support the conditional writes required by Secrets.");
        }
        return concurrency.ConditionalUpsert(values, options);
    }

    private IStorageSession Session(string tenantId) =>
        sessions.Open(
            SecretsGroundworkStorageSchema.UnitId,
            StorageAccess.Scoped(new StorageScope(tenantId)),
            targetName);

    private static StorageKey Key(string tenantId, string normalizedName) => new(new Dictionary<string, object?>
    {
        [SecretsGroundworkStorageSchema.TenantIdField] = tenantId,
        [SecretsGroundworkStorageSchema.NormalizedNameField] = normalizedName
    });

    private static StorageValues Values(SecretDocument document) => new(new Dictionary<string, object?>
    {
        [SecretsGroundworkStorageSchema.TenantIdField] = document.TenantId,
        [SecretsGroundworkStorageSchema.NormalizedNameField] = document.NormalizedName,
        [SecretsGroundworkStorageSchema.NameSearchKeyField] = document.NameSearchKey,
        [SecretsGroundworkStorageSchema.DisplayNameSearchKeyField] = document.DisplayNameSearchKey,
        [SecretsGroundworkStorageSchema.TypeNameLookupKeyField] = document.TypeNameLookupKey,
        [SecretsGroundworkStorageSchema.StoreNameLookupKeyField] = document.StoreNameLookupKey,
        [SecretsGroundworkStorageSchema.ScopeLookupKeyField] = document.ScopeLookupKey,
        [SecretsGroundworkStorageSchema.StatusField] = document.Status,
        [SecretsGroundworkStorageSchema.HasNonExpiringActiveVersionField] = document.HasNonExpiringActiveVersion,
        [SecretsGroundworkStorageSchema.MaxActiveVersionExpiresAtField] = document.MaxActiveVersionExpiresAt,
        [SecretsGroundworkStorageSchema.PayloadField] = JsonSerializer.Serialize(document, SecretsGroundworkJson.Options)
    });

    private static QueryRequest ListQuery(string tenantId, SecretRepositoryListRequest request)
    {
        var predicates = new List<Predicate> { Equal(Columns.TenantId, tenantId) };
        if (request.Search is not null)
        {
            var searchKey = SearchKey(request.Search);
            predicates.Add(new Predicate.Or([
                new Predicate.Substring(Columns.NameSearchKey, searchKey, Anchor.Contains),
                new Predicate.Substring(Columns.DisplayNameSearchKey, searchKey, Anchor.Contains)
            ]));
        }
        if (request.TypeName is not null)
            predicates.Add(Equal(Columns.TypeNameLookupKey, LookupKey(request.TypeName)));
        if (request.TypeNames.Count > 0)
            predicates.Add(In(Columns.TypeNameLookupKey, request.TypeNames.Select(LookupKey)));
        if (request.StoreName is not null)
            predicates.Add(Equal(Columns.StoreNameLookupKey, LookupKey(request.StoreName)));
        if (request.StoreNames.Count > 0)
            predicates.Add(In(Columns.StoreNameLookupKey, request.StoreNames.Select(LookupKey)));
        if (request.Scope is not null)
            predicates.Add(Equal(Columns.ScopeLookupKey, LookupKey(request.Scope)));

        if (request.ActiveOnly)
        {
            predicates.Add(Equal(Columns.Status, StatusValue(SecretStatus.Active)));
            predicates.Add(new Predicate.Or([
                Equal(Columns.HasNonExpiringActiveVersion, true),
                new Predicate.Range(
                    Columns.MaxActiveVersionExpiresAt,
                    Bound.Exclusive(QueryConstant.Of(Columns.MaxActiveVersionExpiresAt, request.Now!.Value)),
                    null)
            ]));
        }
        else if (request.Status is not null)
        {
            predicates.Add(Equal(Columns.Status, StatusValue(request.Status.Value)));
        }

        if (request.ExcludedStatus is not null && !request.ActiveOnly && request.Status is null)
            predicates.Add(new Predicate.Not(Equal(Columns.Status, StatusValue(request.ExcludedStatus.Value))));

        return new QueryRequest(
            new TableId(SecretsGroundworkStorageSchema.UnitName),
            new Predicate.And(predicates),
            [new OrderTerm(Columns.NormalizedName, OrderDirection.Ascending, NullOrder.First)],
            Projection.All,
            Paging.OffsetLimit(request.Skip, request.Take),
            ResultShape.TotalCount.Instance,
            acceptedScan: request.Search is null ? null : SearchScanAcceptance);
    }

    private static readonly ScanAcceptance SearchScanAcceptance = ScanAcceptance.Allow(
        "GW-SCAN-ELSA-SECRETS-SUBSTRING",
        "Portable case-insensitive substring search has no cross-provider index shape; the API bounds each page to 250 rows.",
        "elsa-secrets",
        new DateTimeOffset(2027, 8, 16, 0, 0, 0, TimeSpan.Zero));

    private static Predicate Equal(ColumnRef column, object? value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Predicate In(ColumnRef column, IEnumerable<string> values) =>
        new Predicate.In(column, values.Select(value => QueryConstant.Of(column, value)));

    private static Secret Map(IReadOnlyDictionary<string, object?> row, string tenantId)
    {
        if (!row.TryGetValue(SecretsGroundworkStorageSchema.PayloadField, out var payload))
            throw new InvalidOperationException("The Groundwork secret payload is missing.");
        var json = payload switch
        {
            string text => text,
            JsonElement element => element.GetRawText(),
            JsonDocument jsonDocument => jsonDocument.RootElement.GetRawText(),
            _ => throw new InvalidOperationException("The Groundwork secret payload is invalid.")
        };
        var document = Deserialize(json);
        if (!string.Equals(document.TenantId, document.Secret.TenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Secret document contains conflicting tenant identities.");
        if (!string.Equals(document.TenantId, tenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Secret document tenant does not match its storage identity.");
        return document.Secret;
    }

    private static bool IsContradictory(SecretRepositoryListRequest request) =>
        request.Status is not null && request.Status == request.ExcludedStatus ||
        request.ActiveOnly &&
        (request.Status is not null && request.Status != SecretStatus.Active ||
         request.ExcludedStatus == SecretStatus.Active);

    private static string StatusValue(SecretStatus status) => status.ToString().ToLowerInvariant();

    private static string SearchKey(string value) =>
        PortableStringComparison.CreateSearchKey(value, PortableStringComparisonPolicy.UnicodeOrdinalIgnoreCase);

    private static string LookupKey(string value) =>
        PortableStringComparison.ProjectIdentity(
            value,
            PortableStringComparisonPolicy.UnicodeOrdinalIgnoreCase).LookupKey;

    private static void ValidateIdentity(string tenantId, string normalizedName)
    {
        ValidateTenantId(tenantId);
        SecretNameConstraints.Validate(normalizedName);
    }

    private static void ValidateTenantId(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (tenantId.Length > 256)
            throw new ArgumentException("A secret tenant ID cannot exceed 256 characters.", nameof(tenantId));
    }

    internal static SecretDocument Deserialize(string contentJson) =>
        JsonSerializer.Deserialize<SecretDocument>(contentJson, SecretsGroundworkJson.Options)
        ?? throw new InvalidOperationException("Secret document content is invalid.");

    internal sealed record SecretDocument(
        string TenantId,
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

    private static class Columns
    {
        private static readonly TableId Table = new(SecretsGroundworkStorageSchema.UnitName);
        internal static ColumnRef TenantId { get; } = String(SecretsGroundworkStorageSchema.TenantIdField, false, 256);
        internal static ColumnRef NormalizedName { get; } = String(SecretsGroundworkStorageSchema.NormalizedNameField, false, SecretNameConstraints.MaximumLength);
        internal static ColumnRef NameSearchKey { get; } = String(SecretsGroundworkStorageSchema.NameSearchKeyField, false);
        internal static ColumnRef DisplayNameSearchKey { get; } = String(SecretsGroundworkStorageSchema.DisplayNameSearchKeyField, false);
        internal static ColumnRef TypeNameLookupKey { get; } = String(SecretsGroundworkStorageSchema.TypeNameLookupKeyField, false, 64);
        internal static ColumnRef StoreNameLookupKey { get; } = String(SecretsGroundworkStorageSchema.StoreNameLookupKeyField, false, 64);
        internal static ColumnRef ScopeLookupKey { get; } = String(SecretsGroundworkStorageSchema.ScopeLookupKeyField, true, 64);
        internal static ColumnRef Status { get; } = String(SecretsGroundworkStorageSchema.StatusField, false, 32);
        internal static ColumnRef HasNonExpiringActiveVersion { get; } = new(Table, SecretsGroundworkStorageSchema.HasNonExpiringActiveVersionField, QueryType.Boolean, false);
        internal static ColumnRef MaxActiveVersionExpiresAt { get; } = new(Table, SecretsGroundworkStorageSchema.MaxActiveVersionExpiresAtField, QueryType.DateTimeOffset, true);

        private static ColumnRef String(string name, bool nullable, int? maxLength = null) =>
            new(Table, name, QueryType.String, nullable, maxLength);
    }
}
