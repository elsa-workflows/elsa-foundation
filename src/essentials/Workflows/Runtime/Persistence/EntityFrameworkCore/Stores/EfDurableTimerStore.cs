using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core durable timer store with provider-neutral due ordering and fenced claims.</summary>
/// <remarks>
/// Timer identity is scoped by the pair (workflow execution ID, timer ID). The JSON envelope is authoritative,
/// while the relational projections support bounded due and workflow-page queries. Every mutating transition uses
/// the row revision as an optimistic compare-and-swap token, so an expired or superseded claim cannot mutate a
/// successor timer incarnation.
/// </remarks>
public sealed class EfDurableTimerStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IDurableTimerStore
{
    private const string CursorPurpose = "ef-runtime-durable-timers-v1";
    private static readonly EfWriteRetry Transitions = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.Concurrency);

    public bool SupportsClaimTransitions => true;

    public async ValueTask<DurableTimer> SaveAsync(
        DurableTimer timer,
        CancellationToken cancellationToken = default)
    {
        ValidateTimer(timer);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, timer.WorkflowExecutionId, timer.TimerId);
        var existing = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is not null)
            return ReadChecked(existing, scope, timer.WorkflowExecutionId, timer.TimerId);

        var entity = ToEntity(timer, scope, id, 1);
        context.DurableTimers.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            Detach(entity);
            return timer;
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            Detach(entity);
            var winner = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (winner is null)
                throw new InvalidOperationException("The durable timer save conflicted, but the winning row could not be reloaded.", exception);
            return ReadChecked(winner, scope, timer.WorkflowExecutionId, timer.TimerId);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            Detach(entity);
            throw new InvalidOperationException("The durable timer save changed concurrently; retry the operation.", exception);
        }
        catch (OperationCanceledException)
        {
            Detach(entity);
            throw;
        }
    }

    public async ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default)
    {
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var rows = await context.DurableTimers.AsNoTracking()
            .Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey && x.DueTimeUtcTicks <= asOf.UtcTicks)
            .OrderBy(x => x.DueTimeUtcTicks)
            .ThenBy(x => x.TimerIdOrderKey)
            .ThenBy(x => x.WorkflowExecutionIdOrderKey)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => ReadChecked(row, scope)).ToArray();
    }

    public async ValueTask<DurableTimer?> FindAsync(
        string workflowExecutionId,
        string timerId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, timerId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, timerId);
        var row = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return row is null ? null : ReadChecked(row, scope, workflowExecutionId, timerId);
    }

    public async ValueTask<RuntimeStorePage<DurableTimer>> ListPageAsync(
        DurableTimerPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIdentity(query.WorkflowExecutionId, nameof(query.WorkflowExecutionId));
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var workflow = EfRuntimeOperationalStoreSupport.Encode(query.WorkflowExecutionId);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(query.WorkflowExecutionId);
        var source = context.DurableTimers.AsNoTracking().Where(x =>
            x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey &&
            x.WorkflowExecutionIdHash == workflowHash && x.WorkflowExecutionId == workflow);
        if (query.ContinuationToken is not null)
        {
            var cursor = EfRuntimeOperationalStoreSupport.DecodeCursor(continuationCodec, CursorPurpose, query.ContinuationToken);
            EfRuntimeOperationalStoreSupport.ValidateCursor(query, scope, query.WorkflowExecutionId, cursor);
            source = source.Where(x => string.Compare(x.TimerIdOrderKey, EfRuntimeOperationalStoreSupport.Order(cursor.Last)) > 0);
        }

        var rows = await source
            .OrderBy(x => x.TimerIdOrderKey)
            .Take(checked(query.Limit + 1))
            .ToArrayAsync(cancellationToken);
        var hasNext = rows.Length > query.Limit;
        if (hasNext)
            rows = rows[..query.Limit];
        var items = rows.Select(row => ReadChecked(row, scope, query.WorkflowExecutionId)).ToArray();
        var next = hasNext
            ? EfRuntimeOperationalStoreSupport.Cursor(continuationCodec, CursorPurpose, scope, query.WorkflowExecutionId, items[^1].TimerId)
            : null;
        return new RuntimeStorePage<DurableTimer>(query, items, next);
    }

    public async ValueTask DeleteAsync(
        string workflowExecutionId,
        string timerId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, timerId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, timerId);
        await Transitions.RunAsync(context, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (row is null)
                return;
            _ = ReadChecked(row, scope, workflowExecutionId, timerId);
            AttachForDelete(row);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is DbUpdateConcurrencyException or OperationCanceledException)
            {
                Detach(row);
                throw;
            }
        }, _ => throw TransitionDidNotSettle("delete", workflowExecutionId, timerId), cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<RuntimeDurableTimerClaim>> ClaimDueAsync(
        RuntimeDurableTimerClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdentity(request.OwnerId, nameof(request.OwnerId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var rows = await context.DurableTimers.AsNoTracking()
            .Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey &&
                        x.DueTimeUtcTicks <= request.Now.UtcTicks &&
                        (x.VisibleAfterUtcTicks == null || x.VisibleAfterUtcTicks <= request.Now.UtcTicks))
            .OrderBy(x => x.VisibleAfterUtcTicks ?? x.DueTimeUtcTicks)
            .ThenBy(x => x.DueTimeUtcTicks)
            .ThenBy(x => x.TimerIdOrderKey)
            .ThenBy(x => x.WorkflowExecutionIdOrderKey)
            .Take(request.Limit)
            .ToArrayAsync(cancellationToken);

        var claims = new List<RuntimeDurableTimerClaim>(rows.Length);
        foreach (var candidate in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.DueTimeUtcTicks > request.Now.UtcTicks ||
                candidate.VisibleAfterUtcTicks is { } visibleAfter && visibleAfter > request.Now.UtcTicks)
                continue;
            var timer = ReadChecked(candidate, scope);
            var originalRevision = candidate.Revision;
            var updated = ToClaimedEntity(candidate, timer, request);
            AttachForUpdate(updated, originalRevision);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                Detach(updated);
                claims.Add(ToClaim(updated, timer));
            }
            catch (DbUpdateConcurrencyException)
            {
                Detach(updated);
            }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
            {
                Detach(updated);
            }
            catch (OperationCanceledException)
            {
                Detach(updated);
                throw;
            }
        }

        return claims;
    }

    public async ValueTask<RuntimeDurableTimerClaimTransitionResult> RenewClaimAsync(
        RuntimeDurableTimerClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateClaim(claim);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Durable timer visibility timeout must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var current = await LoadClaimAsync(claim, scope, cancellationToken);
        if (current is null)
            return RuntimeDurableTimerClaimTransitionResult.AlreadyApplied;
        if (!Matches(current, claim))
            return RuntimeDurableTimerClaimTransitionResult.Stale;

        var originalRevision = current.Revision;
        var updated = ToRenewedEntity(current, now, visibilityTimeout);
        AttachForUpdate(updated, originalRevision);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            Detach(updated);
            return RuntimeDurableTimerClaimTransitionResult.Applied(ToClaim(updated, claim.Timer));
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach(updated);
            return RuntimeDurableTimerClaimTransitionResult.Stale;
        }
        catch (OperationCanceledException)
        {
            Detach(updated);
            throw;
        }
    }

    public async ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimAsync(
        RuntimeDurableTimerClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateClaim(claim);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var current = await LoadClaimAsync(claim, scope, cancellationToken);
        if (current is null)
            return RuntimeDurableTimerClaimTransitionResult.AlreadyApplied;
        if (!Matches(current, claim))
            return RuntimeDurableTimerClaimTransitionResult.Stale;
        AttachForDelete(current);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return RuntimeDurableTimerClaimTransitionResult.Applied();
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach(current);
            return RuntimeDurableTimerClaimTransitionResult.Stale;
        }
        catch (OperationCanceledException)
        {
            Detach(current);
            throw;
        }
    }

    public async ValueTask<RuntimeDurableTimerClaimTransitionResult> ReleaseClaimAsync(
        RuntimeDurableTimerClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateClaim(claim);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var current = await LoadClaimAsync(claim, scope, cancellationToken);
        if (current is null)
            return RuntimeDurableTimerClaimTransitionResult.AlreadyApplied;
        if (!Matches(current, claim))
            return RuntimeDurableTimerClaimTransitionResult.Stale;

        current.ClaimOwnerId = null;
        current.ClaimedAtUtcTicks = null;
        current.ClaimedAtOffsetMinutes = null;
        current.VisibleAfterUtcTicks = visibleAt.UtcTicks;
        current.VisibleAfterOffsetMinutes = OffsetMinutes(visibleAt);
        current.FailureCount = checked(current.FailureCount + 1);
        current.ClaimOrderKey = ClaimOrderKey(visibleAt, claim.Timer);
        var originalRevision = current.Revision;
        current.Revision = checked(current.Revision + 1);
        AttachForUpdate(current, originalRevision);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            Detach(current);
            return RuntimeDurableTimerClaimTransitionResult.Applied();
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach(current);
            return RuntimeDurableTimerClaimTransitionResult.Stale;
        }
        catch (OperationCanceledException)
        {
            Detach(current);
            throw;
        }
    }

    private async Task<DurableTimerEntity?> LoadClaimAsync(
        RuntimeDurableTimerClaim claim,
        string scope,
        CancellationToken cancellationToken)
    {
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, claim.Timer.WorkflowExecutionId, claim.Timer.TimerId);
        var row = await context.DurableTimers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is not null)
            _ = ReadChecked(row, scope, claim.Timer.WorkflowExecutionId, claim.Timer.TimerId);
        return row;
    }

    private static DurableTimerEntity ToClaimedEntity(
        DurableTimerEntity source,
        DurableTimer timer,
        RuntimeDurableTimerClaimRequest request)
    {
        var visibleAfter = request.Now.Add(request.VisibilityTimeout);
        source.ClaimOwnerId = EfRuntimeOperationalStoreSupport.Encode(request.OwnerId);
        source.ClaimToken = checked(source.ClaimToken + 1);
        source.ClaimedAtUtcTicks = request.Now.UtcTicks;
        source.ClaimedAtOffsetMinutes = OffsetMinutes(request.Now);
        source.VisibleAfterUtcTicks = visibleAfter.UtcTicks;
        source.VisibleAfterOffsetMinutes = OffsetMinutes(visibleAfter);
        source.ClaimOrderKey = ClaimOrderKey(visibleAfter, timer);
        source.Revision = checked(source.Revision + 1);
        return source;
    }

    private static DurableTimerEntity ToRenewedEntity(
        DurableTimerEntity source,
        DateTimeOffset now,
        TimeSpan visibilityTimeout)
    {
        var visibleAfter = now.Add(visibilityTimeout);
        source.VisibleAfterUtcTicks = visibleAfter.UtcTicks;
        source.VisibleAfterOffsetMinutes = OffsetMinutes(visibleAfter);
        source.ClaimOrderKey = ClaimOrderKey(visibleAfter, ReadTimer(source));
        source.Revision = checked(source.Revision + 1);
        return source;
    }

    private static RuntimeDurableTimerClaim ToClaim(DurableTimerEntity row, DurableTimer timer)
    {
        if (row.ClaimOwnerId is null || row.ClaimedAtUtcTicks is null || row.VisibleAfterUtcTicks is null)
            throw new InvalidDataException("The durable-timer claim projection is incomplete.");
        return new RuntimeDurableTimerClaim(
            timer,
            EfRuntimeOperationalStoreSupport.Decode(row.ClaimOwnerId),
            row.ClaimToken,
            row.Revision,
            FromUtcTicks(row.ClaimedAtUtcTicks.Value, row.ClaimedAtOffsetMinutes!.Value),
            FromUtcTicks(row.VisibleAfterUtcTicks.Value, row.VisibleAfterOffsetMinutes!.Value),
            row.FailureCount);
    }

    private static bool Matches(DurableTimerEntity row, RuntimeDurableTimerClaim claim) =>
        row.Revision == claim.Revision &&
        row.ClaimToken == claim.FencingToken &&
        StringComparer.Ordinal.Equals(row.ClaimOwnerId, EfRuntimeOperationalStoreSupport.Encode(claim.OwnerId));

    internal static DurableTimer ReadChecked(
        DurableTimerEntity row,
        string scope,
        string? expectedWorkflowExecutionId = null,
        string? expectedTimerId = null)
    {
        if (EfSchemaVersion.NotReadable("RuntimeOperationalState", row.SchemaVersion, RuntimeOperationalStateEfModule.SchemaVersion) ||
            row.Revision <= 0 ||
            row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
            row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
            throw new InvalidDataException("The durable-timer row scope, schema, or revision projection is corrupt.");

        var timer = ReadTimer(row);
        ValidateTimer(timer);
        ValidateClaimProjection(row);
        if ((expectedWorkflowExecutionId is not null && !StringComparer.Ordinal.Equals(expectedWorkflowExecutionId, timer.WorkflowExecutionId)) ||
            (expectedTimerId is not null && !StringComparer.Ordinal.Equals(expectedTimerId, timer.TimerId)) ||
            row.Id != EfRuntimeOperationalStoreSupport.CompositeId(scope, timer.WorkflowExecutionId, timer.TimerId) ||
            row.WorkflowExecutionId != EfRuntimeOperationalStoreSupport.Encode(timer.WorkflowExecutionId) ||
            row.WorkflowExecutionIdHash != EfRuntimeOperationalStoreSupport.Hash(timer.WorkflowExecutionId) ||
            row.WorkflowExecutionIdOrderKey != EfRuntimeOperationalStoreSupport.Order(timer.WorkflowExecutionId) ||
            row.TimerId != EfRuntimeOperationalStoreSupport.Encode(timer.TimerId) ||
            row.TimerIdHash != EfRuntimeOperationalStoreSupport.Hash(timer.TimerId) ||
            row.TimerIdOrderKey != EfRuntimeOperationalStoreSupport.Order(timer.TimerId) ||
            row.StimulusType != EfRuntimeOperationalStoreSupport.Encode(timer.StimulusType) ||
            row.StimulusHash != EfRuntimeOperationalStoreSupport.Encode(timer.StimulusHash) ||
            row.DueTimeUtcTicks != timer.DueTime.UtcTicks ||
            row.DueTimeOffsetMinutes != OffsetMinutes(timer.DueTime) ||
            row.CreatedAtUtcTicks != timer.CreatedAt.UtcTicks ||
            row.CreatedAtOffsetMinutes != OffsetMinutes(timer.CreatedAt) ||
            row.ClaimOrderKey != ClaimOrderKey(row.VisibleAfterUtcTicks is { } visible ? FromUtcTicks(visible, row.VisibleAfterOffsetMinutes!.Value) : timer.DueTime, timer))
            throw new InvalidDataException("The durable-timer row identity or projection does not match its current content.");

        return timer;
    }

    private static DurableTimer ReadTimer(DurableTimerEntity row)
    {
        try
        {
            return RuntimeArtifactJson.Deserialize<DurableTimer>(row.ContentJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted durable timer is not valid current data.", exception);
        }
    }

    private static DurableTimerEntity ToEntity(DurableTimer timer, string scope, string id, long revision)
    {
        var entity = new DurableTimerEntity
        {
            Id = id,
            ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope),
            ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
            WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(timer.WorkflowExecutionId),
            WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(timer.WorkflowExecutionId),
            WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(timer.WorkflowExecutionId),
            TimerId = EfRuntimeOperationalStoreSupport.Encode(timer.TimerId),
            TimerIdHash = EfRuntimeOperationalStoreSupport.Hash(timer.TimerId),
            TimerIdOrderKey = EfRuntimeOperationalStoreSupport.Order(timer.TimerId),
            StimulusType = EfRuntimeOperationalStoreSupport.Encode(timer.StimulusType),
            StimulusHash = EfRuntimeOperationalStoreSupport.Encode(timer.StimulusHash),
            DueTimeUtcTicks = timer.DueTime.UtcTicks,
            DueTimeOffsetMinutes = OffsetMinutes(timer.DueTime),
            CreatedAtUtcTicks = timer.CreatedAt.UtcTicks,
            CreatedAtOffsetMinutes = OffsetMinutes(timer.CreatedAt),
            ClaimOrderKey = ClaimOrderKey(timer.DueTime, timer),
            ContentJson = RuntimeArtifactJson.Serialize(timer),
            SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
            Revision = revision
        };
        return entity;
    }

    private void AttachForUpdate(DurableTimerEntity row, long originalRevision)
    {
        DetachTracked(row.Id);
        context.DurableTimers.Attach(row);
        context.Entry(row).Property(x => x.Revision).OriginalValue = originalRevision;
        context.Entry(row).State = EntityState.Modified;
    }

    private void AttachForDelete(DurableTimerEntity row)
    {
        DetachTracked(row.Id);
        context.DurableTimers.Attach(row);
        context.Entry(row).State = EntityState.Deleted;
    }

    private void DetachTracked(string id)
    {
        var tracked = context.ChangeTracker.Entries<DurableTimerEntity>().SingleOrDefault(x => x.Entity.Id == id);
        tracked?.State = EntityState.Detached;
    }

    private void Detach(DurableTimerEntity row) =>
        context.Entry(row).State = EntityState.Detached;

    private static void ValidateClaim(RuntimeDurableTimerClaim claim)
    {
        ValidateTimer(claim.Timer);
        ValidateIdentity(claim.OwnerId, nameof(claim.OwnerId));
    }

    private static void ValidateClaimProjection(DurableTimerEntity row)
    {
        if (row.ClaimToken < 0 || row.FailureCount < 0)
            throw new InvalidDataException("Durable-timer claim counters cannot be negative.");
        if (row.ClaimToken == 0 && (row.FailureCount != 0 || row.ClaimOwnerId is not null || row.ClaimedAtUtcTicks is not null || row.ClaimedAtOffsetMinutes is not null || row.VisibleAfterUtcTicks is not null || row.VisibleAfterOffsetMinutes is not null))
            throw new InvalidDataException("The durable-timer initial claim state is inconsistent.");
        if (row.ClaimToken > 0 && row.ClaimOwnerId is null && (row.ClaimedAtUtcTicks is not null || row.ClaimedAtOffsetMinutes is not null || row.VisibleAfterUtcTicks is null || row.VisibleAfterOffsetMinutes is null))
            throw new InvalidDataException("The durable-timer released claim state is inconsistent.");
        if (row.ClaimToken > 0 && row.ClaimOwnerId is not null && (row.ClaimedAtUtcTicks is null || row.ClaimedAtOffsetMinutes is null || row.VisibleAfterUtcTicks is null || row.VisibleAfterOffsetMinutes is null))
            throw new InvalidDataException("The durable-timer claim state is incomplete.");
        if (row.ClaimedAtUtcTicks is { } claimed && row.VisibleAfterUtcTicks is { } visible && visible <= claimed)
            throw new InvalidDataException("The durable-timer visibility deadline must follow the claim time.");
    }

    private static void ValidateTimer(DurableTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ValidateIdentity(timer.WorkflowExecutionId, nameof(timer.WorkflowExecutionId));
        ValidateIdentity(timer.TimerId, nameof(timer.TimerId));
        ValidateBounded(timer.StimulusType, RuntimeOperationalStateEfModule.DurableTimerStimulusTypeMaximumLength, nameof(timer.StimulusType));
        ValidateBounded(timer.StimulusHash, RuntimeOperationalStateEfModule.DurableTimerStimulusHashMaximumLength, nameof(timer.StimulusHash));
        if (timer.ActivityExecutionId is not null)
            ValidateIdentity(timer.ActivityExecutionId, nameof(timer.ActivityExecutionId));
        if (timer.ExecutionScopeId is not null)
            ValidateIdentity(timer.ExecutionScopeId, nameof(timer.ExecutionScopeId));
    }

    private static void ValidateIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > RuntimeOperationalStateEfModule.IdentityMaximumLength)
            throw new ArgumentException($"Runtime identity cannot exceed {RuntimeOperationalStateEfModule.IdentityMaximumLength} UTF-16 code units.", parameterName);
    }

    private static void ValidateBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength)
            throw new ArgumentException($"Runtime value cannot exceed {maximumLength} UTF-16 code units.", parameterName);
    }

    private static int OffsetMinutes(DateTimeOffset value) => checked((int)value.Offset.TotalMinutes);

    private static DateTimeOffset FromUtcTicks(long utcTicks, int offsetMinutes) =>
        new DateTimeOffset(new DateTime(utcTicks, DateTimeKind.Utc)).ToOffset(TimeSpan.FromMinutes(offsetMinutes));

    private static string ClaimOrderKey(DateTimeOffset availableAt, DurableTimer timer)
    {
        var framed = string.Concat(timer.WorkflowExecutionId.Length.ToString(CultureInfo.InvariantCulture), ":", timer.WorkflowExecutionId,
            timer.TimerId.Length.ToString(CultureInfo.InvariantCulture), ":", timer.TimerId);
        return string.Concat(
            availableAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture), ".",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(framed))));
    }

    private static InvalidOperationException TransitionDidNotSettle(string transition, string workflowExecutionId, string timerId) =>
        new($"Durable-timer {transition} for workflow execution '{workflowExecutionId}' and timer '{timerId}' did not settle after {Transitions.MaxAttempts} compare-and-swap attempts.");
}
