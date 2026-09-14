using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Primitives.Versioning;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;

public abstract class EfDesignCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic)
{
    protected WorkflowsDesignDbContext Db => db;
    protected IPersistenceAccessContextAccessor Access => access;
    protected IDesignAtomicWriter Atomic => atomic;
    protected void Tenant(string? id) => EfDesignSupport.EnsureTenant(access, id);
    protected string? WriteTenant(string? id)
    {
        var effective = id ?? Access.Current.Scope?.Value;
        Tenant(effective);
        return effective;
    }
    protected IQueryable<T> Scoped<T>(IQueryable<T> query, Func<T, string?> tenant) where T : class => EfDesignSupport.InScope(query, access, tenant);
    protected static DateTimeOffset Now => DateTimeOffset.UtcNow;
    protected static WorkflowDefinitionState EmptyState() => new([], null, [], [], null);
    protected static WorkflowDefinitionDraft Draft(string id, string definitionId, string? tenant, WorkflowDefinitionState state, string? sourceVersionId = null) => new() { Id = id, WorkflowDefinitionId = definitionId, TenantId = tenant, StateSource = null, State = state, SourceVersionId = sourceVersionId, CreatedAt = Now, LastModifiedAt = Now };
    protected void SaveDraftState(WorkflowDefinitionDraft row, WorkflowDefinitionState state, string operation) { row.State = state; row.StateSource = EfDesignSupport.WriteState(Serializer, state, operation); }
    protected IPayloadSerializer Serializer = null!;
    protected static void SaveLayout(WorkflowsDesignDbContext db, WorkflowDefinitionDraftLayout row, IReadOnlyCollection<DesignMetadataRecord> records, IReadOnlyCollection<ActivityPresentationRecord>? presentation = null) { EfDesignSupport.SetLayout(db, row, records, presentation); }
    protected static void SaveVersionLayout(WorkflowsDesignDbContext db, WorkflowDefinitionVersionLayout row, IReadOnlyCollection<DesignMetadataRecord> records, IReadOnlyCollection<ActivityPresentationRecord>? presentation = null) { EfDesignSupport.SetLayout(db, row, records, presentation); }
}

public sealed class EfAddWorkflowDefinitionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IPayloadSerializer serializer, IIdentityGenerator identities) : EfDesignCommand(db, access, atomic), IAddWorkflowDefinitionCommand
{
    public Task<WorkflowDefinitionCreated> Execute(DesignOperationKey key, WorkflowDefinition definition, WorkflowDefinitionDraft draft, CancellationToken ct = default) => Execute(key, definition, draft, [], [], ct);
    public Task<WorkflowDefinitionCreated> Execute(DesignOperationKey key, WorkflowDefinition definition, WorkflowDefinitionDraft draft, IReadOnlyCollection<DesignMetadataRecord> layout, CancellationToken ct = default) => Execute(key, definition, draft, layout, [], ct);
    public async Task<WorkflowDefinitionCreated> Execute(DesignOperationKey key, WorkflowDefinition definition, WorkflowDefinitionDraft draft, IReadOnlyCollection<DesignMetadataRecord> layout, IReadOnlyCollection<ActivityPresentationRecord> presentation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(draft); definition.TenantId = WriteTenant(definition.TenantId); draft.TenantId = WriteTenant(draft.TenantId);
        if (!WorkflowDefinitionIdentity.Equals(definition.Id, draft.WorkflowDefinitionId)) throw new ArgumentException("The first draft must belong to the definition.", nameof(draft));
        draft.WorkflowDefinitionId = definition.Id;
        Serializer = serializer; SaveDraftState(draft, draft.State, "workflow.definition.create.v1"); var normalizedPresentation = ActivityPresentationRecord.NormalizeCollection(presentation); EfDesignSupport.Stamp(definition, Now); EfDesignSupport.Stamp(draft, Now);
        var requestMaterial = new CreateWorkflowDefinitionRequestMaterial(
            definition.Name,
            definition.Description,
            definition.DeletedAt,
            definition.DeletedReason,
            definition.IsSourceOwned,
            draft.SourceVersionId,
            draft.StateSource!,
            EfDesignSupport.LayoutMaterial(layout),
            EfDesignSupport.PresentationMaterial(normalizedPresentation));
        return await Atomic.ExecuteAsync(key, "workflow.definition.create.v1", requestMaterial, [DesignPersistenceUnitNames.Definitions, DesignPersistenceUnitNames.Drafts, DesignPersistenceUnitNames.DraftLayouts], async token => { EfDesignSupport.SetDefinitionSearchKeys(Db, definition); Db.Definitions.Add(definition); Db.Drafts.Add(draft); var sibling = WorkflowDefinitionDraftLayout.CreateFor(identities, draft.Id, layout, normalizedPresentation); sibling.TenantId = draft.TenantId; EfDesignSupport.Stamp(sibling, Now); EfDesignSupport.SetLayout(Db, sibling, layout, normalizedPresentation); Db.DraftLayouts.Add(sibling); await Task.CompletedTask; return new WorkflowDefinitionCreated(definition.Id, draft.Id); }, ct);
    }
}

public sealed class EfMaterializeWorkflowDefinitionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic) : EfDesignCommand(db, access, atomic), IMaterializeWorkflowDefinitionCommand
{
    public async Task<string> Execute(DesignOperationKey key, WorkflowDefinition definition, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(definition); definition.TenantId = WriteTenant(definition.TenantId); EfDesignSupport.Stamp(definition, Now); return await Atomic.ExecuteAsync(key, "workflow.definition.materialize.v1", new MaterializeWorkflowDefinitionRequestMaterial(definition.Id, definition.Name, definition.Description, definition.DeletedAt, definition.DeletedReason, definition.IsSourceOwned), [DesignPersistenceUnitNames.Definitions], async token => { EfDesignSupport.SetDefinitionSearchKeys(Db, definition); Db.Definitions.Add(definition); await Task.CompletedTask; return definition.Id; }, ct); }
}

public sealed class EfSaveWorkflowDefinitionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic) : EfDesignCommand(db, access, atomic), ISaveWorkflowDefinitionCommand
{
    public async Task Execute(DesignOperationKey key, WorkflowDefinition definition, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(definition); WorkflowDefinitionLimits.Validate(definition); Tenant(definition.TenantId); await Atomic.ExecuteAsync(key, "workflow.definition.save.v1", new SaveWorkflowDefinitionRequestMaterial(definition.Id, definition.Name, definition.Description, definition.DeletedAt, definition.DeletedReason, definition.IsSourceOwned), [DesignPersistenceUnitNames.Definitions], async token => { var idKey = EfDesignSupport.SearchKey(definition.Id); var row = await Scoped(Db.Definitions, x => x.TenantId).SingleOrDefaultAsync(x => EF.Property<string>(x, "IdSearchKey") == idKey, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), definition.Id); row.Name = definition.Name; row.Description = definition.Description; row.DeletedAt = definition.DeletedAt; row.DeletedReason = definition.DeletedReason; row.IsSourceOwned = definition.IsSourceOwned; row.LastModifiedAt = Now; EfDesignSupport.SetDefinitionSearchKeys(Db, row); return definition.Id; }, ct); }
}

public sealed class EfCreateDraftCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IIdentityGenerator identities, IPayloadSerializer serializer, IDistributedLockProvider? lockProvider = null, IInlineEventPublisher? inlineEvents = null, IDeferredEventPublisher? deferredEvents = null) : EfDesignCommand(db, access, atomic), ICreateDraftCommand
{
    public async Task<string> Execute(DesignOperationKey key, string workflowDefinitionId, WorkflowDefinitionState? initialState = null, IReadOnlyCollection<DesignMetadataRecord>? initialLayout = null, string? sourceVersionId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId); Serializer = serializer; var state = initialState ?? EmptyState(); var id = identities.Generate(); var tenant = Access.Current.Scope?.Value; var draft = Draft(id, workflowDefinitionId, tenant, state, sourceVersionId); SaveDraftState(draft, state, "workflow.draft.create.v1"); var layout = initialLayout ?? []; return await ExecuteLocked(key, workflowDefinitionId, sourceVersionId, draft, layout, ct);
    }
    private async Task<string> ExecuteLocked(DesignOperationKey key, string definitionId, string? sourceVersionId, WorkflowDefinitionDraft draft, IReadOnlyCollection<DesignMetadataRecord> layout, CancellationToken ct)
    {
        if (lockProvider is null) throw new InvalidOperationException("Workflow design draft creation requires a distributed lock provider.");
        await using var handle = await lockProvider.AcquireLockAsync(WorkflowDesignPersistenceLockKeys.DraftKey(draft.Id), null, ct); return await ExecuteCore(key, definitionId, sourceVersionId, draft, layout, ct);
    }
    private async Task<string> ExecuteCore(DesignOperationKey key, string definitionId, string? sourceVersionId, WorkflowDefinitionDraft draft, IReadOnlyCollection<DesignMetadataRecord> layout, CancellationToken ct)
    {
        IReadOnlyList<ValidationError> errors = []; var staged = false;
        var id = await Atomic.ExecuteAsync(key, "workflow.draft.create.v1", new CreateDraftRequestMaterial(definitionId, draft.StateSource!, EfDesignSupport.LayoutMaterial(layout), sourceVersionId), [DesignPersistenceUnitNames.Drafts, DesignPersistenceUnitNames.DraftLayouts], async token => { var definitionKey = EfDesignSupport.SearchKey(definitionId); var definition = await Scoped(Db.Definitions.AsNoTracking(), x => x.TenantId).SingleOrDefaultAsync(x => EF.Property<string>(x, "IdSearchKey") == definitionKey, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), definitionId); draft.WorkflowDefinitionId = definition.Id; draft.TenantId = WriteTenant(definition.TenantId); staged = true; if (inlineEvents is not null) errors = (await inlineEvents.DeriveValidationErrorsAsync(draft, token)).ToArray(); Db.Drafts.Add(draft); var sibling = WorkflowDefinitionDraftLayout.CreateFor(identities, draft.Id, layout); sibling.TenantId = draft.TenantId; EfDesignSupport.Stamp(sibling, Now); EfDesignSupport.SetLayout(Db, sibling, layout); Db.DraftLayouts.Add(sibling); return draft.Id; }, ct);
        if (staged && deferredEvents is not null) { await deferredEvents.Publish(new DraftCreated(draft.Id, draft.WorkflowDefinitionId, sourceVersionId), CancellationToken.None); await deferredEvents.Publish(new DraftValidated(draft, errors), CancellationToken.None); }
        return id;
    }
}

public sealed class EfCloneDraftFromVersionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IIdentityGenerator identities, IPayloadSerializer serializer, IDistributedLockProvider? lockProvider = null, IInlineEventPublisher? inlineEvents = null, IDeferredEventPublisher? deferredEvents = null) : EfDesignCommand(db, access, atomic), ICloneDraftFromVersionCommand
{
    public async Task<string> Execute(DesignOperationKey key, string sourceVersionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVersionId); Serializer = serializer;
        if (lockProvider is null) throw new InvalidOperationException("Workflow draft cloning requires a distributed lock provider.");
        IReadOnlyList<ValidationError> errors = []; var staged = false;
        IDistributedSynchronizationHandle? draftLock = null;
        string result;
        try
        {
            result = await Atomic.ExecuteAsync(key, "workflow.draft.clone-from-version.v1", new CloneDraftRequestMaterial(sourceVersionId), [DesignPersistenceUnitNames.Drafts, DesignPersistenceUnitNames.DraftLayouts], async token =>
            {
                // A transient atomic retry reruns the stage and allocates a fresh draft id.
                // Dispose the previous attempt's handle before acquiring the next one.
                if (draftLock is not null)
                {
                    await draftLock.DisposeAsync();
                    draftLock = null;
                }
                var source = await Scoped(Db.Versions.AsNoTracking(), x => x.TenantId).SingleOrDefaultAsync(x => x.Id == sourceVersionId, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionVersion), sourceVersionId);
                Tenant(source.TenantId); var state = EfDesignSupport.ReadState(serializer, source.StateSource, "workflow.draft.clone-from-version.v1");
                var layout = await Scoped(Db.VersionLayouts.AsNoTracking(), x => x.TenantId).SingleOrDefaultAsync(x => x.WorkflowDefinitionVersionId == sourceVersionId, token);
                var records = layout is null ? [] : EfDesignSupport.ReadLayout(Db.Entry(layout).Property<string>("RecordsJson").CurrentValue);
                var presentation = layout is null ? [] : EfDesignSupport.ReadPresentation(Db.Entry(layout).Property<string>("ActivityPresentationJson").CurrentValue);
                staged = true; var id = identities.Generate(); var draft = Draft(id, source.DefinitionId, source.TenantId, state, sourceVersionId); SaveDraftState(draft, state, "workflow.draft.clone-from-version.v1");
                if (lockProvider is not null) draftLock = await lockProvider.AcquireLockAsync(WorkflowDesignPersistenceLockKeys.DraftKey(id), null, token);
                if (inlineEvents is not null) errors = (await inlineEvents.DeriveValidationErrorsAsync(draft, token)).ToArray();
                Db.Drafts.Add(draft); var sibling = WorkflowDefinitionDraftLayout.CreateFor(identities, id, records, presentation); sibling.TenantId = source.TenantId; EfDesignSupport.Stamp(sibling, Now); EfDesignSupport.SetLayout(Db, sibling, records, presentation); Db.DraftLayouts.Add(sibling); return id;
            }, ct);
        }
        finally
        {
            if (draftLock is not null) await draftLock.DisposeAsync();
        }
        if (staged && deferredEvents is not null) { var draft = await Scoped(Db.Drafts.AsNoTracking(), x => x.TenantId).SingleAsync(x => x.Id == result, ct); await deferredEvents.Publish(new DraftCreated(result, draft.WorkflowDefinitionId, sourceVersionId), CancellationToken.None); await deferredEvents.Publish(new DraftValidated(draft, errors), CancellationToken.None); }
        return result;
    }
}

