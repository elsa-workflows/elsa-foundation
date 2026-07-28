using System.Text.Json;

namespace Elsa.Studio.Preferences.Core.Models;

public sealed record StudioPreferenceKey(
    string SubjectId,
    string TenantId,
    string StudioHostId,
    string Namespace);

public sealed record StudioPreferenceDocument(
    string Namespace,
    int SchemaVersion,
    string Revision,
    JsonElement Value,
    DateTimeOffset UpdatedAt);

public sealed record StudioPreferenceWrite(int SchemaVersion, JsonElement Value);

public enum StudioPreferenceWriteConditionKind
{
    MustNotExist,
    RevisionMatches
}

public sealed record StudioPreferenceWriteCondition(StudioPreferenceWriteConditionKind Kind, string? Revision = null)
{
    public static StudioPreferenceWriteCondition MustNotExist { get; } = new(StudioPreferenceWriteConditionKind.MustNotExist);

    public static StudioPreferenceWriteCondition Matches(string revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        return new(StudioPreferenceWriteConditionKind.RevisionMatches, revision);
    }
}

public enum StudioPreferenceStoreWriteStatus
{
    Saved,
    Conflict,
    NotFound
}

public sealed record StudioPreferenceStoreWriteResult(
    StudioPreferenceStoreWriteStatus Status,
    StudioPreferenceDocument? Document = null);
