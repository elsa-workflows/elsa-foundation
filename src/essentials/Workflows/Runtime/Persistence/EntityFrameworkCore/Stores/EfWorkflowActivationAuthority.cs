using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Concrete opt-in EF Core authority for the definition-keyed workflow activation slots.</summary>
/// <remarks>
/// The slot row is the authority: its revision is the compare-and-swap token and its unique active-activation
/// projection prevents one activation from being admitted to two lanes in the same persistence scope. A document store
/// remains available as the default until the surrounding composition is explicitly switched.
/// </remarks>
public sealed class EfWorkflowActivationAuthority(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowActivationAuthority
{
    private const int PageSize = 100;
    // A transient conflict is retried only when SaveChanges reports it, never when a read raises it.
    private static readonly EfWriteRetry Transitions = new(
        EfWriteRetry.DefaultMaxAttempts,
        exception => EfRelationalExceptionClassifier.IsSaveConflict(
            exception, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient));
    private readonly RuntimeDbContext context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly IPersistenceAccessContextAccessor accessContextAccessor = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));

    public async ValueTask<WorkflowActivationSlot?> FindAsync(
        string workflowDefinitionId,
        string slotName,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowDefinitionId, nameof(workflowDefinitionId));
        ValidateIdentity(slotName, nameof(slotName));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var row = await context.WorkflowActivationSlots.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == RowId(scope, workflowDefinitionId, slotName), cancellationToken);
        return row is null ? null : Read(row, scope, workflowDefinitionId, slotName);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowDefinitionId, nameof(workflowDefinitionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeKey = Encode(scope);
        var scopeHash = Hash(scope);
        var definition = Encode(workflowDefinitionId);
        var definitionHash = Hash(workflowDefinitionId);
        var rows = new List<WorkflowActivationSlot>();
        string? after = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = context.WorkflowActivationSlots.AsNoTracking().Where(x =>
                x.ScopeKey == scopeKey && x.ScopeKeyHash == scopeHash &&
                x.WorkflowDefinitionId == definition && x.WorkflowDefinitionIdHash == definitionHash);
            if (after is not null)
                query = query.Where(x => x.SlotNameOrderKey.CompareTo(after) > 0);
            var page = await query
                .OrderBy(x => x.SlotNameOrderKey)
                .ThenBy(x => x.SlotIdOrderKey)
                .Take(PageSize)
                .ToArrayAsync(cancellationToken);
            rows.AddRange(page.Select(x => Read(x, scope, workflowDefinitionId)));
            if (page.Length < PageSize)
                return rows;
            after = page[^1].SlotNameOrderKey;
        }
    }

    public async ValueTask<WorkflowActivationTransition> TryActivateAsync(
        WorkflowActivationSlotRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var rowId = RowId(scope, request.WorkflowDefinitionId, request.SlotName);
        return await Transitions.RunAsync(context, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ChangeTracker.Clear();
            var row = await context.WorkflowActivationSlots.SingleOrDefaultAsync(x => x.Id == rowId, cancellationToken);
            var current = row is null
                ? Empty(request.WorkflowDefinitionId, request.SlotName, request.UpdatedAt)
                : Read(row, scope, request.WorkflowDefinitionId, request.SlotName);
            if (current.Revision != request.ExpectedRevision)
                return Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation slot revision changed; another writer moved it first.");
            if (current.ActiveActivationId is not null && current.Source is not null &&
                request.OwnershipIntent != WorkflowActivationOwnershipIntent.TakeOver &&
                !current.Source.IsSameOwnerAs(request.Source))
                return Conflict(current, WorkflowActivationConflict.ForeignSource,
                    $"Definition '{request.WorkflowDefinitionId}' slot '{request.SlotName}' is owned by activation source '{current.Source.Describe()}'; '{request.Source.Describe()}' cannot activate a different artifact on it. Ownership transfer is an explicit operator action.");
            if (await IsLiveInAnotherSlotAsync(scope, request.ActivationId, rowId, cancellationToken))
                return Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation is already live in another slot.");

            var next = current with
            {
                ActiveActivationId = request.ActivationId,
                Source = request.Source,
                Revision = checked(current.Revision + 1),
                UpdatedAt = request.UpdatedAt
            };
            if (row is null)
                context.WorkflowActivationSlots.Add(ToEntity(next, scope, rowId));
            else
                Copy(row, next, scope);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return new WorkflowActivationTransition(true, next, current.ActiveActivationId, ReplacedSource: current.Source);
            }
            catch (Exception exception) when (Transitions.ShouldRetry(context, exception))
            {
                context.ChangeTracker.Clear();
                throw;
            }
        }, _ => SettledConflictAsync(request.WorkflowDefinitionId, request.SlotName, request.UpdatedAt, cancellationToken), cancellationToken);
    }

    public async ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        string workflowDefinitionId,
        string slotName,
        WorkflowActivationSource source,
        long expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowDefinitionId, nameof(workflowDefinitionId));
        ValidateIdentity(slotName, nameof(slotName));
        ValidateSource(source);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var rowId = RowId(scope, workflowDefinitionId, slotName);
        return await Transitions.RunAsync(context, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ChangeTracker.Clear();
            var row = await context.WorkflowActivationSlots.SingleOrDefaultAsync(x => x.Id == rowId, cancellationToken);
            var current = row is null ? Empty(workflowDefinitionId, slotName, updatedAt) : Read(row, scope, workflowDefinitionId, slotName);
            if (current.Revision != expectedRevision)
                return Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation slot revision changed; another writer moved it first.");
            if (current.ActiveActivationId is not null && current.Source is not null && !current.Source.IsSameOwnerAs(source))
                return Conflict(current, WorkflowActivationConflict.ForeignSource,
                    $"Definition '{workflowDefinitionId}' slot '{slotName}' is owned by activation source '{current.Source.Describe()}'; '{source.Describe()}' cannot deactivate it.");

            var next = current with
            {
                ActiveActivationId = null,
                Source = null,
                Revision = checked(current.Revision + 1),
                UpdatedAt = updatedAt
            };
            if (row is null)
                context.WorkflowActivationSlots.Add(ToEntity(next, scope, rowId));
            else
                Copy(row, next, scope);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return new WorkflowActivationTransition(true, next, current.ActiveActivationId, ReplacedSource: current.Source);
            }
            catch (Exception exception) when (Transitions.ShouldRetry(context, exception))
            {
                context.ChangeTracker.Clear();
                throw;
            }
        }, _ => SettledConflictAsync(workflowDefinitionId, slotName, updatedAt, cancellationToken), cancellationToken);
    }

    private async ValueTask<WorkflowActivationTransition> SettledConflictAsync(
        string workflowDefinitionId,
        string slotName,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        var settled = await FindAsync(workflowDefinitionId, slotName, cancellationToken) ?? Empty(workflowDefinitionId, slotName, updatedAt);
        return Conflict(settled, WorkflowActivationConflict.RevisionMismatch,
            "The activation slot changed concurrently and did not settle.");
    }

    private async ValueTask<bool> IsLiveInAnotherSlotAsync(string scope, string activationId, string rowId, CancellationToken cancellationToken)
    {
        var hash = Hash(activationId);
        var encoded = Encode(activationId);
        var matches = await context.WorkflowActivationSlots.AsNoTracking()
            .Where(x => x.ScopeKey == Encode(scope) && x.ScopeKeyHash == Hash(scope) &&
                        x.ActiveActivationIdHash == hash && x.ActiveActivationId == encoded && x.Id != rowId)
            .OrderBy(x => x.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        foreach (var match in matches)
        {
            var slot = Read(match, scope);
            if (slot.ActiveActivationId != activationId)
                throw new InvalidDataException("The activation-slot lookup does not match its authoritative activation identity.");
        }
        return matches.Length > 0;
    }

    private string RequireScope() => EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);

    private static WorkflowActivationSlot Empty(string definitionId, string slotName, DateTimeOffset updatedAt) =>
        new(WorkflowActivationSlotIdentity.Create(definitionId, slotName), definitionId, slotName, null, null, 0, updatedAt);

    private static WorkflowActivationTransition Conflict(WorkflowActivationSlot slot, WorkflowActivationConflict conflict, string diagnostic) =>
        new(false, slot, Conflict: conflict, Diagnostic: diagnostic);

    private static WorkflowActivationSlotEntity ToEntity(WorkflowActivationSlot slot, string scope, string id)
    {
        var row = new WorkflowActivationSlotEntity { Id = id };
        Copy(row, slot, scope);
        return row;
    }

    private static void Copy(WorkflowActivationSlotEntity row, WorkflowActivationSlot slot, string scope)
    {
        row.ScopeKey = Encode(scope);
        row.ScopeKeyHash = Hash(scope);
        row.SlotId = Encode(slot.SlotId);
        row.SlotIdHash = Hash(slot.SlotId);
        row.SlotIdOrderKey = SlotIdOrder(slot.SlotId);
        row.WorkflowDefinitionId = Encode(slot.WorkflowDefinitionId);
        row.WorkflowDefinitionIdHash = Hash(slot.WorkflowDefinitionId);
        row.WorkflowDefinitionIdOrderKey = Order(slot.WorkflowDefinitionId);
        row.SlotName = Encode(slot.SlotName);
        row.SlotNameHash = Hash(slot.SlotName);
        row.SlotNameOrderKey = Order(slot.SlotName);
        row.ActiveActivationId = slot.ActiveActivationId is null ? null : Encode(slot.ActiveActivationId);
        row.ActiveActivationIdHash = slot.ActiveActivationId is null ? null : Hash(slot.ActiveActivationId);
        row.ActiveActivationIdOrderKey = slot.ActiveActivationId is null ? null : Order(slot.ActiveActivationId);
        row.ActiveActivationUniquenessKey = Hash(slot.ActiveActivationId is null
            ? $"slot\0{slot.SlotId}"
            : $"activation\0{slot.ActiveActivationId}");
        row.SourceKind = slot.Source?.Kind is { } kind ? Encode(kind) : null;
        row.SourceId = slot.Source?.SourceId is { } sourceId ? Encode(sourceId) : null;
        row.UpdatedAtUtcTicks = slot.UpdatedAt.UtcTicks;
        row.UpdatedAtOffsetMinutes = checked((int)slot.UpdatedAt.Offset.TotalMinutes);
        row.ContentJson = RuntimeArtifactJson.Serialize(slot);
        row.SchemaVersion = RuntimeActivationSlotEfModule.SchemaVersion;
        row.Revision = slot.Revision;
    }

    private static WorkflowActivationSlot Read(WorkflowActivationSlotEntity row, string scope, string? expectedDefinitionId = null, string? expectedSlotName = null)
    {
        WorkflowActivationSlot slot;
        try
        {
            slot = RuntimeArtifactJson.Deserialize<WorkflowActivationSlot>(row.ContentJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The persisted EF activation-slot content is malformed.", exception);
        }
        ValidateIdentity(slot.WorkflowDefinitionId, nameof(slot.WorkflowDefinitionId));
        ValidateIdentity(slot.SlotName, nameof(slot.SlotName));
        if (EfSchemaVersion.NotReadable("RuntimeActivationSlot", row.SchemaVersion, RuntimeActivationSlotEfModule.SchemaVersion) ||
            row.Id != RowId(scope, slot.WorkflowDefinitionId, slot.SlotName) ||
            row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
            expectedDefinitionId is not null && slot.WorkflowDefinitionId != expectedDefinitionId ||
            expectedSlotName is not null && slot.SlotName != expectedSlotName ||
            row.SlotId != Encode(slot.SlotId) || row.SlotIdHash != Hash(slot.SlotId) ||
            row.SlotIdOrderKey != SlotIdOrder(slot.SlotId) ||
            slot.SlotId != WorkflowActivationSlotIdentity.Create(slot.WorkflowDefinitionId, slot.SlotName) ||
            row.WorkflowDefinitionId != Encode(slot.WorkflowDefinitionId) || row.WorkflowDefinitionIdHash != Hash(slot.WorkflowDefinitionId) ||
            row.WorkflowDefinitionIdOrderKey != Order(slot.WorkflowDefinitionId) ||
            row.SlotName != Encode(slot.SlotName) || row.SlotNameHash != Hash(slot.SlotName) ||
            row.SlotNameOrderKey != Order(slot.SlotName) ||
            row.ActiveActivationId != EncodeOptional(slot.ActiveActivationId) ||
            row.ActiveActivationIdHash != HashOptional(slot.ActiveActivationId) ||
            row.ActiveActivationIdOrderKey != OrderOptional(slot.ActiveActivationId) ||
            row.ActiveActivationUniquenessKey != Hash(slot.ActiveActivationId is null
                ? $"slot\0{slot.SlotId}"
                : $"activation\0{slot.ActiveActivationId}") ||
            row.SourceKind != (slot.Source is null ? null : Encode(slot.Source.Kind)) ||
            row.SourceId != (slot.Source?.SourceId is null ? null : Encode(slot.Source.SourceId)) ||
            row.UpdatedAtUtcTicks != slot.UpdatedAt.UtcTicks ||
            row.UpdatedAtOffsetMinutes != (int)slot.UpdatedAt.Offset.TotalMinutes ||
            row.Revision <= 0 || slot.Revision != row.Revision)
            throw new InvalidDataException("The persisted EF activation-slot row does not match its identity envelope or authoritative content.");
        ValidateSource(slot.Source);
        if (slot.ActiveActivationId is null && slot.Source is not null)
            throw new InvalidDataException("An inactive activation slot cannot retain an owner.");
        if (slot.ActiveActivationId is not null && slot.Source is null)
            throw new InvalidDataException("An active activation slot must retain its owner.");
        return slot;
    }

    private static void ValidateRequest(WorkflowActivationSlotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdentity(request.WorkflowDefinitionId, nameof(request.WorkflowDefinitionId));
        ValidateIdentity(request.SlotName, nameof(request.SlotName));
        ValidateIdentity(request.ActivationId, nameof(request.ActivationId));
        ValidateSource(request.Source);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ExpectedRevision);
        if (!Enum.IsDefined(request.OwnershipIntent)) throw new ArgumentOutOfRangeException(nameof(request.OwnershipIntent));
    }

    private static void ValidateSource(WorkflowActivationSource? source)
    {
        if (source is null) return;
        ValidateIdentity(source.Kind, nameof(source.Kind));
        if (source.SourceId is not null) ValidateIdentity(source.SourceId, nameof(source.SourceId));
    }

    private static void ValidateIdentity(string value, string parameterName) => EfRuntimeOperationalStoreSupport.ValidateIdentity(value, parameterName);
    private static string RowId(string scope, string definitionId, string slotName) => EfRuntimeOperationalStoreSupport.CompositeId(scope, WorkflowActivationSlotIdentity.Create(definitionId, slotName));
    private static string Encode(string value) => EfRuntimeOperationalStoreSupport.Encode(value);
    private static string Hash(string value) => EfRuntimeOperationalStoreSupport.Hash(value);
    private static string Order(string value) => EfRuntimeOperationalStoreSupport.Order(value);
    private static string SlotIdOrder(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeActivationSlotEfModule.SlotIdMaximumLength));
    private static string? EncodeOptional(string? value) => value is null ? null : Encode(value);
    private static string? HashOptional(string? value) => value is null ? null : Hash(value);
    private static string? OrderOptional(string? value) => value is null ? null : Order(value);
}