public sealed class EfAddWorkflowDefinitionVersionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IPayloadSerializer serializer, IIdentityGenerator identities, IDistributedLockProvider? lockProvider = null) : EfDesignCommand(db, access, atomic), IAddWorkflowDefinitionVersionCommand
{
    public async Task<WorkflowDefinitionVersionAdded> Execute(DesignOperationKey key, string definitionId, WorkflowDefinitionState state, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId); ArgumentNullException.ThrowIfNull(state); Serializer = serializer;
        if (lockProvider is null) throw new InvalidOperationException("Workflow version allocation requires a distributed lock provider.");
        await using var handle = await lockProvider.AcquireLockAsync(WorkflowDesignPersistenceLockKeys.DefinitionKey(definitionId), null, ct);
        return await ExecuteCore(key, definitionId, state, ct);
    }
    private Task<WorkflowDefinitionVersionAdded> ExecuteCore(DesignOperationKey key, string definitionId, WorkflowDefinitionState state, CancellationToken ct) { var stateJson = EfDesignSupport.WriteState(serializer, state, "workflow.version.add.v1"); return Atomic.ExecuteAsync(key, "workflow.version.add.v1", new AddWorkflowDefinitionVersionRequestMaterial(definitionId, stateJson), [DesignPersistenceUnitNames.Versions], async token => { var definitionKey = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(definitionId)); var definition = await Scoped(Db.Definitions.AsNoTracking(), x => x.TenantId).SingleOrDefaultAsync(x => EF.Property<string>(x, "IdLookupHash") == definitionKey, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), definitionId); var actualDefinitionId = definition.Id; var actualDefinitionKey = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(actualDefinitionId)); var latest = await Scoped(Db.Versions.AsNoTracking(), x => x.TenantId).Where(x => EF.Property<string>(x, "DefinitionIdLookupHash") == actualDefinitionKey).OrderByDescending(x => x.SemVerSortKey).FirstOrDefaultAsync(token); var version = WorkflowVersionNumbering.NextMajor(latest?.Version); var row = new WorkflowDefinitionVersion(actualDefinitionId, version) { Id = identities.Generate(), TenantId = definition.TenantId, StateSource = stateJson, State = state, CreatedAt = Now, LastModifiedAt = Now }; Db.Versions.Add(row); return new WorkflowDefinitionVersionAdded(actualDefinitionId, row.Id, version); }, ct); }
}

public sealed class EfMaterializeWorkflowDefinitionVersionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IPayloadSerializer serializer) : EfDesignCommand(db, access, atomic), IMaterializeWorkflowDefinitionVersionCommand
{
    public async Task<WorkflowDefinitionVersionAdded> Execute(DesignOperationKey key, WorkflowDefinitionVersion version, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(version); version.TenantId = WriteTenant(version.TenantId); version.StateSource ??= EfDesignSupport.WriteState(serializer, version.State, "workflow.version.materialize.v1"); EfDesignSupport.Stamp(version, Now); return await Atomic.ExecuteAsync(key, "workflow.version.materialize.v1", new MaterializeWorkflowDefinitionVersionRequestMaterial(version.DefinitionId, version.Id, version.Version, version.SourceDraftId, version.SourceCreatedAt, version.StateSource), [DesignPersistenceUnitNames.Versions], async token => { var definitionKey = EfDesignSupport.SearchKey(version.DefinitionId); var definition = await Scoped(Db.Definitions.AsNoTracking(), x => x.TenantId).SingleOrDefaultAsync(x => EF.Property<string>(x, "IdSearchKey") == definitionKey, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), version.DefinitionId); var normalizedVersion = WorkflowDefinitionVersion.From(version, definition.Id); normalizedVersion.TenantId = definition.TenantId; normalizedVersion.CreatedAt = version.CreatedAt; normalizedVersion.LastModifiedAt = version.LastModifiedAt; normalizedVersion.StateSource = version.StateSource; Db.Versions.Add(normalizedVersion); await Task.CompletedTask; return new WorkflowDefinitionVersionAdded(normalizedVersion.DefinitionId, normalizedVersion.Id, normalizedVersion.Version); }, ct); }
}

