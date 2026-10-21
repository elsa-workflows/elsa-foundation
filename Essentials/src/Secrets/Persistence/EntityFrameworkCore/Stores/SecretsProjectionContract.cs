using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Audits and repairs projection fields written before the persisted Unicode algorithm was pinned, and rows a writer
/// from before <c>FreeNameOnDelete</c> deleted in place, leaving the deleted secret in the row that holds its name.
/// Reindexing is an explicit operator action; ordinary startup only validates and fails closed.
/// Both paths page by the <c>(TenantId, NormalizedName, Tombstone)</c> key so each bounded batch is a single
/// forward seek rather than a growing OFFSET rescan. The cursor carries the whole key: a name shared by the secret
/// holding it and secrets deleted under it is several rows, and a cursor without the tombstone would skip those of
/// them that fall after a page boundary, reporting them current without ever reading them.
/// </summary>
public static class SecretsProjectionContract
{
    private const int BatchSize = 100;

    /// <summary>
    /// Validates stored row and payload projections in bounded keyset pages.
    /// </summary>
    /// <exception cref="InvalidOperationException">Legacy projection bytes, or a secret deleted in place, remain; the operator must reindex.</exception>
    /// <exception cref="SecretsProjectionException">A row payload cannot be parsed or does not match its key.</exception>
    public static async Task EnsureCurrentAsync(
        SecretsDbContext context,
        CancellationToken cancellationToken = default)
    {
        if (!await HasLegacyProjectionsAsync(context, cancellationToken))
            return;

        throw new InvalidOperationException(
            "One or more Secrets rows do not use persisted projection " +
            $"'{SecretsSearchKeys.UnicodeOrdinalIgnoreCaseAlgorithmId}', or hold a secret that a host from before " +
            "FreeNameOnDelete deleted in place. " +
            $"Quiesce writers and run '{EfPostMigrationActions.Command}' for this host's provider to " +
            "reindex legacy rows before starting the host.");
    }

    /// <summary>
    /// The same read-only audit as <see cref="EnsureCurrentAsync"/>, reporting its verdict instead of throwing
    /// it: <c>true</c> when any row still carries a legacy projection or a secret deleted in place. This is what
    /// <c>SecretsProjectionReindex.AuditAsync</c> calls, because an <c>IEfPostMigrationAction</c> has to
    /// tell "required" apart from "the database could not be read" — and laundering the one
    /// <see cref="InvalidOperationException"/> that means the former out of every other one that means the
    /// latter is exactly the conflation that would report a broken database as a routine reindex.
    /// </summary>
    /// <exception cref="SecretsProjectionException">A row payload cannot be parsed or does not match its key.</exception>
    public static async Task<bool> HasLegacyProjectionsAsync(
        SecretsDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        SeekCursor? after = null;
        while (true)
        {
            var records = await TakePageAsync(context.Secrets.AsNoTracking(), after, cancellationToken);
            var hasLegacyProjections = false;
            // Every row in the page is read even once one is known legacy: a damaged row later in the same
            // page must still surface as the SecretsProjectionException it is, not be hidden by an early exit.
            foreach (var record in records)
            {
                if (!IsCurrent(record))
                    hasLegacyProjections = true;
            }

            if (hasLegacyProjections)
                return true;

            if (records.Count < BatchSize)
                return false;

            after = Cursor(records[^1]);
        }
    }

    /// <summary>
    /// Rewrites legacy projection columns and payload copies from the authoritative stored secret, and moves a secret
    /// deleted in place out of the row that holds its name into a row of its own under its id, which frees the name.
    /// </summary>
    /// <exception cref="SecretsProjectionException">A row payload cannot be parsed or does not match its key, or the database refuses the row a secret deleted in place moves to because its key is too long; the current batch is rolled back.</exception>
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
                var moves = new List<(SecretRecord Record, SecretDocument Current)>();

                foreach (var record in records)
                {
                    var (stored, current) = ReadDocuments(record);
                    if (DeletedInPlace(record, stored))
                    {
                        moves.Add((record, current));
                        continue;
                    }

                    if (Matches(record, stored, current))
                        continue;

                    current.CopyProjectionsTo(record);
                    updatedBatch++;
                }

                if (updatedBatch > 0)
                    await context.SaveProjectionChangesAsync(cancellationToken);

                foreach (var (record, current) in moves)
                {
                    await MoveDeletedInPlaceAsync(context, record, current, cancellationToken);
                    updatedBatch++;
                }

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

