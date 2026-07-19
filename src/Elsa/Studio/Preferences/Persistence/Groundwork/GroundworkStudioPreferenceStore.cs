using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Studio.Preferences.Persistence.Groundwork;

public sealed class GroundworkStudioPreferenceStore(IDocumentStore store) : IStudioPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<StudioPreferenceDocument?> FindAsync(StudioPreferenceKey key, CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(StudioPreferencesStorageManifest.DocumentKind, CreateId(key), cancellationToken);
        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<StudioPreferenceStoreWriteResult> WriteAsync(
        StudioPreferenceKey key,
        StudioPreferenceWrite write,
        StudioPreferenceWriteCondition condition,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var expectedVersion = condition.Kind switch
        {
            StudioPreferenceWriteConditionKind.MustNotExist => 0,
            StudioPreferenceWriteConditionKind.RevisionMatches when TryParseRevision(condition.Revision, out var version) => version,
            _ => -1
        };

        if (expectedVersion < 0)
            return new(StudioPreferenceStoreWriteStatus.Conflict);

        var value = new StoredPreference(
            key.SubjectId,
            key.TenantId,
            key.StudioHostId,
            key.Namespace,
            write.SchemaVersion,
            write.Value.Clone(),
            updatedAt);
        var result = await store.SaveAsync(
            new SaveDocumentRequest(
                StudioPreferencesStorageManifest.DocumentKind,
                CreateId(key),
                StudioPreferencesStorageManifest.SchemaVersion,
                JsonSerializer.Serialize(value, JsonOptions),
                ExpectedVersion: expectedVersion),
            cancellationToken);

        return result.Status switch
        {
            DocumentStoreWriteStatus.Saved => new(StudioPreferenceStoreWriteStatus.Saved, Map(result.Document!)),
            DocumentStoreWriteStatus.NotFound => new(StudioPreferenceStoreWriteStatus.NotFound),
            _ => new(StudioPreferenceStoreWriteStatus.Conflict)
        };
    }

    private static StudioPreferenceDocument Map(DocumentEnvelope envelope)
    {
        var stored = JsonSerializer.Deserialize<StoredPreference>(envelope.ContentJson, JsonOptions)
                     ?? throw new JsonException("The Studio preference document is empty.");
        return new(stored.Namespace, stored.SchemaVersion, $"rev-{envelope.Version}", stored.Value.Clone(), stored.UpdatedAt);
    }

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