public sealed class EfUpdateDraftCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IPayloadSerializer serializer, IActivityStructureService activityStructureService, IDistributedLockProvider? lockProvider = null, IInlineEventPublisher? inlineEvents = null, IDeferredEventPublisher? deferredEvents = null) : EfDesignCommand(db, access, atomic), IUpdateDraftCommand
{
    public async Task Execute(DesignOperationKey key, UpdateDraftRequest request, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(request); Serializer = serializer; var reachableNodeIds = CollectNodeIds(request.State.RootActivity); var normalized = request with { ActivityPresentation = ActivityPresentationRecord.NormalizeCollection(request.ActivityPresentation).Where(record => reachableNodeIds.Contains(record.NodeId)).ToArray() }; if (lockProvider is null) throw new InvalidOperationException("Workflow draft updates require a distributed lock provider."); await using var handle = await lockProvider.AcquireLockAsync(WorkflowDesignPersistenceLockKeys.DraftKey(request.DraftId), null, ct); await ExecuteCore(key, normalized, ct); }
    private HashSet<string> CollectNodeIds(ActivityNode? rootActivity)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        if (rootActivity is null)
            return nodeIds;

        var stack = new Stack<ActivityNode>();
        stack.Push(rootActivity);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!nodeIds.Add(node.NodeId))
                continue;

            foreach (var child in activityStructureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push(child);
        }

        return nodeIds;
    }
    private async Task ExecuteCore(DesignOperationKey key, UpdateDraftRequest request, CancellationToken ct)
    {
        IReadOnlyList<ValidationError> errors = []; var staged = false;
        var stateJson = EfDesignSupport.WriteState(serializer, request.State, "workflow.draft.replace.v1");
        await Atomic.ExecuteAsync(key, "workflow.draft.replace.v1", new UpdateDraftRequestMaterial(request.DraftId, stateJson, EfDesignSupport.LayoutMaterial(request.Layout), EfDesignSupport.PresentationMaterial(request.ActivityPresentation ?? [])), [DesignPersistenceUnitNames.Drafts, DesignPersistenceUnitNames.DraftLayouts], async token => { staged = true; var row = await Scoped(Db.Drafts, x => x.TenantId).SingleOrDefaultAsync(x => x.Id == request.DraftId, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), request.DraftId); row.State = request.State; row.StateSource = stateJson; errors = inlineEvents is null ? [] : (await inlineEvents.DeriveValidationErrorsAsync(row, token)).ToArray(); row.LastModifiedAt = Now; var layout = await Scoped(Db.DraftLayouts, x => x.TenantId).SingleOrDefaultAsync(x => x.WorkflowDefinitionDraftId == request.DraftId, token); if (layout is null) { layout = WorkflowDefinitionDraftLayout.CreateFor(new SequentialIdentityGenerator(), request.DraftId, request.Layout, request.ActivityPresentation); layout.TenantId = row.TenantId; Db.DraftLayouts.Add(layout); } else { EfDesignSupport.SetLayout(Db, layout, request.Layout, request.ActivityPresentation); layout.LastModifiedAt = Now; } return true; }, ct);
        if (staged && deferredEvents is not null) { var row = await Scoped(Db.Drafts.AsNoTracking(), x => x.TenantId).SingleAsync(x => x.Id == request.DraftId, ct); row.State = request.State; await deferredEvents.Publish(new DraftValidated(row, errors), CancellationToken.None); }
    }
}

public sealed class EfDiscardDraftCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IDistributedLockProvider? lockProvider = null, IDeferredEventPublisher? deferredEvents = null) : EfDesignCommand(db, access, atomic), IDiscardDraftCommand
{
    public async Task Execute(DesignOperationKey key, string draftId, CancellationToken ct = default) { ArgumentException.ThrowIfNullOrWhiteSpace(draftId); if (lockProvider is null) throw new InvalidOperationException("Workflow draft discard requires a distributed lock provider."); await using var handle = await lockProvider.AcquireLockAsync(WorkflowDesignPersistenceLockKeys.DraftKey(draftId), null, ct); await ExecuteCore(key, draftId, ct); }
    private async Task ExecuteCore(DesignOperationKey key, string draftId, CancellationToken ct) { string? definitionId = null; var removed = false; await Atomic.ExecuteAsync(key, "workflow.draft.discard.v1", new DiscardDraftRequestMaterial(draftId), [DesignPersistenceUnitNames.Drafts, DesignPersistenceUnitNames.DraftLayouts], async token => { var row = await Scoped(Db.Drafts, x => x.TenantId).SingleOrDefaultAsync(x => x.Id == draftId, token); if (row is not null) { definitionId = row.WorkflowDefinitionId; removed = true; Db.Drafts.Remove(row); } return true; }, ct); if (removed && deferredEvents is not null) await deferredEvents.Publish(new DraftDiscarded(draftId, definitionId!), CancellationToken.None); }
}

