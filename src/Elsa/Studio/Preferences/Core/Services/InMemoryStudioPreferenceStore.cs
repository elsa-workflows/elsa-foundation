using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Models;

namespace Elsa.Studio.Preferences.Core.Services;

public sealed class InMemoryStudioPreferenceStore : IStudioPreferenceStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<StudioPreferenceKey, StudioPreferenceDocument> _documents = [];

    public ValueTask<StudioPreferenceDocument?> FindAsync(StudioPreferenceKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
            return ValueTask.FromResult(_documents.GetValueOrDefault(key));
    }

    public ValueTask<StudioPreferenceStoreWriteResult> WriteAsync(
        StudioPreferenceKey key,
        StudioPreferenceWrite write,
        StudioPreferenceWriteCondition condition,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            _documents.TryGetValue(key, out var existing);
            if (condition.Kind == StudioPreferenceWriteConditionKind.MustNotExist && existing is not null)
                return ValueTask.FromResult(new StudioPreferenceStoreWriteResult(StudioPreferenceStoreWriteStatus.Conflict));
            if (condition.Kind == StudioPreferenceWriteConditionKind.RevisionMatches && existing is null)
                return ValueTask.FromResult(new StudioPreferenceStoreWriteResult(StudioPreferenceStoreWriteStatus.NotFound));
            if (condition.Kind == StudioPreferenceWriteConditionKind.RevisionMatches &&
                !string.Equals(existing!.Revision, condition.Revision, StringComparison.Ordinal))
                return ValueTask.FromResult(new StudioPreferenceStoreWriteResult(StudioPreferenceStoreWriteStatus.Conflict));

            var revision = existing is null ? 1 : ParseRevision(existing.Revision) + 1;
            var document = new StudioPreferenceDocument(key.Namespace, write.SchemaVersion, $"rev-{revision}", write.Value.Clone(), updatedAt);
            _documents[key] = document;
            return ValueTask.FromResult(new StudioPreferenceStoreWriteResult(StudioPreferenceStoreWriteStatus.Saved, document));
        }
    }

    private static int ParseRevision(string revision) => int.Parse(revision.AsSpan("rev-".Length));
}
