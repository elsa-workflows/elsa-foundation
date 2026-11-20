using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Tests.Fixtures;

internal sealed class InMemoryActivityExecutionHierarchyStore : IActivityExecutionHierarchyStore
{
    private readonly Dictionary<(string WorkflowExecutionId, string ExecutionScopeId), ActivityExecutionHierarchyRoot> _roots = new();
    private readonly Dictionary<(string WorkflowExecutionId, string ExecutionScopeId, string ActivityExecutionId), ActivityExecutionHierarchyRecord> _records = new();
    private readonly Dictionary<(string WorkflowExecutionId, string ActivityExecutionId), ActivityExecutionLayout> _layouts = new();
    private readonly byte[] _cursorKey = RandomNumberGenerator.GetBytes(32);
    private readonly Lock _gate = new();

    public void SaveRoot(ActivityExecutionHierarchyRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        lock (_gate)
            _roots[(root.WorkflowExecutionId, root.ExecutionScopeId)] = root;
    }

    public void SaveLayout(ActivityExecutionLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        lock (_gate)
            _layouts[(layout.WorkflowExecutionId, layout.ActivityExecutionId)] = layout;
    }

    public ValueTask SaveAsync(ActivityExecutionHierarchyRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, record.Item.WorkflowExecutionId))
            throw new ArgumentException("The record and item workflow execution ids must match.", nameof(record));
        if (!StringComparer.Ordinal.Equals(record.ActivityExecutionId, record.Item.ActivityExecutionId))
            throw new ArgumentException("The record and item activity execution ids must match.", nameof(record));
        if (record.ExecutionSequence != record.Item.ExecutionSequence)
            throw new ArgumentException("The record and item execution sequences must match.", nameof(record));

        lock (_gate)
            _records[(record.WorkflowExecutionId, record.ExecutionScopeId, record.ActivityExecutionId)] = record;
        return ValueTask.CompletedTask;
    }

    public ValueTask<ActivityExecutionHierarchyPage?> ReadPageAsync(
        ActivityExecutionHierarchyQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.RootActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.AuthorizationProfile);

        lock (_gate)
        {
            if (!_roots.TryGetValue((query.WorkflowExecutionId, query.RootActivityExecutionId), out var root))
                return ValueTask.FromResult<ActivityExecutionHierarchyPage?>(null);

            var requestedLimit = query.Limit;
            if (requestedLimit is <= 0)
                throw new ArgumentOutOfRangeException(nameof(query), "The hierarchy page limit must be positive.");

            var cursor = query.Cursor is null ? null : DecodeCursor(query.Cursor);
            var effectiveLimit = cursor?.EffectiveLimit ?? requestedLimit ?? 100;
            var include = query.Include.OrderBy(value => value).ToArray();
            if (cursor is not null)
                ValidateCursor(cursor, query, effectiveLimit, include);

            var scopedRecords = _records.Values
                .Where(record => StringComparer.Ordinal.Equals(record.WorkflowExecutionId, query.WorkflowExecutionId))
                .Where(record => StringComparer.Ordinal.Equals(record.ExecutionScopeId, query.RootActivityExecutionId))
                .ToArray();
            var watermark = cursor?.CommittedThroughSequence ?? scopedRecords.Select(record => record.ExecutionSequence).DefaultIfEmpty(0).Max();

            var ordered = scopedRecords
                .Where(record => record.ExecutionSequence <= watermark)
                .Where(record => cursor is null || IsAfter(record, cursor.LastExecutionSequence, cursor.LastActivityExecutionId))
                .OrderBy(record => record.ExecutionSequence)
                .ThenBy(record => record.ActivityExecutionId, StringComparer.Ordinal)
                .Take(effectiveLimit + 1)
                .ToArray();
            var pageRecords = ordered.Take(effectiveLimit).ToArray();
            var last = pageRecords.LastOrDefault();
            var nextCursor = ordered.Length > effectiveLimit && last is not null
                ? EncodeCursor(new CursorState(
                    query.WorkflowExecutionId,
                    query.RootActivityExecutionId,
                    query.AuthorizationProfile,
                    include,
                    effectiveLimit,
                    watermark,
                    last.ExecutionSequence,
                    last.ActivityExecutionId))
                : null;

            return ValueTask.FromResult<ActivityExecutionHierarchyPage?>(new ActivityExecutionHierarchyPage(
                root,
                watermark,
                effectiveLimit,
                pageRecords.Select(record => record.Item).ToArray(),
                nextCursor));
        }
    }

    public ValueTask<ActivityExecutionLayout?> FindLayoutAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        lock (_gate)
            return ValueTask.FromResult(_layouts.GetValueOrDefault((workflowExecutionId, activityExecutionId)));
    }

    public ValueTask<ActivityExecutionBoundary?> FindBoundaryAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult<ActivityExecutionBoundary?>(null);
    }

    public ValueTask<ActivityExecutionAttemptNavigation?> FindAttemptNavigationAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult(ActivityExecutionHierarchyProjector.FindAttemptNavigation(
                _records.Values
                    .Where(record => StringComparer.Ordinal.Equals(record.WorkflowExecutionId, workflowExecutionId))
                    .ToArray(),
                activityExecutionId));
    }

    private static bool IsAfter(ActivityExecutionHierarchyRecord record, long sequence, string activityExecutionId) =>
        record.ExecutionSequence > sequence ||
        record.ExecutionSequence == sequence && StringComparer.Ordinal.Compare(record.ActivityExecutionId, activityExecutionId) > 0;

    private static void ValidateCursor(
        CursorState cursor,
        ActivityExecutionHierarchyQuery query,
        int effectiveLimit,
        ActivityExecutionHierarchyInclude[] include)
    {
        if (!StringComparer.Ordinal.Equals(cursor.WorkflowExecutionId, query.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(cursor.RootActivityExecutionId, query.RootActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(cursor.AuthorizationProfile, query.AuthorizationProfile) ||
            cursor.EffectiveLimit != effectiveLimit ||
            (query.Limit is not null && query.Limit != cursor.EffectiveLimit) ||
            !cursor.Include.SequenceEqual(include))
            throw new InvalidOperationException("The hierarchy cursor is bound to a different query shape.");
    }

    private string EncodeCursor(CursorState cursor)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(cursor);
        var signature = HMACSHA256.HashData(_cursorKey, payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    private CursorState DecodeCursor(string value)
    {
        var parts = value.Split('.', StringSplitOptions.None);
        if (parts.Length != 2)
            throw new InvalidOperationException("The hierarchy cursor is malformed.");

        try
        {
            var payload = FromBase64Url(parts[0]);
            var signature = FromBase64Url(parts[1]);
            var expected = HMACSHA256.HashData(_cursorKey, payload);
            if (!CryptographicOperations.FixedTimeEquals(signature, expected))
                throw new InvalidOperationException("The hierarchy cursor signature is invalid.");
            return JsonSerializer.Deserialize<CursorState>(payload)
                   ?? throw new InvalidOperationException("The hierarchy cursor payload is empty.");
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("The hierarchy cursor is malformed.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The hierarchy cursor payload is malformed.", exception);
        }
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private sealed record CursorState(
        string WorkflowExecutionId,
        string RootActivityExecutionId,
        string AuthorizationProfile,
        ActivityExecutionHierarchyInclude[] Include,
        int EffectiveLimit,
        long CommittedThroughSequence,
        long LastExecutionSequence,
        string LastActivityExecutionId);
}
