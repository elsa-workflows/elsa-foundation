using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Audits and repairs projection fields written before the persisted Unicode algorithm was pinned.
/// Reindexing is an explicit operator action; ordinary startup only validates and fails closed.
/// </summary>
public static class SecretsProjectionContract
{
    private const int BatchSize = 100;

    public static async Task EnsureCurrentAsync(
        SecretsDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        for (var offset = 0;; offset += BatchSize)
        {
            var records = await Ordered(context.Secrets.AsNoTracking())
                .Skip(offset)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (records.Any(record => !IsCurrent(record)))
            {
                throw new InvalidOperationException(
                    "One or more Secrets rows do not use persisted projection " +
                    $"'{SecretsSearchKeys.UnicodeOrdinalIgnoreCaseAlgorithmId}'. " +
                    "Quiesce writers and run the provider-specific 'bash tools/ef/dual-migrate.sh apply' " +
                    "command to reindex legacy rows before starting the host.");
            }

            if (records.Count < BatchSize)
                return;
        }
    }

    public static async Task<int> ReindexAsync(
        SecretsDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var offset = 0;
        var updatedTotal = 0;
        while (true)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var records = await Ordered(context.Secrets)
                .Skip(offset)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            var updatedBatch = 0;

            foreach (var record in records)
            {
                var (stored, current) = ReadDocuments(record);
                if (Matches(record, stored, current))
                    continue;

                current.CopyProjectionsTo(record);
                updatedBatch++;
            }

            if (updatedBatch > 0)
                await context.SaveProjectionChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            updatedTotal += updatedBatch;
            offset += records.Count;
            context.ChangeTracker.Clear();

            if (records.Count < BatchSize)
                return updatedTotal;
        }
    }

    private static IOrderedQueryable<SecretRecord> Ordered(IQueryable<SecretRecord> records) =>
        records.OrderBy(record => record.TenantId).ThenBy(record => record.NormalizedName);

    private static bool IsCurrent(SecretRecord record)
    {
        var (stored, current) = ReadDocuments(record);
        return Matches(record, stored, current);
    }

    private static (SecretDocument Stored, SecretDocument Current) ReadDocuments(SecretRecord record)
    {
        var stored = SecretDocument.Parse(record.Payload);
        var current = SecretDocument.FromSecret(stored.Secret);
        if (!string.Equals(current.TenantId, record.TenantId, StringComparison.Ordinal) ||
            !string.Equals(current.NormalizedName, record.NormalizedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A Secrets row identity does not match its stored document; the current projection-reindex batch was rolled back. " +
                "Correct the inconsistent row and rerun the idempotent operator command.");
        }

        return (stored, current);
    }

    private static bool Matches(SecretRecord record, SecretDocument stored, SecretDocument current) =>
        SameProjections(record, current) && SameProjections(stored, current);

    private static bool SameProjections(SecretRecord record, SecretDocument current) =>
        string.Equals(record.NameSearchKey, current.NameSearchKey, StringComparison.Ordinal) &&
        string.Equals(record.DisplayNameSearchKey, current.DisplayNameSearchKey, StringComparison.Ordinal) &&
        string.Equals(record.TypeNameLookupKey, current.TypeNameLookupKey, StringComparison.Ordinal) &&
        string.Equals(record.StoreNameLookupKey, current.StoreNameLookupKey, StringComparison.Ordinal) &&
        string.Equals(record.ScopeLookupKey, current.ScopeLookupKey, StringComparison.Ordinal) &&
        string.Equals(record.Status, current.Status, StringComparison.Ordinal) &&
        record.HasNonExpiringActiveVersion == current.HasNonExpiringActiveVersion &&
        record.MaxActiveVersionExpiresAt == current.MaxActiveVersionExpiresAt;

    private static bool SameProjections(SecretDocument stored, SecretDocument current) =>
        string.Equals(stored.NameSearchKey, current.NameSearchKey, StringComparison.Ordinal) &&
        string.Equals(stored.DisplayNameSearchKey, current.DisplayNameSearchKey, StringComparison.Ordinal) &&
        string.Equals(stored.TypeNameLookupKey, current.TypeNameLookupKey, StringComparison.Ordinal) &&
        string.Equals(stored.StoreNameLookupKey, current.StoreNameLookupKey, StringComparison.Ordinal) &&
        string.Equals(stored.ScopeLookupKey, current.ScopeLookupKey, StringComparison.Ordinal) &&
        string.Equals(stored.Status, current.Status, StringComparison.Ordinal) &&
        stored.HasNonExpiringActiveVersion == current.HasNonExpiringActiveVersion &&
        stored.MaxActiveVersionExpiresAt == current.MaxActiveVersionExpiresAt;
}
