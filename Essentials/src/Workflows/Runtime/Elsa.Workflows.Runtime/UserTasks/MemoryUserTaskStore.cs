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
        return ValueTask.FromResult(state.Forms.TryGetValue((Scope, tenantId, id, version), out var json) ? JsonSerializer.Deserialize<UserTaskFormVersion>(json) : null);
    }

    public ValueTask<UserTaskFormPage> ListFormsAsync(string tenantId, int limit, string? after = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ValidateLimit(limit);
        var scope = Scope;
        var rows = state.Forms.Where(x => x.Key.Scope == scope && x.Key.Tenant == tenantId)
            .Select(x => new { Key = UserTaskRules.Id(scope, tenantId, x.Key.Id, x.Key.Version), Version = JsonSerializer.Deserialize<UserTaskFormVersion>(x.Value)! })
            .Where(x => after is null || StringComparer.Ordinal.Compare(x.Key, after) > 0).OrderBy(x => x.Key, StringComparer.Ordinal).Take(limit + 1).ToArray();
        return ValueTask.FromResult(new UserTaskFormPage(rows.Take(limit).Select(x => x.Version).ToArray(), rows.Length > limit ? rows[limit - 1].Key : null));
    }

    public ValueTask<bool> TryCreateFormAsync(UserTaskFormVersion version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(state.Forms.TryAdd((Scope, version.TenantId, version.Form.Id, version.Form.Version), JsonSerializer.Serialize(version)));
    }

    private static UserTaskRecord Read(string json) => JsonSerializer.Deserialize<UserTaskRecord>(json)!;
    private static void ValidateLimit(int limit) { if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit)); }
    private static UserTaskPage Page(IEnumerable<UserTaskRecord> source, int limit, string? after)
    {
        var rows = source.Where(x => after is null || StringComparer.Ordinal.Compare(x.Id, after) > 0).OrderBy(x => x.Id, StringComparer.Ordinal).Take(limit + 1).ToArray();
        return new(rows.Take(limit).ToArray(), rows.Length > limit ? rows[limit - 1].Id : null);
    }
}