public sealed class EfDeleteWorkflowDefinitionPermanentlyCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IEnumerable<IWorkflowDefinitionPermanentDeletionGuard>? guards = null) : EfDesignCommand(db, access, atomic), IDeleteWorkflowDefinitionPermanentlyCommand
{
    public async Task Execute(DesignOperationKey key, string definitionId, CancellationToken ct = default) { ArgumentException.ThrowIfNullOrWhiteSpace(definitionId); var allGuards = (guards ?? []).ToArray(); var publicationGuards = allGuards.OfType<IWorkflowDefinitionPublicationDeletionGuard>().ToArray(); await Atomic.ExecuteAsync(key, "workflow.definition.permanent-delete.v1", new PermanentDeleteRequestMaterial(definitionId), [DesignPersistenceUnitNames.Definitions, DesignPersistenceUnitNames.Drafts, DesignPersistenceUnitNames.Versions, DesignPersistenceUnitNames.DraftLayouts, DesignPersistenceUnitNames.VersionLayouts], async token => { if (publicationGuards.Length == 0) throw new PermanentDeletionUnavailableException(definitionId); var idKey = EfDesignSupport.SearchKey(definitionId); var row = await Scoped(Db.Definitions, x => x.TenantId).SingleOrDefaultAsync(x => EF.Property<string>(x, "IdSearchKey") == idKey, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), definitionId); if (row.DeletedAt is null) throw new WorkflowDefinitionNotSoftDeletedException(row.Id); foreach (var guard in allGuards) await guard.EnsureCanDeleteAsync(row.Id, token); Db.Definitions.Remove(row); return true; }, ct); }
}

public sealed class EfPromoteDraftToVersionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IPayloadSerializer serializer, IIdentityGenerator identities, IWorkflowDefinitionVersionStore versionStore, IDistributedLockProvider? lockProvider = null, IInlineEventPublisher? inlineEvents = null) : EfDesignCommand(db, access, atomic), IPromoteDraftToVersionCommand
{
    public Task<string> Execute(DesignOperationKey key, string draftId, CancellationToken ct = default) => Execute(key, draftId, null, ct);
    public async Task<string> Execute(DesignOperationKey key, string draftId, string? requestedVersion, CancellationToken ct = default)
    {
        Serializer = serializer;
        if (lockProvider is null)
            throw new InvalidOperationException("Workflow draft promotion requires a distributed lock provider.");

        var normalizedRequestedVersion = requestedVersion?.Trim();
        IDistributedSynchronizationHandle? draftLock = null;
        IDistributedSynchronizationHandle? definitionLock = null;
        try
        {
            var result = await Atomic.ExecuteAsync(
                key,
                "workflow.draft.promote.v1",
                new PromoteDraftRequestMaterial(
                    draftId,
                    normalizedRequestedVersion is null ? "automatic" : "exact",
                    normalizedRequestedVersion),
                [DesignPersistenceUnitNames.Versions, DesignPersistenceUnitNames.VersionLayouts],
                async (_, token) =>
                {
                    var draft = await Scoped(Db.Drafts.AsNoTracking(), x => x.TenantId)
                        .SingleOrDefaultAsync(x => x.Id == draftId, token)
                        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), draftId);
                    draft = EfDesignSupport.MapDraft(serializer, draft);
                    if (inlineEvents is not null)
                    {
                        var errors = await inlineEvents.DeriveValidationErrorsAsync(draft, token);
                        if (errors.Count > 0)
                            throw new DraftHasValidationErrorsException(draftId, errors);
                    }

                    var latest = await versionStore.FindLatestVersionAsync(draft.WorkflowDefinitionId, token);
                    var initialAssessment = WorkflowVersionNumbering.AssessPromotion(
                        latest?.Version,
                        normalizedRequestedVersion,
                        versionIdentityExists: false);
                    var candidateIdentitySortKey = WorkflowVersionNumbering.GetCandidateIdentitySortKey(initialAssessment);
                    var versionIdentityExists = candidateIdentitySortKey is not null &&
                        await versionStore.ExistsAsync(draft.WorkflowDefinitionId, candidateIdentitySortKey, token);
                    var assessment = WorkflowVersionNumbering.AssessPromotion(
                        latest?.Version,
                        normalizedRequestedVersion,
                        versionIdentityExists);
                    ThrowIfRejected(assessment, draft.WorkflowDefinitionId);

                    var row = new WorkflowDefinitionVersion(draft.WorkflowDefinitionId, assessment.ResolvedVersion!)
                    {
                        Id = identities.Generate(),
                        TenantId = draft.TenantId,
                        SourceDraftId = draft.Id,
                        StateSource = draft.StateSource,
                        State = draft.State,
                        CreatedAt = Now,
                        LastModifiedAt = Now
                    };
                    var draftLayout = await Scoped(Db.DraftLayouts, x => x.TenantId)
                        .SingleOrDefaultAsync(x => x.WorkflowDefinitionDraftId == draft.Id, token);
                    var layout = new WorkflowDefinitionVersionLayout
                    {
                        Id = identities.Generate(),
                        TenantId = draft.TenantId,
                        WorkflowDefinitionVersionId = row.Id,
                        Records = draftLayout is null ? [] : EfDesignSupport.ReadLayout(Db.Entry(draftLayout).Property<string>("RecordsJson").CurrentValue),
                        ActivityPresentation = draftLayout is null ? [] : EfDesignSupport.ReadPresentation(Db.Entry(draftLayout).Property<string>("ActivityPresentationJson").CurrentValue)
                    };
                    Db.Versions.Add(row);
                    EfDesignSupport.SetLayout(Db, layout, layout.Records.ToArray(), layout.ActivityPresentation.ToArray());
                    Db.VersionLayouts.Add(layout);
                    return DesignAtomicWriteStage<string>.Accepted(row.Id);
                },
                beforeAttempt: async token =>
                {
                    if (definitionLock is not null)
                    {
                        await definitionLock.DisposeAsync();
                        definitionLock = null;
                    }
                    if (draftLock is not null)
                    {
                        await draftLock.DisposeAsync();
                        draftLock = null;
                    }
                    draftLock = await lockProvider.AcquireLockAsync(
                        WorkflowDesignPersistenceLockKeys.DraftKey(draftId), null, token);
                    var draft = await Scoped(Db.Drafts.AsNoTracking(), x => x.TenantId)
                        .SingleOrDefaultAsync(x => x.Id == draftId, token)
                        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), draftId);
                    definitionLock = await lockProvider.AcquireLockAsync(
                        WorkflowDesignPersistenceLockKeys.DefinitionKey(draft.WorkflowDefinitionId), null, token);
                },
                cancellationToken: ct);

            if (result.Status == DesignAtomicWriteStatus.Conflict)
                throw new WorkflowPromotionOperationConflictException(
                    $"Workflow promotion operation '{key.Value}' was previously recorded with different request material.");
            return result.Value!;
        }
        catch (DesignPersistenceException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            throw new WorkflowDefinitionVersionConflictException(
                draftId,
                normalizedRequestedVersion ?? "automatic");
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            throw new WorkflowDefinitionVersionConflictException(
                draftId,
                normalizedRequestedVersion ?? "automatic");
        }
        finally
        {
            try
            {
                if (definitionLock is not null)
                    await definitionLock.DisposeAsync();
            }
            finally
            {
                if (draftLock is not null)
                    await draftLock.DisposeAsync();
            }
        }

        static void ThrowIfRejected(WorkflowPromotionVersionAssessment assessment, string definitionId)
        {
            if (assessment.IsReady)
                return;

            var issue = assessment.Issues.Single();
            if (issue.Code == "version-conflict")
                throw new WorkflowDefinitionVersionConflictException(
                    definitionId,
                    assessment.RequestedVersion ?? assessment.ResolvedVersion ?? "automatic");

            throw new WorkflowVersionSelectionException(issue.Code, issue.Message);
        }

    }
}

public sealed class EfSubmitWorkflowDefinitionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IPayloadSerializer serializer, IIdentityGenerator identities, IActivityStructureService activityStructureService) : EfDesignCommand(db, access, atomic), ISubmitWorkflowDefinitionCommand
{
    public async Task<SubmittedWorkflowDefinition> Execute(DesignOperationKey key, string name, string? description, WorkflowDefinitionState state, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(state);
        var tenant = Access.Current.Scope?.Value;
        var definition = new WorkflowDefinition { Id = identities.Generate(), TenantId = tenant, Name = name, Description = description, CreatedAt = Now, LastModifiedAt = Now };
        var draft = Draft(identities.Generate(), definition.Id, tenant, state);
        draft.StateSource = EfDesignSupport.WriteState(serializer, state, "workflow.definition.submit.v1");
        var draftLayout = WorkflowDefinitionDraftLayout.CreateFor(identities, draft.Id);
        draftLayout.TenantId = tenant;
        EfDesignSupport.Stamp(draftLayout, Now);
        var version = new WorkflowDefinitionVersion(definition.Id, "1.0.0") { Id = identities.Generate(), TenantId = tenant, StateSource = draft.StateSource, State = state, CreatedAt = Now, LastModifiedAt = Now };
        var layout = new WorkflowDefinitionVersionLayout { Id = identities.Generate(), TenantId = tenant, WorkflowDefinitionVersionId = version.Id, CreatedAt = Now, LastModifiedAt = Now, Records = [], ActivityPresentation = [] };
        var result = await Atomic.ExecuteAsync<SubmittedWorkflowDefinition>(
            key,
            "workflow.definition.submit.v1",
            new SubmitWorkflowDefinitionRequestMaterial(name, description, draft.StateSource!),
            [DesignPersistenceUnitNames.Definitions, DesignPersistenceUnitNames.Drafts, DesignPersistenceUnitNames.DraftLayouts, DesignPersistenceUnitNames.Versions, DesignPersistenceUnitNames.VersionLayouts],
            async (_, token) =>
            {
                Db.Definitions.Add(definition);
                Db.Drafts.Add(draft);
                EfDesignSupport.SetLayout(Db, draftLayout, [], []);
                Db.DraftLayouts.Add(draftLayout);
                Db.Versions.Add(version);
                EfDesignSupport.SetLayout(Db, layout, [], []);
                Db.VersionLayouts.Add(layout);
                await Task.CompletedTask;
                return DesignAtomicWriteStage<SubmittedWorkflowDefinition>.Accepted(
                    new SubmittedWorkflowDefinition(definition.Id, draft.Id, version.Id));
            },
            beforeAttempt: token =>
            {
                token.ThrowIfCancellationRequested();
                SubmittedActivityTreeValidator.Validate(state.RootActivity, activityStructureService);
                return Task.CompletedTask;
            },
            cancellationToken: ct);
        return result.Value!;
    }
}

internal sealed class SequentialIdentityGenerator : IIdentityGenerator
{
    public string Generate() => Guid.NewGuid().ToString("N");
}

internal sealed record CreateWorkflowDefinitionRequestMaterial(
    string Name,
    string? Description,
    DateTimeOffset? DeletedAt,
    string? DeletedReason,
    bool IsSourceOwned,
    string? SourceVersionId,
    string StateJson,
    IReadOnlyCollection<DesignLayoutMaterial> Layout,
    IReadOnlyCollection<DesignActivityPresentationMaterial> ActivityPresentation);

internal sealed record MaterializeWorkflowDefinitionRequestMaterial(
    string DefinitionId,
    string Name,
    string? Description,
    DateTimeOffset? DeletedAt,
    string? DeletedReason,
    bool IsSourceOwned);

internal sealed record SaveWorkflowDefinitionRequestMaterial(
    string DefinitionId,
    string Name,
    string? Description,
    DateTimeOffset? DeletedAt,
    string? DeletedReason,
    bool IsSourceOwned);

internal sealed record CreateDraftRequestMaterial(
    string WorkflowDefinitionId,
    string StateJson,
    IReadOnlyCollection<DesignLayoutMaterial> Layout,
    string? SourceVersionId);

internal sealed record CloneDraftRequestMaterial(string SourceVersionId);

internal sealed record AddWorkflowDefinitionVersionRequestMaterial(
    string DefinitionId,
    string StateJson);

internal sealed record MaterializeWorkflowDefinitionVersionRequestMaterial(
    string DefinitionId,
    string VersionId,
    string Version,
    string? SourceDraftId,
    DateTimeOffset? SourceCreatedAt,
    string StateJson);

internal sealed record UpdateDraftRequestMaterial(
    string DraftId,
    string StateJson,
    IReadOnlyCollection<DesignLayoutMaterial> Layout,
    IReadOnlyCollection<DesignActivityPresentationMaterial> ActivityPresentation);

internal sealed record DiscardDraftRequestMaterial(string DraftId);

internal sealed record PermanentDeleteRequestMaterial(string DefinitionId);

internal sealed record PromoteDraftRequestMaterial(
    string DraftId,
    string AssignmentMode,
    string? RequestedVersion);

internal sealed record SubmitWorkflowDefinitionRequestMaterial(
    string Name,
    string? Description,
    string StateJson);