    /// <summary>
    /// The same move a delete makes, within the batch's transaction: the row holding the name goes, its revision still
    /// guarding the delete against a concurrent writer, and the secret's own row takes its id as the tombstone. The
    /// revision carries over, as for every row this repairs. Each move is its own save, so a row the provider refuses
    /// is the one the failure names: on SQL Server, a secret created before the key length bound whose tenant id and
    /// name leave its id no room within 900 bytes cannot be moved, and only the operator can shorten or remove it. Only
    /// that refusal names the row; any other failure, a lost race, a deadlock, a timeout or a dropped connection, says
    /// nothing about it and propagates as itself.
    /// </summary>
    private static async Task MoveDeletedInPlaceAsync(
        SecretsDbContext context,
        SecretRecord record,
        SecretDocument current,
        CancellationToken cancellationToken)
    {
        context.Secrets.Remove(record);
        context.Secrets.Add(current.ToRecord(record.ConcurrencyToken));
        try
        {
            await context.SaveProjectionChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsKeyTooLong(exception))
        {
            throw SecretsProjectionException.ForRefusedMove(record, current.Tombstone, exception);
        }
    }

    /// <summary>
    /// Whether the database refused a row because its key is longer than the key allows, read by the provider
    /// exception's type name so no provider assembly is referenced. Only SQL Server can: its clustered key is 900 bytes,
    /// less than the columns can hold together, and it refuses an entry past that with error 1946. SQLite has no key
    /// limit. PostgreSQL's B-tree entry limit and MySQL's key prefix limit both exceed the most bytes the bounded
    /// columns can hold, and MySQL checks its limit when the key is created, not when a row is written.
    /// </summary>
    private static bool IsKeyTooLong(DbUpdateException exception)
    {
        for (Exception? current = exception.InnerException; current is not null; current = current.InnerException)
        {
            var type = current.GetType();
            if (type.Name.Equals("SqlException", StringComparison.Ordinal) &&
                (type.GetProperty("Number")?.GetValue(current) is 1946 ||
                 current.Message.Contains("maximum length of 900 bytes", StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    private readonly record struct SeekCursor(string TenantId, string NormalizedName, string Tombstone);

    private static SeekCursor Cursor(SecretRecord record) => new(record.TenantId, record.NormalizedName, record.Tombstone);

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
        var tombstone = cursor.Tombstone;
        return records.Where(record =>
            record.TenantId.CompareTo(tenantId) > 0 ||
            (record.TenantId == tenantId && record.NormalizedName.CompareTo(normalizedName) > 0) ||
            (record.TenantId == tenantId && record.NormalizedName == normalizedName && record.Tombstone.CompareTo(tombstone) > 0));
    }

    private static IOrderedQueryable<SecretRecord> Ordered(IQueryable<SecretRecord> records) =>
        records.OrderBy(record => record.TenantId).ThenBy(record => record.NormalizedName).ThenBy(record => record.Tombstone);

    private static bool IsCurrent(SecretRecord record)
    {
        var (stored, current) = ReadDocuments(record);
        return !DeletedInPlace(record, stored) && Matches(record, stored, current);
    }

    /// <summary>
    /// A deleted secret still in the row that holds its name: what a host from before <c>FreeNameOnDelete</c> leaves when
    /// it deletes in place, since it knows no tombstone. The name stays taken until the reindex moves the row.
    /// </summary>
    private static bool DeletedInPlace(SecretRecord record, SecretDocument stored) =>
        record.Tombstone.Length == 0 && stored.Secret.Status == SecretStatus.Deleted;

    private static (SecretDocument Stored, SecretDocument Current) ReadDocuments(SecretRecord record)
    {
        // Settled before the payload is parsed: a row a newer module version wrote reports skew here rather than a
        // projection or payload fault.
        EfSchemaVersion.EnsureReadable(SecretsEfModule.Chain, record.SchemaVersion);
        SecretDocument stored;
        SecretDocument current;
        try
        {
            using var json = JsonDocument.Parse(SecretsEfModule.Chain.Upcast<SecretRecord>(record.SchemaVersion, (nameof(record.Payload), record.Payload))[nameof(record.Payload)]!);
            ValidatePayloadStructure(json.RootElement);
            stored = SecretDocument.Parse(json.RootElement);
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

        // A live secret under a tombstone would be invisible to every lookup, and nothing says it should take the name
        // back, so it fails closed. A deleted secret in the row that holds its name is repairable: the reindex moves it
        // under its id, unless that id cannot be a tombstone.
        if (!string.Equals(stored.TenantId, record.TenantId, StringComparison.Ordinal) ||
            !string.Equals(stored.NormalizedName, record.NormalizedName, StringComparison.Ordinal) ||
            !string.Equals(current.TenantId, record.TenantId, StringComparison.Ordinal) ||
            !string.Equals(current.NormalizedName, record.NormalizedName, StringComparison.Ordinal) ||
            record.Tombstone.Length > 0 && stored.Secret.Status != SecretStatus.Deleted ||
            DeletedInPlace(record, stored) && !SecretRecordConfiguration.IsTombstone(stored.Secret.Id))
        {
            throw SecretsProjectionException.ForIdentity(record);
        }

        return (stored, current);
    }

    private static void ValidatePayloadStructure(JsonElement root)
    {
        if (!TryGetProperty(root, "secret", out var secret) || secret.ValueKind != JsonValueKind.Object)
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
