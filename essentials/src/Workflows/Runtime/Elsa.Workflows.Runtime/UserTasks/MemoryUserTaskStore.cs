using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Workflows.Runtime.UserTasks;

internal sealed class UserTaskMemoryState
{
    internal readonly ConcurrentDictionary<(string Scope, string Tenant, string Id), string> Tasks = new();
    internal readonly ConcurrentDictionary<(string Scope, string Tenant, string Id, string Version), string> Forms = new();
}

/// <summary>Explicit development journal; scoped facade over partition-keyed process state.</summary>
internal sealed class MemoryUserTaskStore(UserTaskMemoryState state, IPersistenceAccessContextAccessor access) : IUserTaskStore
{
    public bool IsDurable => false;
    private string Scope => access.Current.RequireScope().Value;

    public ValueTask<UserTaskRecord?> FindAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(state.Tasks.TryGetValue((Scope, tenantId, id), out var json) ? Read(json) : null);
    }

    public ValueTask<bool> TryWriteAsync(UserTaskRecord record, long? expectedRevision, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record.Revision != (expectedRevision ?? 0) + 1) throw new ArgumentException("A conditional task write must advance exactly one revision.");
        var key = (Scope, record.TenantId, record.Id);
        var json = JsonSerializer.Serialize(record);
        if (expectedRevision is null) return ValueTask.FromResult(state.Tasks.TryAdd(key, json));
        return ValueTask.FromResult(state.Tasks.TryGetValue(key, out var prior) && Read(prior).Revision == expectedRevision && state.Tasks.TryUpdate(key, json, prior));
    }

    public ValueTask<UserTaskPage> QueryAsync(UserTaskQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateLimit(query.Limit);
        var scope = Scope;
        var items = state.Tasks.Where(x => x.Key.Scope == scope && x.Key.Tenant == query.TenantId)
            .Select(x => Read(x.Value)).Where(x => UserTaskRules.Matches(x, query));
        return ValueTask.FromResult(Page(items, query.Limit, query.After));
    }

    public ValueTask<UserTaskPage> RecoverAsync(int limit, string? after, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ValidateLimit(limit);
        var scope = Scope;
        return ValueTask.FromResult(Page(state.Tasks.Where(x => x.Key.Scope == scope).Select(x => Read(x.Value)).Where(x => !UserTaskRules.Terminal(x)), limit, after));
    }

    public ValueTask<UserTaskFormVersion?> FindFormAsync(string tenantId, string id, string version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(version != UserTaskProtocol.FormDraftVersion && state.Forms.TryGetValue((Scope, tenantId, id, version), out var json) ? JsonSerializer.Deserialize<UserTaskFormVersion>(json) : null);
    }

    public ValueTask<UserTaskFormPage> ListFormsAsync(string tenantId, int limit, string? after = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ValidateLimit(limit);
        var scope = Scope;
        var deleted = state.Forms.Where(x => x.Key.Scope == scope && x.Key.Tenant == tenantId && x.Key.Version == UserTaskProtocol.FormDraftVersion)
            .Select(x => ReadDraft(x.Value)).Where(x => x.Deleted).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var rows = state.Forms.Where(x => x.Key.Scope == scope && x.Key.Tenant == tenantId && x.Key.Version != UserTaskProtocol.FormDraftVersion)
            .Select(x => new { Key = UserTaskRules.Id(scope, tenantId, x.Key.Id, x.Key.Version), Version = JsonSerializer.Deserialize<UserTaskFormVersion>(x.Value)! })
            .Where(x => !deleted.Contains(x.Version.Form.Id))
            .Where(x => after is null || StringComparer.Ordinal.Compare(x.Key, after) > 0).OrderBy(x => x.Key, StringComparer.Ordinal).Take(limit + 1).ToArray();
        return ValueTask.FromResult(new UserTaskFormPage(rows.Take(limit).Select(x => x.Version).ToArray(), rows.Length > limit ? rows[limit - 1].Key : null));
    }

    public ValueTask<UserTaskFormPage> ListFormVersionsAsync(string tenantId, string id, int limit, string? after = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ValidateLimit(limit);
        var scope = Scope;
        if (state.Forms.TryGetValue((scope, tenantId, id, UserTaskProtocol.FormDraftVersion), out var draftJson) && ReadDraft(draftJson).Deleted)
            return ValueTask.FromResult(new UserTaskFormPage([], null));
        var rows = state.Forms.Where(x => x.Key.Scope == scope && x.Key.Tenant == tenantId && x.Key.Id == id && x.Key.Version != UserTaskProtocol.FormDraftVersion)
            .Select(x => new { Key = UserTaskRules.Id(scope, tenantId, x.Key.Id, x.Key.Version), Version = JsonSerializer.Deserialize<UserTaskFormVersion>(x.Value)! })
            .Where(x => after is null || StringComparer.Ordinal.Compare(x.Key, after) > 0).OrderBy(x => x.Key, StringComparer.Ordinal).Take(limit + 1).ToArray();
        return ValueTask.FromResult(new UserTaskFormPage(rows.Take(limit).Select(x => x.Version).ToArray(), rows.Length > limit ? rows[limit - 1].Key : null));
    }

    public ValueTask<bool> TryCreateFormAsync(UserTaskFormVersion version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (version.Form.Version == UserTaskProtocol.FormDraftVersion) return ValueTask.FromResult(false);
        lock (state)
        {
            if (state.Forms.TryGetValue((Scope, version.TenantId, version.Form.Id, UserTaskProtocol.FormDraftVersion), out var draftJson) && ReadDraft(draftJson).Deleted)
                return ValueTask.FromResult(false);
            return ValueTask.FromResult(state.Forms.TryAdd((Scope, version.TenantId, version.Form.Id, version.Form.Version), JsonSerializer.Serialize(version)));
        }
    }

    public ValueTask<UserTaskFormDefinition?> FindFormDefinitionAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = Scope;
        var rows = state.Forms.Where(x => x.Key.Scope == scope && x.Key.Tenant == tenantId && x.Key.Id == id).ToArray();
        return ValueTask.FromResult(rows.Length == 0 ? null : Definition(tenantId, id, rows));
    }

    public ValueTask<UserTaskFormDefinitionPage> ListFormDefinitionsAsync(string tenantId, int limit, string? after = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ValidateLimit(limit);
        var scope = Scope;
        var definitions = state.Forms.Keys.Where(x => x.Scope == scope && x.Tenant == tenantId).Select(x => x.Id).Distinct(StringComparer.Ordinal)
            .Select(id => Definition(tenantId, id, state.Forms.Where(x => x.Key.Scope == scope && x.Key.Tenant == tenantId && x.Key.Id == id).ToArray()))
            .Where(x => !x.Deleted && (x.LatestPublished is not null || x.Draft is not null))
            .Where(x => after is null || StringComparer.Ordinal.Compare(x.Id, after) > 0).OrderBy(x => x.Id, StringComparer.Ordinal).Take(limit + 1).ToArray();
        return ValueTask.FromResult(new UserTaskFormDefinitionPage(definitions.Take(limit).ToArray(), definitions.Length > limit ? definitions[limit - 1].Id : null));
    }

    public ValueTask<bool> TryWriteFormDraftAsync(UserTaskFormDraft draft, long expectedRevision, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (draft.Revision != expectedRevision + 1 || draft.Form is { } form && form.Id != draft.Id) throw new ArgumentException("A form draft write must advance one revision and retain its form identity.");
        var key = (Scope, draft.TenantId, draft.Id, UserTaskProtocol.FormDraftVersion);
        lock (state)
        {
            var current = state.Forms.TryGetValue(key, out var prior) ? ReadDraft(prior) : null;
            if ((current?.Revision ?? 0) != expectedRevision) return ValueTask.FromResult(false);
            if (current?.Deleted == true) return ValueTask.FromResult(false);
            var json = JsonSerializer.Serialize(draft);
            if (prior is null) return ValueTask.FromResult(state.Forms.TryAdd(key, json));
            return ValueTask.FromResult(state.Forms.TryUpdate(key, json, prior));
        }
    }

    public ValueTask<bool> TryPublishFormAsync(UserTaskFormVersion version, UserTaskFormDraft definitionState, long expectedRevision, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (definitionState.TenantId != version.TenantId || definitionState.Id != version.Form.Id || definitionState.Form is not null ||
            definitionState.Revision != expectedRevision + 1 || version.Form.Version == UserTaskProtocol.FormDraftVersion)
            throw new ArgumentException("A form publication must atomically advance the matching definition revision.");
        var scope = Scope;
        var draftKey = (scope, version.TenantId, version.Form.Id, UserTaskProtocol.FormDraftVersion);
        var versionKey = (scope, version.TenantId, version.Form.Id, version.Form.Version);
        lock (state)
        {
            var prior = state.Forms.TryGetValue(draftKey, out var json) ? json : null;
            var current = prior is null ? null : ReadDraft(prior);
            if ((current?.Revision ?? 0) != expectedRevision) return ValueTask.FromResult(false);
            if (current?.Deleted == true) return ValueTask.FromResult(false);
            if (!state.Forms.TryAdd(versionKey, JsonSerializer.Serialize(version))) return ValueTask.FromResult(false);
            var tombstone = JsonSerializer.Serialize(definitionState);
            if (prior is null ? state.Forms.TryAdd(draftKey, tombstone) : state.Forms.TryUpdate(draftKey, tombstone, prior)) return ValueTask.FromResult(true);
            state.Forms.TryRemove(versionKey, out _);
            return ValueTask.FromResult(false);
        }
    }

    private static UserTaskFormDefinition Definition(string tenantId, string id, IEnumerable<KeyValuePair<(string Scope, string Tenant, string Id, string Version), string>> source)
    {
        var rows = source.ToArray();
        var versions = rows.Where(x => x.Key.Version != UserTaskProtocol.FormDraftVersion).Select(x => JsonSerializer.Deserialize<UserTaskFormVersion>(x.Value)!).ToArray();
        var latestNumeric = versions.Where(x => int.TryParse(x.Form.Version, out _)).OrderByDescending(x => int.Parse(x.Form.Version, System.Globalization.CultureInfo.InvariantCulture)).ThenByDescending(x => x.CreatedAt).FirstOrDefault();
        var latest = latestNumeric ?? versions.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Form.Version, StringComparer.Ordinal).FirstOrDefault();
        var draftJson = rows.FirstOrDefault(x => x.Key.Version == UserTaskProtocol.FormDraftVersion).Value;
        var state = draftJson is null ? null : ReadDraft(draftJson);
        return new UserTaskFormDefinition(tenantId, id, latest, state?.Form is null ? null : state, state?.Revision ?? 0)
        {
            Deleted = state?.Deleted == true
        };
    }

    private static UserTaskFormDraft ReadDraft(string json) => JsonSerializer.Deserialize<UserTaskFormDraft>(json) ?? throw new InvalidDataException("The form draft is invalid.");

    private static UserTaskRecord Read(string json) => JsonSerializer.Deserialize<UserTaskRecord>(json)!;
    private static void ValidateLimit(int limit) { if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit)); }
    private static UserTaskPage Page(IEnumerable<UserTaskRecord> source, int limit, string? after)
    {
        var rows = source.Where(x => after is null || StringComparer.Ordinal.Compare(x.Id, after) > 0).OrderBy(x => x.Id, StringComparer.Ordinal).Take(limit + 1).ToArray();
        return new(rows.Take(limit).ToArray(), rows.Length > limit ? rows[limit - 1].Id : null);
    }
}
