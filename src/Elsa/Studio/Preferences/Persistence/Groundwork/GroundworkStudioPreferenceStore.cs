using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Models;
using Groundwork.Store;

namespace Elsa.Studio.Preferences.Persistence.Groundwork;

public sealed class GroundworkStudioPreferenceStore(
    IGroundworkStorageSessionSource sessions,
    string? targetName = null) : IStudioPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask<StudioPreferenceDocument?> FindAsync(StudioPreferenceKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = sessions.Open(StudioPreferencesGroundworkStorageSchema.UnitId, StorageAccess.Global, targetName);
        var entry = session.Read(new StorageKey(new Dictionary<string, object?>
        {
            [StudioPreferencesGroundworkStorageSchema.IdField] = CreateId(key)
        }));
        return ValueTask.FromResult<StudioPreferenceDocument?>(entry is null ? null : Map(entry));
    }

    public ValueTask<StudioPreferenceStoreWriteResult> WriteAsync(
        StudioPreferenceKey key,
        StudioPreferenceWrite write,
        StudioPreferenceWriteCondition condition,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = condition.Kind switch
        {
            StudioPreferenceWriteConditionKind.MustNotExist => WriteOptions.CreateOnly,
            StudioPreferenceWriteConditionKind.RevisionMatches when TryParseRevision(condition.Revision, out var version) =>
                WriteOptions.IfVersion(version),
            _ => null
        };

        if (options is null)
            return ValueTask.FromResult(new StudioPreferenceStoreWriteResult(StudioPreferenceStoreWriteStatus.Conflict));

        var value = new StoredPreference(
            key.SubjectId,
            key.TenantId,
            key.StudioHostId,
            key.Namespace,
            write.SchemaVersion,
            write.Value.Clone(),
            updatedAt);
        var session = sessions.Open(StudioPreferencesGroundworkStorageSchema.UnitId, StorageAccess.Global, targetName);
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not support the conditional write required by Studio Preferences.");
        }
        var result = concurrency.ConditionalUpsert(
            new StorageValues(new Dictionary<string, object?>
            {
                [StudioPreferencesGroundworkStorageSchema.IdField] = CreateId(key),
                [StudioPreferencesGroundworkStorageSchema.PayloadField] = JsonSerializer.Serialize(value, JsonOptions)
            }),
            options);

        if (result.Succeeded && result.Version is { } savedVersion)
        {
            return ValueTask.FromResult(new StudioPreferenceStoreWriteResult(
                StudioPreferenceStoreWriteStatus.Saved,
                Map(value, savedVersion)));
        }

        return ValueTask.FromResult<StudioPreferenceStoreWriteResult>(result.Detail.Status == WriteOutcomeStatus.NotFound
            ? new(StudioPreferenceStoreWriteStatus.NotFound)
            : new(StudioPreferenceStoreWriteStatus.Conflict));
    }

    private static StudioPreferenceDocument Map(StoredEntry entry)
    {
        if (!entry.Values.Values.TryGetValue(StudioPreferencesGroundworkStorageSchema.PayloadField, out var payload))
            throw new JsonException("The Studio preference payload is missing.");
        var json = payload switch
        {
            string text => text,
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            _ => throw new JsonException("The Studio preference payload is not JSON.")
        };
        var stored = JsonSerializer.Deserialize<StoredPreference>(json, JsonOptions)
                     ?? throw new JsonException("The Studio preference document is empty.");
        if (entry.Version is not { } version)
            throw new JsonException("The Studio preference row has no optimistic revision.");
        return Map(stored, version);
    }

    private static StudioPreferenceDocument Map(StoredPreference stored, long version) =>
        new(stored.Namespace, stored.SchemaVersion, $"rev-{version}", stored.Value.Clone(), stored.UpdatedAt);

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
               long.TryParse(revision.AsSpan("rev-".Length), out version) &&
               version > 0;
    }

    private sealed record StoredPreference(
        string SubjectId,
        string TenantId,
        string StudioHostId,
        string Namespace,
        int SchemaVersion,
        JsonElement Value,
        DateTimeOffset UpdatedAt);
}
