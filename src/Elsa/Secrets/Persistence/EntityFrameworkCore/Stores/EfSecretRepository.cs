using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

public sealed class EfSecretRepository(SecretsDbContext context) : ISecretRepository, IRevisionAwareSecretRepository, IPagedSecretRepository
{
    private const int MaximumSubstringSearchCatalogRows = 10_000;
    private const int MaximumUnconditionalSaveAttempts = 3;

    public async ValueTask<Secret?> FindAsync(
        string tenantId,
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIdentity(tenantId, normalizedName);
        var record = await context.Secrets.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.TenantId == tenantId && row.NormalizedName == normalizedName,
                cancellationToken);
        return record is null ? null : Map(record, tenantId);
    }

    public async ValueTask<SecretRevisionedRecord?> FindWithRevisionAsync(
        string tenantId,
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIdentity(tenantId, normalizedName);
        var record = await context.Secrets.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.TenantId == tenantId && row.NormalizedName == normalizedName,
                cancellationToken);
        if (record is null)
            return null;
        if (record.ConcurrencyToken is not { Length: 16 })
            throw new InvalidOperationException("The EF secret row has no optimistic concurrency token.");
        return new SecretRevisionedRecord(Map(record, tenantId), SecretRevisionMapper.Revision(record.ConcurrencyToken));
    }

    public async ValueTask<SecretRepositoryPage> ListPageAsync(
        string tenantId,
        SecretRepositoryListRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTenantId(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        if (IsContradictory(request))
            return new SecretRepositoryPage([], 0);

        if (request.Search is not null)
            await EnsureSubstringSearchCatalogBound(tenantId, cancellationToken);

        var query = ListQuery(tenantId, request);
        var totalCount = await query.LongCountAsync(cancellationToken);
        var records = await query
            .OrderBy(row => row.NormalizedName)
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(cancellationToken);
        var items = records.Select(record => Map(record, tenantId)).ToArray();
        return new SecretRepositoryPage(items, totalCount);
    }

    public async ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(secret);
        ValidateIdentity(secret.TenantId, secret.Name);
        var exists = await context.Secrets.AnyAsync(
            row => row.TenantId == secret.TenantId && row.NormalizedName == secret.Name,
            cancellationToken);
        if (exists)
            return false;

        context.Secrets.Add(SecretDocument.FromSecret(secret).ToRecord());
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return true;
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            context.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException($"Could not add secret '{secret.Name}'.", exception);
        }
    }

    public async ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(secret);
        ValidateIdentity(secret.TenantId, secret.Name);
        var document = SecretDocument.FromSecret(secret);
        for (var attempt = 0; attempt < MaximumUnconditionalSaveAttempts; attempt++)
        {
            var existing = await context.Secrets.FindAsync([secret.TenantId, secret.Name], cancellationToken);
            if (existing is null)
                context.Secrets.Add(document.ToRecord());
            else
                document.CopyProjectionsTo(existing);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt + 1 < MaximumUnconditionalSaveAttempts)
            {
                // SaveAsync is an unconditional last-write-wins operation. Refresh after another
                // writer wins the optimistic race, then apply this caller's complete document.
                context.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException exception)
            {
                context.ChangeTracker.Clear();
                throw new InvalidOperationException($"Could not save secret '{secret.Name}' after {MaximumUnconditionalSaveAttempts} attempts.", exception);
            }
            catch (DbUpdateException exception)
                when (attempt + 1 < MaximumUnconditionalSaveAttempts && EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
            {
                // A concurrent creator may win between FindAsync and INSERT. Refresh and turn the
                // operation into an update on the next attempt rather than reporting a conflict.
                context.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception)
            {
                context.ChangeTracker.Clear();
                throw new InvalidOperationException($"Could not save secret '{secret.Name}'.", exception);
            }
        }

        throw new InvalidOperationException($"Could not save secret '{secret.Name}'.");
    }

    public async ValueTask<SecretRevisionSaveResult> SaveWithRevisionAsync(
        Secret secret,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(secret);
        if (!SecretRevisionMapper.TryParse(expectedRevision, out var expectedToken))
            return SecretRevisionMapper.InvalidRevision();

        ValidateIdentity(secret.TenantId, secret.Name);
        var document = SecretDocument.FromSecret(secret);
        var existing = await context.Secrets.FindAsync([secret.TenantId, secret.Name], cancellationToken);

        if (expectedToken is null)
        {
            if (existing is not null)
                return new SecretRevisionSaveResult(SecretRevisionSaveStatus.Conflict);
            context.Secrets.Add(document.ToRecord());
        }
        else if (existing is null)
        {
            return new SecretRevisionSaveResult(SecretRevisionSaveStatus.NotFound);
        }
        else if (!SecretRevisionMapper.SameToken(existing.ConcurrencyToken, expectedToken))
        {
            return new SecretRevisionSaveResult(SecretRevisionSaveStatus.Conflict);
        }
        else
        {
            document.CopyProjectionsTo(existing);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return new SecretRevisionSaveResult(SecretRevisionSaveStatus.Conflict);
        }
        catch (DbUpdateException exception)
            when (expectedToken is null && EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            context.ChangeTracker.Clear();
            return new SecretRevisionSaveResult(SecretRevisionSaveStatus.Conflict);
        }
        catch (DbUpdateException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException($"Could not save secret '{secret.Name}'.", exception);
        }

        var saved = existing ?? context.Secrets.Local.Single(row =>
            row.TenantId == secret.TenantId && row.NormalizedName == secret.Name);
        return new SecretRevisionSaveResult(
            SecretRevisionSaveStatus.Saved,
            SecretRevisionMapper.Revision(saved.ConcurrencyToken));
    }

    private IQueryable<SecretRecord> ListQuery(string tenantId, SecretRepositoryListRequest request)
    {
        var query = context.Secrets.AsNoTracking().Where(row => row.TenantId == tenantId);
        if (request.Search is not null)
        {
            var searchKey = SecretsSearchKeys.SearchKey(request.Search);
            query = query.Where(row =>
                row.NameSearchKey.Contains(searchKey) ||
                row.DisplayNameSearchKey.Contains(searchKey));
        }

        if (request.TypeName is not null)
        {
            var typeKey = SecretsSearchKeys.LookupKey(request.TypeName);
            query = query.Where(row => row.TypeNameLookupKey == typeKey);
        }

        if (request.TypeNames.Count > 0)
        {
            var typeKeys = request.TypeNames.Select(SecretsSearchKeys.LookupKey).ToArray();
            query = query.Where(row => typeKeys.Contains(row.TypeNameLookupKey));
        }

        if (request.StoreName is not null)
        {
            var storeKey = SecretsSearchKeys.LookupKey(request.StoreName);
            query = query.Where(row => row.StoreNameLookupKey == storeKey);
        }

        if (request.StoreNames.Count > 0)
        {
            var storeKeys = request.StoreNames.Select(SecretsSearchKeys.LookupKey).ToArray();
            query = query.Where(row => storeKeys.Contains(row.StoreNameLookupKey));
        }

        if (request.Scope is not null)
        {
            var scopeKey = SecretsSearchKeys.LookupKey(request.Scope);
            query = query.Where(row => row.ScopeLookupKey == scopeKey);
        }

        if (request.ActiveOnly)
        {
            var now = request.Now!.Value.ToUniversalTime();
            var active = SecretsSearchKeys.StatusValue(SecretStatus.Active);
            // Split predicates so EF can translate the nullable DateTimeOffset comparison
            // (Sqlite stores it as ticks; SqlServer/Npgsql keep native types).
            query = query.Where(row => row.Status == active);
            query = query.Where(row =>
                row.HasNonExpiringActiveVersion ||
                (row.MaxActiveVersionExpiresAt != null && row.MaxActiveVersionExpiresAt > now));
        }
        else if (request.Status is not null)
        {
            var status = SecretsSearchKeys.StatusValue(request.Status.Value);
            query = query.Where(row => row.Status == status);
        }

        if (request.ExcludedStatus is not null && !request.ActiveOnly && request.Status is null)
        {
            var excluded = SecretsSearchKeys.StatusValue(request.ExcludedStatus.Value);
            query = query.Where(row => row.Status != excluded);
        }

        return query;
    }

    private async Task EnsureSubstringSearchCatalogBound(string tenantId, CancellationToken cancellationToken)
    {
        var count = await context.Secrets.AsNoTracking()
            .CountAsync(row => row.TenantId == tenantId, cancellationToken);
        if (count > MaximumSubstringSearchCatalogRows)
        {
            throw new InvalidOperationException(
                "Secret substring search is limited to scoped catalogs containing at most 10,000 rows; narrow the storage scope before searching.");
        }
    }

    private static Secret Map(SecretRecord record, string tenantId)
    {
        var document = SecretDocument.Parse(record.Payload);
        if (!string.Equals(document.TenantId, document.Secret.TenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Secret document contains conflicting tenant identities.");
        if (!string.Equals(document.TenantId, tenantId, StringComparison.Ordinal) ||
            !string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Secret document tenant does not match its storage identity.");
        return document.Secret;
    }

    private static bool IsContradictory(SecretRepositoryListRequest request) =>
        request.Status is not null && request.Status == request.ExcludedStatus ||
        request.ActiveOnly &&
        (request.Status is not null && request.Status != SecretStatus.Active ||
         request.ExcludedStatus == SecretStatus.Active);

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
}
