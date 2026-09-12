using Elsa.Persistence.EntityFramework;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF-backed Studio preference repository. Namespace validation and quota enforcement remain in
/// <see cref="Elsa.Studio.Preferences.Core.Services.StudioPreferenceService"/>; this adapter owns
/// durable scope identity and optimistic revision semantics.
/// </summary>
public sealed class EfStudioPreferenceStore(StudioPreferencesDbContext context) : IStudioPreferenceStore
{
    public async ValueTask<StudioPreferenceDocument?> FindAsync(
        StudioPreferenceKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);
        var id = CreateId(key);
        var record = await context.Preferences.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == id,
            cancellationToken);
        return record is null || !MatchesKey(record, id, key) ? null : Map(record);
    }

    public async ValueTask<StudioPreferenceStoreWriteResult> WriteAsync(
        StudioPreferenceKey key,
        StudioPreferenceWrite write,
        StudioPreferenceWriteCondition condition,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(condition);

        var id = CreateId(key);
        var record = await context.Preferences.SingleOrDefaultAsync(
            row => row.Id == id,
            cancellationToken);

        if (record is not null && !MatchesKey(record, id, key))
        {
            throw new InvalidOperationException(
                $"The Studio preference identity hash '{id}' maps to a different scope. " +
                "The persisted row may be corrupt or the hash function may have collided.");
        }

        switch (condition.Kind)
        {
            case StudioPreferenceWriteConditionKind.MustNotExist:
                if (record is not null)
                    return Conflict();

                context.Preferences.Add(new StudioPreferenceRecord
                {
                    Id = id,
                    SubjectId = key.SubjectId,
                    TenantId = key.TenantId,
                    StudioHostId = key.StudioHostId,
                    Namespace = key.Namespace,
                    SchemaVersion = write.SchemaVersion,
                    ValueJson = write.Value.GetRawText(),
                    UpdatedAt = updatedAt,
                    Revision = 1
                });
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    return Saved(key.Namespace, write, updatedAt, 1);
                }
                catch (DbUpdateException exception) when
                    (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
                {
                    context.ChangeTracker.Clear();
                    // A concurrent creator can win between the point read and INSERT.
                    return Conflict();
                }

            case StudioPreferenceWriteConditionKind.RevisionMatches:
                if (!TryParseRevision(condition.Revision, out var expectedRevision))
                    return Conflict();
                if (record is null)
                    return new StudioPreferenceStoreWriteResult(StudioPreferenceStoreWriteStatus.NotFound);
                if (record.Revision != expectedRevision)
                    return Conflict();

                var nextRevision = checked(record.Revision + 1);
                record.SchemaVersion = write.SchemaVersion;
                record.ValueJson = write.Value.GetRawText();
                record.UpdatedAt = updatedAt;
                record.Revision = nextRevision;
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    return Saved(key.Namespace, write, updatedAt, nextRevision);
                }
                catch (DbUpdateConcurrencyException)
                {
                    context.ChangeTracker.Clear();
                    return Conflict();
                }

            default:
                return Conflict();
        }
    }

    private static StudioPreferenceStoreWriteResult Saved(
        string namespaceName,
        StudioPreferenceWrite write,
        DateTimeOffset updatedAt,
        long revision) =>
        new(
            StudioPreferenceStoreWriteStatus.Saved,
            new StudioPreferenceDocument(
                namespaceName,
                write.SchemaVersion,
                $"rev-{revision.ToString(CultureInfo.InvariantCulture)}",
                write.Value.Clone(),
                updatedAt));

    private static StudioPreferenceStoreWriteResult Conflict() =>
        new(StudioPreferenceStoreWriteStatus.Conflict);

    private static StudioPreferenceDocument Map(StudioPreferenceRecord record)
    {
        using var document = JsonDocument.Parse(record.ValueJson);
        return new StudioPreferenceDocument(
            record.Namespace,
            record.SchemaVersion,
            $"rev-{record.Revision.ToString(CultureInfo.InvariantCulture)}",
            document.RootElement.Clone(),
            record.UpdatedAt);
    }

    private static bool MatchesKey(StudioPreferenceRecord record, string id, StudioPreferenceKey key) =>
        string.Equals(record.Id, id, StringComparison.Ordinal) &&
        string.Equals(record.SubjectId, key.SubjectId, StringComparison.Ordinal) &&
        string.Equals(record.TenantId, key.TenantId, StringComparison.Ordinal) &&
        string.Equals(record.StudioHostId, key.StudioHostId, StringComparison.Ordinal) &&
        string.Equals(record.Namespace, key.Namespace, StringComparison.Ordinal);

    private static string CreateId(StudioPreferenceKey key)
    {
        var canonical = $"{key.SubjectId.Length}:{key.SubjectId}{key.TenantId.Length}:{key.TenantId}{key.StudioHostId.Length}:{key.StudioHostId}{key.Namespace.Length}:{key.Namespace}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool TryParseRevision(string? revision, out long version)
    {
        version = 0;
        return revision is not null &&
               revision.StartsWith("rev-", StringComparison.Ordinal) &&
               long.TryParse(
                   revision.AsSpan("rev-".Length),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out version) &&
               version > 0;
    }
}
