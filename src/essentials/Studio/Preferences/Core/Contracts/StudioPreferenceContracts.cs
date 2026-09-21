using System.Text.Json;
using Elsa.Studio.Preferences.Core.Models;

namespace Elsa.Studio.Preferences.Core.Contracts;

public interface IStudioPreferenceNamespace
{
    string Name { get; }
    int CurrentSchemaVersion { get; }
    int MaxBytes { get; }
    bool Validate(JsonElement value);
    JsonElement? Migrate(JsonElement value, int fromSchemaVersion);
}

public interface IStudioPreferenceNamespaceRegistry
{
    IStudioPreferenceNamespace? Find(string name);
    IReadOnlyCollection<IStudioPreferenceNamespace> List();
}

public interface IStudioPreferenceStore
{
    ValueTask<StudioPreferenceDocument?> FindAsync(StudioPreferenceKey key, CancellationToken cancellationToken = default);

    ValueTask<StudioPreferenceStoreWriteResult> WriteAsync(
        StudioPreferenceKey key,
        StudioPreferenceWrite write,
        StudioPreferenceWriteCondition condition,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}

public interface IStudioPreferenceService
{
    ValueTask<StudioPreferenceDocument?> FindAsync(StudioPreferenceKey key, CancellationToken cancellationToken = default);

    ValueTask<StudioPreferenceDocument> WriteAsync(
        StudioPreferenceKey key,
        StudioPreferenceWrite write,
        StudioPreferenceWriteCondition condition,
        CancellationToken cancellationToken = default);
}
