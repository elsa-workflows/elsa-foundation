using System.Text.Json;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Audits and repairs projection fields written before the persisted Unicode algorithm was pinned.
/// Reindexing is an explicit operator action; ordinary startup only validates and fails closed.
/// Both paths page by the <c>(TenantId, NormalizedName)</c> key so each bounded batch is a single
/// forward seek rather than a growing OFFSET rescan.
/// </summary>
public static class SecretsProjectionContract
{
    private const int BatchSize = 100;

    /// <summary>
    /// Validates stored row and payload projections in bounded keyset pages.
    /// </summary>
    /// <exception cref="InvalidOperationException">Legacy projection bytes remain; the operator must reindex.</exception>
    /// <exception cref="SecretsProjectionException">A row payload cannot be parsed or does not match its key.</exception>
    public static async Task EnsureCurrentAsync(
        SecretsDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        SeekCursor? after = null;
        while (true)
        {
            var records = await TakePageAsync(context.Secrets.AsNoTracking(), after, cancellationToken);
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

            after = Cursor(records[^1]);
        }
    }

    /// <summary>
    /// Rewrites legacy projection columns and payload copies from the authoritative stored secret.
    /// </summary>
    /// <exception cref="SecretsProjectionException">A row payload cannot be parsed or does not match its key; the current batch is rolled back.</exception>
    public static async Task<int> ReindexAsync(
        SecretsDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        SeekCursor? after = null;
        var updatedTotal = 0;
        while (true)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var records = await TakePageAsync(context.Secrets, after, cancellationToken);
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
                var done = records.Count < BatchSize;
                if (!done)
                    after = Cursor(records[^1]);
                context.ChangeTracker.Clear();

                if (done)
                    return updatedTotal;
            }
            catch
            {
                context.ChangeTracker.Clear();
                throw;
            }
        }
    }

    private readonly record struct SeekCursor(string TenantId, string NormalizedName);

    private static SeekCursor Cursor(SecretRecord record) => new(record.TenantId, record.NormalizedName);

    private static Task<List<SecretRecord>> TakePageAsync(
        IQueryable<SecretRecord> records,
        SeekCursor? after,
        CancellationToken cancellationToken) =>
        Ordered(After(records, after)).Take(BatchSize).ToListAsync(cancellationToken);

    private static IQueryable<SecretRecord> After(IQueryable<SecretRecord> records, SeekCursor? after)
    {
        if (after is not { } cursor)
            return records;

        var tenantId = cursor.TenantId;
        var normalizedName = cursor.NormalizedName;
        return records.Where(record =>
            record.TenantId.CompareTo(tenantId) > 0 ||
            (record.TenantId == tenantId && record.NormalizedName.CompareTo(normalizedName) > 0));
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
        SecretDocument stored;
        SecretDocument current;
        try
        {
            ValidatePayloadStructure(record.Payload);
            stored = SecretDocument.Parse(record.Payload);
            if (stored.Secret is null)
                throw new InvalidOperationException("The serialized document has no authoritative Secret.");
            if (stored.Secret.Versions is null)
                throw new InvalidOperationException("The serialized Secret has no version collection.");
            if (stored.Secret.Versions.Any(static version => version is null))
                throw new InvalidOperationException("The serialized Secret contains an invalid version entry.");

            current = SecretDocument.FromSecret(stored.Secret);
        }
        catch (JsonException exception)
        {
            throw SecretsProjectionException.ForPayload(record, exception);
        }
        catch (NotSupportedException exception)
        {
            throw SecretsProjectionException.ForPayload(record, exception);
        }
        catch (InvalidOperationException exception)
        {
            throw SecretsProjectionException.ForPayload(record, exception);
        }
        catch (ArgumentException exception)
        {
            throw SecretsProjectionException.ForPayload(record, exception);
        }

        if (!string.Equals(stored.TenantId, record.TenantId, StringComparison.Ordinal) ||
            !string.Equals(stored.NormalizedName, record.NormalizedName, StringComparison.Ordinal) ||
            !string.Equals(current.TenantId, record.TenantId, StringComparison.Ordinal) ||
            !string.Equals(current.NormalizedName, record.NormalizedName, StringComparison.Ordinal))
        {
            throw SecretsProjectionException.ForIdentity(record);
        }

        return (stored, current);
    }

    private static void ValidatePayloadStructure(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        if (!TryGetProperty(json.RootElement, "secret", out var secret) || secret.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The serialized document has no authoritative Secret object.");
        if (!TryGetProperty(secret, "versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The serialized Secret has no version array.");
        if (versions.EnumerateArray().Any(static version => version.ValueKind == JsonValueKind.Null))
            throw new InvalidOperationException("The serialized Secret contains an invalid version entry.");
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        var found = false;
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            value = property.Value;
            found = true;
        }

        return found;
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
