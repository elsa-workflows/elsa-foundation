using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Concrete EF adapter for durable workflow execution state (R10).</summary>
public sealed class EfWorkflowExecutionStateStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IWorkflowExecutionStateStore
{
    private const string HistoryCursorPurpose = "ef-runtime-workflow-execution-history-v1";
    private const string CaptureCursorPurpose = "ef-runtime-workflow-execution-capture-v1";

    public async ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        Validate(state);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        accessContextAccessor.Current.EnsureTenantScope(state.TenantId);
        var id = CreateId(scope, state.WorkflowExecutionId);
        context.ChangeTracker.Clear();
        try
        {
            var row = await context.WorkflowExecutionStates.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (row is null)
                context.WorkflowExecutionStates.Add(ToEntity(state, scope, id, NewRevision()));
            else
            {
                _ = ReadChecked(row, scope, state.WorkflowExecutionId);
                CopyToEntity(row, state, scope, id, checked(row.Revision + 1));
            }

            await context.SaveChangesAsync(cancellationToken);
            return state;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The workflow execution state changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey | EfWriteConflict.Transient))
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The workflow execution state changed concurrently; retry the operation.", exception);
        }
        catch (OperationCanceledException) { context.ChangeTracker.Clear(); throw; }
        catch (InvalidDataException) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { context.ChangeTracker.Clear(); throw Normalize("saving", state.WorkflowExecutionId, exception); }
    }

    public async ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var scope = RequireScope();
            var id = CreateId(scope, workflowExecutionId);
            var row = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            return row is null ? null : ReadChecked(row, scope, workflowExecutionId);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { throw Normalize("reading", workflowExecutionId, exception); }
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new List<WorkflowExecutionState>();
            string? cursor = null;
            do
            {
                var page = await QueryPageAsync(new WorkflowExecutionStatePageQuery(WorkflowExecutionStatePaging.MaximumPageSize, Cursor: cursor), cancellationToken);
                result.AddRange(page.Items);
                cursor = page.NextCursor;
            } while (cursor is not null);
            return result;
        }
        // QueryPageAsync already normalizes its own provider failures; rethrow that type before the
        // classifier below, which would otherwise match the normalized failure too and wrap it again.
        catch (WorkflowExecutionStateEntityFrameworkPersistenceException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { throw Normalize("listing", "<all>", exception); }
    }

    public async ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        ValidatePageSize(query.PageSize);
        if (query.TenantId is not null)
            ValidateTenant(query.TenantId, nameof(query.TenantId));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var scope = RequireScope();
            var cursor = DecodeHistoryCursor(query.Cursor, query, scope);
            // Decode first so a cursor bound to another filter is reported as
            // cursor misuse; a tenant mismatch without a cursor still fails
            // closed below before any query is executed.
            accessContextAccessor.Current.EnsureTenantScope(query.TenantId);
            var source = HistoryQuery(query, scope);
            var total = await source.LongCountAsync(cancellationToken);
            if (cursor is not null)
                source = source.Where(x => x.SortTimestampUtcTicks < cursor.SortTicks || x.SortTimestampUtcTicks == cursor.SortTicks && string.Compare(x.WorkflowExecutionIdOrderKey, cursor.OrderKey) > 0);
            var rows = await source.OrderByDescending(x => x.SortTimestampUtcTicks).ThenBy(x => x.WorkflowExecutionIdOrderKey).Take(checked(query.PageSize + 1)).ToArrayAsync(cancellationToken);
            var hasNext = rows.Length > query.PageSize;
            if (hasNext) rows = rows[..query.PageSize];
            var items = rows.Select(x => ReadChecked(x, scope, Decode(x.WorkflowExecutionId))).ToArray();
            return new WorkflowExecutionStatePage(items,
                hasNext && rows.Length > 0 ? EncodeHistoryCursor(rows[^1], query, scope) : null,
                hasNext, total);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { throw Normalize("querying", "<history>", exception); }
    }

    public async ValueTask<WorkflowExecutionAlterationCapturePage> QueryAlterationCapturePageAsync(WorkflowExecutionAlterationCaptureQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        ValidatePageSize(query.PageSize);
        ValidateTenant(query.TenantPartition, nameof(query.TenantPartition));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var scope = RequireScope();
            if (!StringComparer.Ordinal.Equals(scope, query.TenantPartition))
                throw new InvalidOperationException("The requested resource does not belong to the current persistence scope.");
            var cursor = DecodeCaptureCursor(query.Cursor, query, scope);
            var source = context.WorkflowExecutionStates.AsNoTracking()
                .Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) &&
                            x.TenantIdHash == Hash(query.TenantPartition) && x.TenantId == Encode(query.TenantPartition) &&
                            x.AuthorityPartitionKey == query.AuthorityPartitionKey);
            source = ApplySelector(source, query.Selector);
            if (cursor is not null)
                source = source.Where(x => string.Compare(x.WorkflowExecutionIdOrderKey, cursor.OrderKey) > 0);
            var rows = await source.OrderBy(x => x.WorkflowExecutionIdOrderKey).Take(checked(query.PageSize + 1)).ToArrayAsync(cancellationToken);
            var hasNext = rows.Length > query.PageSize;
            if (hasNext) rows = rows[..query.PageSize];
            var items = rows.Select(x =>
            {
                var state = ReadChecked(x, scope, Decode(x.WorkflowExecutionId));
                if (state.Authority is not { } authority || !WorkflowExecutionAuthoritySnapshot.Matches(authority, query.SystemIdentity, query.RootInitiator, query.AuthorityMetadata))
                    throw new InvalidDataException("The persisted workflow execution authority projection does not match its content.");
                return state;
            }).ToArray();
            return new WorkflowExecutionAlterationCapturePage(items,
                hasNext && rows.Length > 0 ? EncodeCaptureCursor(rows[^1], query, scope) : null, hasNext);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { throw Normalize("querying", "<alteration-capture>", exception); }
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var scope = RequireScope();
            var rows = await context.WorkflowExecutionStates.AsNoTracking()
                .Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope))
                .Select(x => new { x.ArtifactId, x.ArtifactIdHash, x.ArtifactIdOrderKey })
                .Distinct()
                .OrderBy(x => x.ArtifactIdOrderKey)
                .ToArrayAsync(cancellationToken);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ids = new List<string>(rows.Length);
            foreach (var row in rows)
            {
                var id = Decode(row.ArtifactId);
                if (row.ArtifactIdHash != Hash(id) || row.ArtifactIdOrderKey != OrderKey(id)) throw new InvalidDataException("The persisted workflow execution artifact projection is corrupt.");
                if (seen.Add(id)) ids.Add(id);
            }
            return ids.ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { throw Normalize("listing pinned artifacts for", "<all>", exception); }
    }

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateId(scope, workflowExecutionId);
        try
        {
            var row = await context.WorkflowExecutionStates.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (row is null) return false;
            _ = ReadChecked(row, scope, workflowExecutionId);
            context.WorkflowExecutionStates.Remove(row);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Transient)) { context.ChangeTracker.Clear(); throw Normalize("deleting", workflowExecutionId, exception); }
        catch (OperationCanceledException) { context.ChangeTracker.Clear(); throw; }
        catch (InvalidDataException) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception)) { context.ChangeTracker.Clear(); throw Normalize("deleting", workflowExecutionId, exception); }
    }

    private IQueryable<WorkflowExecutionStateEntity> HistoryQuery(WorkflowExecutionStatePageQuery query, string scope)
    {
        var source = context.WorkflowExecutionStates.AsNoTracking().Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope));
        if (query.TenantId is not null) source = source.Where(x => x.TenantIdHash == Hash(query.TenantId) && x.TenantId == Encode(query.TenantId));
        if (query.DefinitionId is not null) source = source.Where(x => x.DefinitionIdHash == Hash(query.DefinitionId) && x.DefinitionId == Encode(query.DefinitionId));
        if (query.Status is { } status) source = source.Where(x => x.Status == (int)status);
        if (query.RunKind is { } runKind) source = source.Where(x => x.RunKind == (int)runKind);
        if (query.CorrelationId is not null) source = source.Where(x => x.CorrelationIdHash == Hash(query.CorrelationId) && x.CorrelationId == Encode(query.CorrelationId));
        if (query.WorkflowExecutionId is not null) source = source.Where(x => x.WorkflowExecutionIdHash == Hash(query.WorkflowExecutionId) && x.WorkflowExecutionId == Encode(query.WorkflowExecutionId));
        if (query.ArtifactId is not null) source = source.Where(x => x.ArtifactIdHash == Hash(query.ArtifactId) && x.ArtifactId == Encode(query.ArtifactId));
        if (query.From is { } from) source = source.Where(x => x.SortTimestampUtcTicks >= from.UtcTicks);
        if (query.To is { } to) source = source.Where(x => x.SortTimestampUtcTicks <= to.UtcTicks);
        return source;
    }

    private static IQueryable<WorkflowExecutionStateEntity> ApplySelector(IQueryable<WorkflowExecutionStateEntity> source, WorkflowAlterationQuerySelector selector)
    {
        if (selector.DefinitionId is not null) source = source.Where(x => x.DefinitionIdHash == Hash(selector.DefinitionId) && x.DefinitionId == Encode(selector.DefinitionId));
        if (selector.Status is { } status) source = source.Where(x => x.Status == (int)status);
        if (selector.RunKind is { } runKind) source = source.Where(x => x.RunKind == (int)runKind);
        if (selector.CorrelationId is not null) source = source.Where(x => x.CorrelationIdHash == Hash(selector.CorrelationId) && x.CorrelationId == Encode(selector.CorrelationId));
        if (selector.WorkflowExecutionId is not null) source = source.Where(x => x.WorkflowExecutionIdHash == Hash(selector.WorkflowExecutionId) && x.WorkflowExecutionId == Encode(selector.WorkflowExecutionId));
        if (selector.ArtifactId is not null) source = source.Where(x => x.ArtifactIdHash == Hash(selector.ArtifactId) && x.ArtifactId == Encode(selector.ArtifactId));
        if (selector.From is { } from) source = source.Where(x => x.SortTimestampUtcTicks >= from.UtcTicks);
        if (selector.To is { } to) source = source.Where(x => x.SortTimestampUtcTicks <= to.UtcTicks);
        return source;
    }

    // Internal checkpoint staging reuses the same projection as the direct store. Keeping this seam internal
    // prevents the atomic writer from growing a second, subtly different workflow-execution envelope.
    internal static WorkflowExecutionStateEntity ToEntity(WorkflowExecutionState state, string scope, string id, long revision)
    {
        var row = new WorkflowExecutionStateEntity();
        CopyToEntity(row, state, scope, id, revision);
        return row;
    }

    internal static void CopyToEntity(WorkflowExecutionStateEntity row, WorkflowExecutionState state, string scope, string id, long revision)
    {
        var timestamp = WorkflowExecutionStateHistory.SortTimestamp(state);
        var tenant = state.TenantId;
        var correlation = state.CorrelationId;
        var definition = state.PinnedSource?.DefinitionId ?? state.PinnedExecutable.DefinitionId;
        row.Id = id; row.ScopeKey = Encode(scope); row.ScopeKeyHash = Hash(scope);
        row.WorkflowExecutionId = Encode(state.WorkflowExecutionId); row.WorkflowExecutionIdHash = Hash(state.WorkflowExecutionId); row.WorkflowExecutionIdOrderKey = OrderKey(state.WorkflowExecutionId);
        row.TenantId = tenant is null ? null : Encode(tenant); row.TenantIdHash = tenant is null ? null : Hash(tenant);
        row.DefinitionId = Encode(definition); row.DefinitionIdHash = Hash(definition); row.Status = (int)state.Status; row.RunKind = (int)state.RunKind; row.SortTimestampUtcTicks = timestamp.UtcTicks;
        row.CorrelationId = correlation is null ? null : Encode(correlation); row.CorrelationIdHash = correlation is null ? null : Hash(correlation);
        row.ArtifactId = Encode(state.PinnedExecutable.ArtifactId); row.ArtifactIdHash = Hash(state.PinnedExecutable.ArtifactId); row.ArtifactIdOrderKey = OrderKey(state.PinnedExecutable.ArtifactId);
        row.AuthorityPartitionKey = state.Authority is { } authority ? WorkflowExecutionAuthoritySnapshot.PartitionKey(authority.SystemIdentity, authority.RootInitiator, authority.Metadata) : null;
        row.ContentJson = RuntimeArtifactJson.Serialize(state); row.SchemaVersion = RuntimeWorkflowExecutionEfModule.SchemaVersion; row.Revision = revision;
    }

    internal static WorkflowExecutionState ReadChecked(WorkflowExecutionStateEntity row, string scope, string expectedId)
    {
        try
        {
            return ReadCheckedCore(row, scope, expectedId);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted workflow execution state is not valid current data.", exception);
        }
    }

    internal static string CreateIdForAtomicParticipant(string scope, string id) => CreateId(scope, id);

    private static WorkflowExecutionState ReadCheckedCore(WorkflowExecutionStateEntity row, string scope, string expectedId)
    {
        if (EfSchemaVersion.NotReadable("RuntimeWorkflowExecution", row.SchemaVersion, RuntimeWorkflowExecutionEfModule.SchemaVersion) || row.Id != CreateId(scope, expectedId) || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) || row.Revision <= 0)
            throw new InvalidDataException("The persisted workflow execution state envelope is corrupt.");
        WorkflowExecutionState state;
        try
        {
            state = RuntimeArtifactJson.Deserialize<WorkflowExecutionState>(row.ContentJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted workflow execution state content is not valid current JSON.", exception);
        }
        Validate(state);
        if (state.TenantId is not null && !StringComparer.Ordinal.Equals(state.TenantId, scope))
            throw new InvalidDataException("The persisted workflow execution tenant does not match its persistence scope.");
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, expectedId) || !StringComparer.Ordinal.Equals(row.WorkflowExecutionId, Encode(expectedId)) || row.WorkflowExecutionIdHash != Hash(expectedId) || row.WorkflowExecutionIdOrderKey != OrderKey(expectedId))
            throw new InvalidDataException("The persisted workflow execution identity projection does not match its content.");
        var definition = state.PinnedSource?.DefinitionId ?? state.PinnedExecutable.DefinitionId;
        if (row.DefinitionId != Encode(definition) || row.DefinitionIdHash != Hash(definition) || row.Status != (int)state.Status || row.RunKind != (int)state.RunKind || row.SortTimestampUtcTicks != WorkflowExecutionStateHistory.SortTimestamp(state).UtcTicks)
            throw new InvalidDataException("The persisted workflow execution history projection does not match its content.");
        EnsureOptional(row.TenantId, row.TenantIdHash, state.TenantId); EnsureOptional(row.CorrelationId, row.CorrelationIdHash, state.CorrelationId);
        if (row.ArtifactId != Encode(state.PinnedExecutable.ArtifactId) || row.ArtifactIdHash != Hash(state.PinnedExecutable.ArtifactId) || row.ArtifactIdOrderKey != OrderKey(state.PinnedExecutable.ArtifactId))
            throw new InvalidDataException("The persisted workflow execution artifact projection does not match its content.");
        var authorityKey = state.Authority is { } authority ? WorkflowExecutionAuthoritySnapshot.PartitionKey(authority.SystemIdentity, authority.RootInitiator, authority.Metadata) : null;
        if (row.AuthorityPartitionKey != authorityKey)
            throw new InvalidDataException("The persisted workflow execution authority projection does not match its content.");
        return state;
    }

    private static void EnsureOptional(string? encoded, string? hash, string? value)
    {
        if ((encoded is null) != (value is null) || (hash is null) != (value is null) || value is not null && (encoded != Encode(value) || hash != Hash(value)))
            throw new InvalidDataException("The persisted workflow execution optional projection is corrupt.");
    }

    private static void Validate(WorkflowExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state); ValidateIdentity(state.WorkflowExecutionId, nameof(state.WorkflowExecutionId)); ArgumentNullException.ThrowIfNull(state.PinnedExecutable);
        ValidateIdentity(state.PinnedExecutable.ArtifactId, nameof(state.PinnedExecutable.ArtifactId)); ValidateIdentity(state.PinnedExecutable.DefinitionId, nameof(state.PinnedExecutable.DefinitionId));
        if (state.PinnedSource is { } source) ValidateIdentity(source.DefinitionId, nameof(source.DefinitionId));
        if (state.TenantId is not null) ValidateTenant(state.TenantId, nameof(state.TenantId)); if (state.CorrelationId is not null) ValidateIdentity(state.CorrelationId, nameof(state.CorrelationId));
        if (!Enum.IsDefined(state.Status) || !Enum.IsDefined(state.RunKind)) throw new InvalidDataException("The persisted workflow execution state contains an undefined enum value.");
    }

    private string RequireScope()
    {
        var scope = accessContextAccessor.Current.RequireScope().Value;
        ValidateTenant(scope, nameof(scope));
        return scope;
    }

    private static void ValidateIdentity(string value, string name) { ArgumentException.ThrowIfNullOrWhiteSpace(value); if (value.Length > RuntimeWorkflowExecutionEfModule.IdentityMaximumLength) throw new ArgumentException($"The {name} value cannot exceed {RuntimeWorkflowExecutionEfModule.IdentityMaximumLength} characters.", name); }
    private static void ValidateTenant(string value, string name) { ArgumentException.ThrowIfNullOrWhiteSpace(value); if (value.Length > RuntimeWorkflowExecutionEfModule.TenantMaximumLength) throw new ArgumentException($"The {name} value cannot exceed {RuntimeWorkflowExecutionEfModule.TenantMaximumLength} characters.", name); }
    private static void ValidatePageSize(int pageSize) { if (pageSize > WorkflowExecutionStatePaging.MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, $"Page size cannot exceed {WorkflowExecutionStatePaging.MaximumPageSize} rows."); }
    private static string Encode(string value) => EfRelationalIdentity.Encode(value);
    private static string Decode(string value)
    {
        try { return EfRelationalIdentity.Decode(value); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("The persisted workflow execution identity projection is not valid.", exception);
        }
    }
    private static string Hash(string value) => EfRelationalIdentity.Hash(value);
    private static string OrderKey(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeWorkflowExecutionEfModule.IdentityMaximumLength));
    // Length framing makes the composite identity unambiguous; EfRelationalIdentity hashes the
    // UTF-16 code units directly so distinct opaque values (including lone surrogates) remain distinct.
    // The checkpoint staging seam must use the same framed identity as direct workflow-execution persistence.
    internal static string CreateId(string scope, string id) => Hash($"workflow-execution:{scope.Length}:{scope}{id.Length}:{id}");
    private static long NewRevision() { Span<byte> bytes = stackalloc byte[8]; RandomNumberGenerator.Fill(bytes); var result = BitConverter.ToInt64(bytes) & (long.MaxValue >> 1); return result == 0 ? 1 : result; }
    private static WorkflowExecutionStateEntityFrameworkPersistenceException Normalize(string action, string id, Exception exception) =>
        new(action, id, $"EF workflow execution state {action} failed for '{id}'.", exception);

    private HistoryCursor? DecodeHistoryCursor(string? token, WorkflowExecutionStatePageQuery query, string scope)
    {
        if (token is null) return null;
        try { var cursor = RuntimeArtifactJson.Deserialize<HistoryCursor>(Encoding.UTF8.GetString(continuationCodec.Decode(HistoryCursorPurpose, token))); if (cursor.Version != 1 || cursor.ScopeHash != Hash(scope) || cursor.FilterScope != WorkflowExecutionStateHistory.Scope(query) || cursor.OrderKey != OrderKey(cursor.ExecutionId)) throw new FormatException(); return cursor; }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or InvalidDataException or InvalidOperationException) { throw new ArgumentException("The workflow execution history cursor is invalid or does not belong to this query.", nameof(token), ex); }
    }
    private string EncodeHistoryCursor(WorkflowExecutionStateEntity row, WorkflowExecutionStatePageQuery query, string scope) => continuationCodec.Encode(HistoryCursorPurpose, Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(new HistoryCursor(1, Hash(scope), WorkflowExecutionStateHistory.Scope(query), Decode(row.WorkflowExecutionId), row.SortTimestampUtcTicks, row.WorkflowExecutionIdOrderKey))));
    private CaptureCursor? DecodeCaptureCursor(string? token, WorkflowExecutionAlterationCaptureQuery query, string scope)
    {
        if (token is null) return null;
        try { var cursor = RuntimeArtifactJson.Deserialize<CaptureCursor>(Encoding.UTF8.GetString(continuationCodec.Decode(CaptureCursorPurpose, token))); if (cursor.Version != 1 || cursor.ScopeHash != Hash(scope) || cursor.FilterScope != CaptureScope(query) || cursor.OrderKey != OrderKey(cursor.ExecutionId)) throw new FormatException(); return cursor; }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or InvalidDataException or InvalidOperationException) { throw new ArgumentException("The alteration capture cursor is invalid or does not belong to this query.", nameof(token), ex); }
    }
    private string EncodeCaptureCursor(WorkflowExecutionStateEntity row, WorkflowExecutionAlterationCaptureQuery query, string scope) => continuationCodec.Encode(CaptureCursorPurpose, Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(new CaptureCursor(1, Hash(scope), CaptureScope(query), Decode(row.WorkflowExecutionId), row.WorkflowExecutionIdOrderKey))));
    private static string CaptureScope(WorkflowExecutionAlterationCaptureQuery query) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(new { query.TenantPartition, query.SystemIdentity, query.RootInitiator, query.AuthorityPartitionKey, Metadata = query.AuthorityMetadata.OrderBy(x => x.Key, StringComparer.Ordinal), query.Selector.DefinitionId, query.Selector.Status, query.Selector.RunKind, From = query.Selector.From?.UtcTicks, To = query.Selector.To?.UtcTicks, query.Selector.CorrelationId, query.Selector.WorkflowExecutionId, query.Selector.ArtifactId, query.Selector.MatchAllAuthorized }))));
    private sealed record HistoryCursor(int Version, string ScopeHash, string FilterScope, string ExecutionId, long SortTicks, string OrderKey);
    private sealed record CaptureCursor(int Version, string ScopeHash, string FilterScope, string ExecutionId, string OrderKey);
}

internal static class WorkflowExecutionStatePaging
{
    public const int MaximumPageSize = 500;
}
