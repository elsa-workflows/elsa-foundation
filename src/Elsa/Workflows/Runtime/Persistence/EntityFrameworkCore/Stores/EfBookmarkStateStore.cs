using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Concrete EF adapter for the runtime bookmark state and stimulus index contracts.</summary>
public sealed class EfBookmarkStateStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IBookmarkStateStore, IBookmarkStimulusIndex
{
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();
    public async ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default)
    {
        ValidateState(state);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateId(scope, state.WorkflowExecutionId, state.BookmarkId);

        context.ChangeTracker.Clear();
        try
        {
            var existing = await context.Bookmarks.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
            if (existing is null)
            {
                context.Bookmarks.Add(ToEntity(state, scope, id, NewRevision()));
            }
            else
            {
                _ = MapChecked(existing, scope, state.WorkflowExecutionId, state.BookmarkId, id);
                var revision = checked(existing.Revision + 1);
                CopyToEntity(existing, state, scope, id, revision);
            }

            await context.SaveChangesAsync(cancellationToken);
            return state;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The bookmark state changed concurrently; retry the operation.", exception);
        }
        catch (DbUpdateException exception) when (
            EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) ||
            EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The bookmark state changed concurrently; retry the operation.", exception);
        }
        catch (DbUpdateException exception)
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("saving", state.WorkflowExecutionId, exception);
        }
        catch (DbException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The bookmark state changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("saving", state.WorkflowExecutionId, exception);
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
    {
        ValidateBound(workflowExecutionId, BookmarkStateEfModule.WorkflowIdentityMaximumLength, nameof(workflowExecutionId));
        ValidateBound(bookmarkId, BookmarkStateEfModule.BookmarkIdentityMaximumLength, nameof(bookmarkId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateId(scope, workflowExecutionId, bookmarkId);
        try
        {
            var row = await context.Bookmarks.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (row is null)
                return false;
            _ = MapChecked(row, scope, workflowExecutionId, bookmarkId, id);
            context.Bookmarks.Remove(row);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return false;
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
        {
            context.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException exception)
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("deleting", workflowExecutionId, exception);
        }
        catch (DbException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
        {
            context.ChangeTracker.Clear();
            return false;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("deleting", workflowExecutionId, exception);
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public async ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
    {
        ValidateBound(workflowExecutionId, BookmarkStateEfModule.WorkflowIdentityMaximumLength, nameof(workflowExecutionId));
        ValidateBound(bookmarkId, BookmarkStateEfModule.BookmarkIdentityMaximumLength, nameof(bookmarkId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateId(scope, workflowExecutionId, bookmarkId);
        try
        {
            var row = await context.Bookmarks.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            return row is null ? null : MapChecked(row, scope, workflowExecutionId, bookmarkId, id);
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("finding", workflowExecutionId, exception);
        }
    }

    public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(BookmarkStatePageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        ValidateBound(query.WorkflowExecutionId, BookmarkStateEfModule.WorkflowIdentityMaximumLength, nameof(query.WorkflowExecutionId));
        var cursor = DecodeCursor(query.ContinuationToken, CursorKind.Workflow, scope, query.WorkflowExecutionId);
        return ReadPage(
            context.Bookmarks.AsNoTracking()
                .Where(row => row.ScopeKeyHash == Hash(scope) && row.WorkflowExecutionIdHash == Hash(query.WorkflowExecutionId) &&
                              (cursor == null || row.BookmarkIdOrderKey.CompareTo(cursor.BookmarkOrderKey) > 0))
                .OrderBy(row => row.BookmarkIdOrderKey)
                .Take(query.Limit + 1),
            query,
            scope,
            CursorKind.Workflow,
            query.WorkflowExecutionId,
            cancellationToken);
    }

    public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(BookmarkStimulusPageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        ValidateBound(query.StimulusType, BookmarkStateEfModule.StimulusTypeMaximumLength, nameof(query.StimulusType));
        ValidateBound(query.StimulusHash, BookmarkStateEfModule.StimulusHashMaximumLength, nameof(query.StimulusHash));
        var lookup = StimulusLookupKey(query.StimulusType, query.StimulusHash);
        var binding = StimulusBinding(query.StimulusType, query.StimulusHash);
        var cursor = DecodeCursor(query.ContinuationToken, CursorKind.Stimulus, scope, binding);
        return ReadPage(
            context.Bookmarks.AsNoTracking()
                .Where(row => row.ScopeKeyHash == Hash(scope) && row.StimulusLookupKey == lookup &&
                              (cursor == null || row.WorkflowExecutionIdOrderKey.CompareTo(cursor.WorkflowOrderKey) > 0 ||
                               (row.WorkflowExecutionIdOrderKey == cursor.WorkflowOrderKey && row.BookmarkIdOrderKey.CompareTo(cursor.BookmarkOrderKey) > 0)))
                .OrderBy(row => row.WorkflowExecutionIdOrderKey)
                .ThenBy(row => row.BookmarkIdOrderKey)
                .Take(query.Limit + 1),
            query,
            scope,
            CursorKind.Stimulus,
            binding,
            cancellationToken);
    }

    public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusTypePageAsync(BookmarkStimulusTypePageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        ValidateBound(query.StimulusType, BookmarkStateEfModule.StimulusTypeMaximumLength, nameof(query.StimulusType));
        var lookup = StimulusTypeLookupKey(query.StimulusType);
        var cursor = DecodeCursor(query.ContinuationToken, CursorKind.StimulusType, scope, query.StimulusType);
        return ReadPage(
            context.Bookmarks.AsNoTracking()
                .Where(row => row.ScopeKeyHash == Hash(scope) && row.StimulusTypeLookupKey == lookup &&
                              (cursor == null || row.WorkflowExecutionIdOrderKey.CompareTo(cursor.WorkflowOrderKey) > 0 ||
                               (row.WorkflowExecutionIdOrderKey == cursor.WorkflowOrderKey && row.BookmarkIdOrderKey.CompareTo(cursor.BookmarkOrderKey) > 0)))
                .OrderBy(row => row.WorkflowExecutionIdOrderKey)
                .ThenBy(row => row.BookmarkIdOrderKey)
                .Take(query.Limit + 1),
            query,
            scope,
            CursorKind.StimulusType,
            query.StimulusType,
            cancellationToken);
    }

    private async ValueTask<RuntimeStorePage<BookmarkState>> ReadPage(
        IQueryable<BookmarkStateEntity> source,
        RuntimeStorePageRequest query,
        string scope,
        CursorKind kind,
        string binding,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await source.ToArrayAsync(cancellationToken);
            var hasMore = rows.Length > query.Limit;
            if (hasMore)
                rows = rows[..query.Limit];
            var items = rows.Select(row => MapChecked(row, scope)).ToArray();
            foreach (var item in items)
            {
                if ((kind == CursorKind.Workflow && !StringComparer.Ordinal.Equals(item.WorkflowExecutionId, binding)) ||
                    (kind == CursorKind.Stimulus && !StringComparer.Ordinal.Equals(StimulusBinding(item.StimulusType, item.StimulusHash), binding)) ||
                    (kind == CursorKind.StimulusType && !StringComparer.Ordinal.Equals(item.StimulusType, binding)))
                    throw new InvalidDataException("The persisted EF bookmark row does not match the requested projection.");
            }
            var next = hasMore
                ? EncodeCursor(new BookmarkCursor(1, kind, Hash(scope), binding, rows[^1].WorkflowExecutionId, rows[^1].WorkflowExecutionIdOrderKey, rows[^1].BookmarkId, rows[^1].BookmarkIdOrderKey))
                : null;
            return new RuntimeStorePage<BookmarkState>(query, items, next);
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("listing", binding, exception);
        }
    }

    private string RequireScope()
    {
        var current = accessContextAccessor.Current;
        if (current.AccessPolicy != PersistenceAccessPolicy.Ordinary || current.Scope is null || current.AcrossScopes)
            throw new InvalidOperationException("EF bookmark persistence requires one explicit persistence scope.");
        return current.Scope.Value;
    }

    private static BookmarkStateEntity ToEntity(BookmarkState state, string scope, string id, long revision)
    {
        var row = new BookmarkStateEntity { Id = id };
        CopyToEntity(row, state, scope, id, revision);
        return row;
    }

    // R01's schema only has a numeric concurrency token. Use a fresh positive token for
    // each insertion so a stale delete cannot match a delete-and-recreate successor that
    // happens to reuse the same logical key and revision sequence.
    private static long NewRevision()
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToInt64(bytes) & (long.MaxValue >> 1);
        return value == 0 ? 1 : value;
    }

    private static void CopyToEntity(BookmarkStateEntity row, BookmarkState state, string scope, string id, long revision)
    {
        row.Id = id;
        row.ScopeKey = EfRelationalIdentity.Encode(scope);
        row.ScopeKeyHash = Hash(scope);
        row.WorkflowExecutionId = state.WorkflowExecutionId;
        row.WorkflowExecutionIdHash = Hash(state.WorkflowExecutionId);
        row.WorkflowExecutionIdOrderKey = OrdinalKey(state.WorkflowExecutionId);
        row.BookmarkId = state.BookmarkId;
        row.BookmarkIdHash = Hash(state.BookmarkId);
        row.BookmarkIdOrderKey = OrdinalKey(state.BookmarkId);
        row.ActivityExecutionId = state.ActivityExecutionId;
        row.ExecutableNodeId = state.ExecutableNodeId;
        row.ResumeTargetId = state.ResumeTargetId;
        row.StimulusType = state.StimulusType;
        row.StimulusHash = state.StimulusHash;
        row.StimulusLookupKey = StimulusLookupKey(state.StimulusType, state.StimulusHash);
        row.StimulusTypeLookupKey = StimulusTypeLookupKey(state.StimulusType);
        row.PayloadJson = SerializePayload(state.Payload);
        row.ContentJson = JsonSerializer.Serialize(state, Json);
        row.MetadataJson = JsonSerializer.Serialize(state.Metadata, Json);
        row.SchemaVersion = BookmarkStateEfModule.SchemaVersion;
        row.CreatedAtUtcTicks = state.CreatedAt.UtcTicks;
        row.CreatedAtOffsetMinutes = checked((int)state.CreatedAt.Offset.TotalMinutes);
        row.ExpiresAtUtcTicks = state.ExpiresAt?.UtcTicks;
        row.ExpiresAtOffsetMinutes = state.ExpiresAt is null ? null : checked((int)state.ExpiresAt.Value.Offset.TotalMinutes);
        row.Revision = revision;
    }

    private static BookmarkState MapChecked(BookmarkStateEntity row, string scope, string? expectedWorkflow = null, string? expectedBookmark = null, string? expectedId = null)
    {
        try
        {
            if (row.SchemaVersion != BookmarkStateEfModule.SchemaVersion || EfRelationalIdentity.Decode(row.ScopeKey) != scope || row.ScopeKeyHash != Hash(scope) ||
                row.Id != (expectedId ?? CreateId(scope, row.WorkflowExecutionId, row.BookmarkId)) ||
                row.WorkflowExecutionIdHash != Hash(row.WorkflowExecutionId) || row.BookmarkIdHash != Hash(row.BookmarkId) ||
                row.WorkflowExecutionIdOrderKey != OrdinalKey(row.WorkflowExecutionId) || row.BookmarkIdOrderKey != OrdinalKey(row.BookmarkId) ||
                (expectedWorkflow is not null && row.WorkflowExecutionId != expectedWorkflow) ||
                (expectedBookmark is not null && row.BookmarkId != expectedBookmark) || string.IsNullOrWhiteSpace(row.MetadataJson) || row.Revision <= 0)
                throw new InvalidDataException("The persisted EF bookmark row is corrupt.");

            var state = JsonSerializer.Deserialize<BookmarkState>(row.ContentJson, Json)
                        ?? throw new JsonException("Bookmark content was null.");
            var projectedPayload = DeserializePayload(row.PayloadJson);
            var contentPayloadMatchesProjection = SerializePayload(state.Payload) == row.PayloadJson ||
                                                  state.Payload is null && projectedPayload is { ValueKind: JsonValueKind.Null };
            var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(row.MetadataJson, Json)
                           ?? throw new JsonException("Bookmark metadata was null.");
            if (state.BookmarkId != row.BookmarkId || state.WorkflowExecutionId != row.WorkflowExecutionId ||
                state.ActivityExecutionId != row.ActivityExecutionId || state.ExecutableNodeId != row.ExecutableNodeId ||
                state.ResumeTargetId != row.ResumeTargetId || state.StimulusType != row.StimulusType || state.StimulusHash != row.StimulusHash ||
                !contentPayloadMatchesProjection || SerializePayload(projectedPayload) != row.PayloadJson ||
                JsonSerializer.Serialize(state.Metadata, Json) != row.MetadataJson ||
                state.CreatedAt.UtcTicks != row.CreatedAtUtcTicks || state.CreatedAt.Offset.TotalMinutes != row.CreatedAtOffsetMinutes ||
                state.ExpiresAt?.UtcTicks != row.ExpiresAtUtcTicks ||
                state.ExpiresAt?.Offset.TotalMinutes != row.ExpiresAtOffsetMinutes)
                throw new InvalidDataException("The persisted EF bookmark projection is corrupt.");
            ValidateState(state);
            if (row.StimulusLookupKey != StimulusLookupKey(state.StimulusType, state.StimulusHash) ||
                row.StimulusTypeLookupKey != StimulusTypeLookupKey(state.StimulusType))
                throw new InvalidDataException("The persisted EF bookmark stimulus projection is corrupt.");
            return state with { Payload = projectedPayload };
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or OverflowException or FormatException)
        {
            throw new InvalidDataException("The persisted EF bookmark payload is not valid current JSON.", exception);
        }
    }

    private static void ValidateState(BookmarkState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateBound(state.WorkflowExecutionId, BookmarkStateEfModule.WorkflowIdentityMaximumLength, nameof(state.WorkflowExecutionId));
        ValidateBound(state.BookmarkId, BookmarkStateEfModule.BookmarkIdentityMaximumLength, nameof(state.BookmarkId));
        ValidateBound(state.ActivityExecutionId, BookmarkStateEfModule.WorkflowIdentityMaximumLength, nameof(state.ActivityExecutionId));
        ValidateBound(state.ExecutableNodeId, BookmarkStateEfModule.WorkflowIdentityMaximumLength, nameof(state.ExecutableNodeId));
        ValidateBound(state.ResumeTargetId, BookmarkStateEfModule.WorkflowIdentityMaximumLength, nameof(state.ResumeTargetId));
        ValidateBound(state.StimulusType, BookmarkStateEfModule.StimulusTypeMaximumLength, nameof(state.StimulusType));
        ValidateBound(state.StimulusHash, BookmarkStateEfModule.StimulusHashMaximumLength, nameof(state.StimulusHash));
        ArgumentNullException.ThrowIfNull(state.Metadata);
        if (state.Payload is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("Bookmark payload cannot be undefined.", nameof(state));
        foreach (var pair in state.Metadata)
        {
            ValidateBound(pair.Key, BookmarkStateEfModule.ProjectionMaximumLength, nameof(state.Metadata));
            ArgumentNullException.ThrowIfNull(pair.Value);
        }
        _ = CreateId("scope", state.WorkflowExecutionId, state.BookmarkId);
    }

    private static string? SerializePayload(JsonElement? payload) =>
        payload is null ? null : JsonSerializer.Serialize(payload.Value, Json);

    private static JsonElement? DeserializePayload(string? payloadJson) =>
        payloadJson is null ? null : JsonSerializer.Deserialize<JsonElement>(payloadJson, Json);

    private static void ValidateBound(string value, int maximum, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximum)
            throw new ArgumentException($"The {parameterName} value cannot exceed {maximum} characters.", parameterName);
    }

    private static string CreateId(string scope, string workflowExecutionId, string bookmarkId) =>
        Hash($"{scope.Length}:{scope}{workflowExecutionId.Length}:{workflowExecutionId}{bookmarkId.Length}:{bookmarkId}");

    private static string StimulusLookupKey(string stimulusType, string stimulusHash) => Hash($"{stimulusType.Length}:{stimulusType}{stimulusHash}");
    private static string StimulusBinding(string stimulusType, string stimulusHash) =>
        $"{stimulusType.Length}:{stimulusType}{stimulusHash.Length}:{stimulusHash}";
    private static string StimulusTypeLookupKey(string stimulusType) => Hash(stimulusType);
    private static string Hash(string value) => EfRelationalIdentity.Hash(value);
    private static string OrdinalKey(string value)
    {
        // Use fixed-width decimal code-unit blocks. Unlike provider-collated raw
        // strings, digits sort identically across the supported relational
        // providers; padding makes ordinal prefix ordering correct.
        const int codeUnitWidth = 5;
        const int lengthWidth = 5;
        var builder = new StringBuilder(BookmarkStateEfModule.OrdinalOrderKeyMaximumLength);
        for (var index = 0; index < value.Length; index++)
            builder.Append(((int)value[index]).ToString($"D{codeUnitWidth}", System.Globalization.CultureInfo.InvariantCulture));
        builder.Append('0', checked((BookmarkStateEfModule.WorkflowIdentityMaximumLength - value.Length) * codeUnitWidth));
        builder.Append(value.Length.ToString($"D{lengthWidth}", System.Globalization.CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static string EncodeCursor(BookmarkCursor cursor) => Convert.ToBase64String(Utf8.GetBytes(JsonSerializer.Serialize(cursor, Json)));

    private static BookmarkCursor? DecodeCursor(string? token, CursorKind kind, string scope, string binding)
    {
        if (token is null) return null;
        try
        {
            var cursor = JsonSerializer.Deserialize<BookmarkCursor>(Utf8.GetString(Convert.FromBase64String(token)), Json);
            if (cursor is null || cursor.Version != 1 || cursor.Kind != kind || cursor.ScopeHash != Hash(scope) || cursor.Binding != binding ||
                (kind == CursorKind.Workflow && cursor.WorkflowExecutionId != binding) ||
                string.IsNullOrWhiteSpace(cursor.WorkflowExecutionId) || string.IsNullOrWhiteSpace(cursor.BookmarkId) ||
                cursor.WorkflowOrderKey != OrdinalKey(cursor.WorkflowExecutionId) || cursor.BookmarkOrderKey != OrdinalKey(cursor.BookmarkId))
                throw new FormatException();
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or JsonException or DecoderFallbackException or NotSupportedException)
        {
            throw new ArgumentException("The bookmark continuation token is invalid.", nameof(token), exception);
        }
    }

    private static bool IsProviderFailure(Exception exception) =>
        exception is not (InvalidDataException or OperationCanceledException) &&
        exception is (DbException or DbUpdateException or InvalidOperationException);

    private static BookmarkStateEntityFrameworkPersistenceException NormalizeProviderFailure(
        string operation,
        string identity,
        Exception inner) =>
        new(operation, identity, $"The EF runtime bookmark store failed while {operation} bookmark state '{identity}'.", inner);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private enum CursorKind { Workflow, Stimulus, StimulusType }
    private sealed record BookmarkCursor(int Version, CursorKind Kind, string ScopeHash, string Binding, string WorkflowExecutionId, string WorkflowOrderKey, string BookmarkId, string BookmarkOrderKey);
}
