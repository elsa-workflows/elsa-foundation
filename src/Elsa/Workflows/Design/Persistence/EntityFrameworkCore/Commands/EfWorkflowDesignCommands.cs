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
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;

public abstract class EfDesignCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic)
{
    protected WorkflowsDesignDbContext Db => db;
    protected IPersistenceAccessContextAccessor Access => access;
    protected IDesignAtomicWriter Atomic => atomic;
    protected void Tenant(string? id) => EfDesignSupport.EnsureTenant(access, id);
    protected IQueryable<T> Scoped<T>(IQueryable<T> query, Func<T, string?> tenant) where T : class => EfDesignSupport.InScope(query, access, tenant);
    protected static DateTimeOffset Now => DateTimeOffset.UtcNow;
    protected static WorkflowDefinitionState EmptyState() => new([], null, [], [], null);
    protected static WorkflowDefinitionDraft Draft(string id, string definitionId, string? tenant, WorkflowDefinitionState state, string? sourceVersionId = null) => new() { Id = id, WorkflowDefinitionId = definitionId, TenantId = tenant, StateSource = null, State = state, SourceVersionId = sourceVersionId, CreatedAt = Now, LastModifiedAt = Now };
    protected void SaveDraftState(WorkflowDefinitionDraft row, WorkflowDefinitionState state) { row.State = state; row.StateSource = EfDesignSupport.WriteState(Serializer, state); }
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
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(draft); Tenant(definition.TenantId); Tenant(draft.TenantId);
        if (definition.Id != draft.WorkflowDefinitionId) throw new ArgumentException("The first draft must belong to the definition.", nameof(draft));
        Serializer = serializer; SaveDraftState(draft, draft.State); EfDesignSupport.Stamp(definition, Now); EfDesignSupport.Stamp(draft, Now);
        return await Atomic.ExecuteAsync(key, "workflow.definition.create.v1", new { DefinitionId = definition.Id, definition.Name, definition.Description, DraftId = draft.Id, State = draft.StateSource, Layout = layout, Presentation = presentation }, async token => { Db.Definitions.Add(definition); Db.Drafts.Add(draft); var sibling = WorkflowDefinitionDraftLayout.CreateFor(identities, draft.Id, layout, presentation); sibling.TenantId = draft.TenantId; EfDesignSupport.Stamp(sibling, Now); EfDesignSupport.SetLayout(Db, sibling, layout, presentation); Db.DraftLayouts.Add(sibling); await Task.CompletedTask; return new WorkflowDefinitionCreated(definition.Id, draft.Id); }, ct);
    }
}

public sealed class EfMaterializeWorkflowDefinitionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic) : EfDesignCommand(db, access, atomic), IMaterializeWorkflowDefinitionCommand
{
    public async Task<string> Execute(DesignOperationKey key, WorkflowDefinition definition, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(definition); Tenant(definition.TenantId); EfDesignSupport.Stamp(definition, Now); return await Atomic.ExecuteAsync(key, "workflow.definition.materialize.v1", new { definition.Id, definition.Name, definition.Description, definition.DeletedAt, definition.IsSourceOwned }, async token => { Db.Definitions.Add(definition); await Task.CompletedTask; return definition.Id; }, ct); }
}

public sealed class EfSaveWorkflowDefinitionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic) : EfDesignCommand(db, access, atomic), ISaveWorkflowDefinitionCommand
{
    public async Task Execute(DesignOperationKey key, WorkflowDefinition definition, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(definition); Tenant(definition.TenantId); await Atomic.ExecuteAsync(key, "workflow.definition.save.v1", new { definition.Id, definition.Name, definition.Description, definition.DeletedAt, definition.DeletedReason, definition.IsSourceOwned }, async token => { var row = await Scoped(Db.Definitions, x => x.TenantId).SingleOrDefaultAsync(x => x.Id == definition.Id, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), definition.Id); row.Name = definition.Name; row.Description = definition.Description; row.DeletedAt = definition.DeletedAt; row.DeletedReason = definition.DeletedReason; row.IsSourceOwned = definition.IsSourceOwned; row.LastModifiedAt = Now; return definition.Id; }, ct); }
}

public sealed class EfCreateDraftCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IIdentityGenerator identities, IPayloadSerializer serializer, IDistributedLockProvider? lockProvider = null, IInlineEventPublisher? inlineEvents = null, IDeferredEventPublisher? deferredEvents = null) : EfDesignCommand(db, access, atomic), ICreateDraftCommand
{
    public async Task<string> Execute(DesignOperationKey key, string workflowDefinitionId, WorkflowDefinitionState? initialState = null, IReadOnlyCollection<DesignMetadataRecord>? initialLayout = null, string? sourceVersionId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId); Serializer = serializer; var state = initialState ?? EmptyState(); var id = identities.Generate(); var tenant = Access.Current.Scope?.Value; var draft = Draft(id, workflowDefinitionId, tenant, state, sourceVersionId); SaveDraftState(draft, state); var layout = initialLayout ?? []; return await ExecuteLocked(key, workflowDefinitionId, sourceVersionId, draft, layout, ct);
    }
    private async Task<string> ExecuteLocked(DesignOperationKey key, string definitionId, string? sourceVersionId, WorkflowDefinitionDraft draft, IReadOnlyCollection<DesignMetadataRecord> layout, CancellationToken ct)
    {
        if (lockProvider is not null) { await using var handle = await lockProvider.AcquireLockAsync(WorkflowDesignPersistenceLockKeys.DraftKey(draft.Id), null, ct); return await ExecuteCore(key, definitionId, sourceVersionId, draft, layout, ct); }
        return await ExecuteCore(key, definitionId, sourceVersionId, draft, layout, ct);
    }
    private async Task<string> ExecuteCore(DesignOperationKey key, string definitionId, string? sourceVersionId, WorkflowDefinitionDraft draft, IReadOnlyCollection<DesignMetadataRecord> layout, CancellationToken ct)
    {
        var errors = inlineEvents is null ? [] : (await inlineEvents.DeriveValidationErrorsAsync(draft, ct)).ToArray();
        var id = await Atomic.ExecuteAsync(key, "workflow.draft.create.v1", new { definitionId, sourceVersionId, State = draft.StateSource, layout }, async token => { Db.Drafts.Add(draft); var sibling = WorkflowDefinitionDraftLayout.CreateFor(identities, draft.Id, layout); sibling.TenantId = draft.TenantId; EfDesignSupport.Stamp(sibling, Now); EfDesignSupport.SetLayout(Db, sibling, layout); Db.DraftLayouts.Add(sibling); return draft.Id; }, ct);
        if (deferredEvents is not null) { await deferredEvents.Publish(new DraftCreated(draft.Id, definitionId, sourceVersionId), CancellationToken.None); await deferredEvents.Publish(new DraftValidated(draft, errors), CancellationToken.None); }
        return id;
    }
}

public sealed class EfCloneDraftFromVersionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IIdentityGenerator identities, IPayloadSerializer serializer, IDistributedLockProvider? lockProvider = null, IInlineEventPublisher? inlineEvents = null, IDeferredEventPublisher? deferredEvents = null) : EfDesignCommand(db, access, atomic), ICloneDraftFromVersionCommand
{
    public async Task<string> Execute(DesignOperationKey key, string sourceVersionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVersionId); Serializer = serializer;
        IReadOnlyList<ValidationError> errors = [];
        IDistributedSynchronizationHandle? draftLock = null;
        string result;
        try
        {
            result = await Atomic.ExecuteAsync(key, "workflow.draft.clone-from-version.v1", new { sourceVersionId }, async token =>
            {
                var source = await Scoped(Db.Versions.AsNoTracking(), x => x.TenantId).SingleOrDefaultAsync(x => x.Id == sourceVersionId, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionVersion), sourceVersionId);
                Tenant(source.TenantId); var state = EfDesignSupport.ReadState(serializer, source.StateSource);
                var layout = await Scoped(Db.VersionLayouts.AsNoTracking(), x => x.TenantId).SingleOrDefaultAsync(x => x.WorkflowDefinitionVersionId == sourceVersionId, token);
                var records = layout is null ? [] : EfDesignSupport.ReadLayout(Db.Entry(layout).Property<string>("RecordsJson").CurrentValue);
                var presentation = layout is null ? [] : EfDesignSupport.ReadPresentation(Db.Entry(layout).Property<string>("ActivityPresentationJson").CurrentValue);
                var id = identities.Generate(); var draft = Draft(id, source.DefinitionId, source.TenantId, state, sourceVersionId); SaveDraftState(draft, state);
                if (lockProvider is not null) draftLock = await lockProvider.AcquireLockAsync(WorkflowDesignPersistenceLockKeys.DraftKey(id), null, token);
                if (inlineEvents is not null) errors = (await inlineEvents.DeriveValidationErrorsAsync(draft, token)).ToArray();
                Db.Drafts.Add(draft); var sibling = WorkflowDefinitionDraftLayout.CreateFor(identities, id, records, presentation); sibling.TenantId = source.TenantId; EfDesignSupport.Stamp(sibling, Now); EfDesignSupport.SetLayout(Db, sibling, records, presentation); Db.DraftLayouts.Add(sibling); return id;
            }, ct);
        }
        finally
        {
            if (draftLock is not null) await draftLock.DisposeAsync();
        }
        if (deferredEvents is not null) { var draft = await Scoped(Db.Drafts.AsNoTracking(), x => x.TenantId).SingleAsync(x => x.Id == result, ct); await deferredEvents.Publish(new DraftCreated(result, draft.WorkflowDefinitionId, sourceVersionId), CancellationToken.None); await deferredEvents.Publish(new DraftValidated(draft, errors), CancellationToken.None); }
        return result;
    }
}

public sealed class EfAddWorkflowDefinitionVersionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IPayloadSerializer serializer, IIdentityGenerator identities, IDistributedLockProvider? lockProvider = null) : EfDesignCommand(db, access, atomic), IAddWorkflowDefinitionVersionCommand
{
    public async Task<WorkflowDefinitionVersionAdded> Execute(DesignOperationKey key, string definitionId, WorkflowDefinitionState state, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId); ArgumentNullException.ThrowIfNull(state); Serializer = serializer;
        if (lockProvider is null) return await ExecuteCore(key, definitionId, state, ct);
        await using var handle = await lockProvider.AcquireLockAsync(WorkflowDesignPersistenceLockKeys.DefinitionKey(definitionId), null, ct);
        return await ExecuteCore(key, definitionId, state, ct);
    }
    private Task<WorkflowDefinitionVersionAdded> ExecuteCore(DesignOperationKey key, string definitionId, WorkflowDefinitionState state, CancellationToken ct) => Atomic.ExecuteAsync(key, "workflow.version.add.v1", new { definitionId, state }, async token => { var definition = await Scoped(Db.Definitions.AsNoTracking(), x => x.TenantId).SingleOrDefaultAsync(x => x.Id == definitionId, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), definitionId); var latest = await Scoped(Db.Versions.AsNoTracking(), x => x.TenantId).Where(x => x.DefinitionId == definitionId).OrderByDescending(x => x.SemVerSortKey).FirstOrDefaultAsync(token); var version = WorkflowVersionNumbering.NextMajor(latest?.Version); var row = new WorkflowDefinitionVersion(definitionId, version) { Id = identities.Generate(), TenantId = definition.TenantId, StateSource = serializer.Serialize(state), State = state, CreatedAt = Now, LastModifiedAt = Now }; Db.Versions.Add(row); return new WorkflowDefinitionVersionAdded(definitionId, row.Id, version); }, ct);
}

public sealed class EfMaterializeWorkflowDefinitionVersionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IPayloadSerializer serializer) : EfDesignCommand(db, access, atomic), IMaterializeWorkflowDefinitionVersionCommand
{
    public async Task<WorkflowDefinitionVersionAdded> Execute(DesignOperationKey key, WorkflowDefinitionVersion version, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(version); Tenant(version.TenantId); version.StateSource ??= serializer.Serialize(version.State); EfDesignSupport.Stamp(version, Now); return await Atomic.ExecuteAsync(key, "workflow.version.materialize.v1", new { version.Id, version.DefinitionId, version.Version, version.StateSource }, async token => { Db.Versions.Add(version); await Task.CompletedTask; return new WorkflowDefinitionVersionAdded(version.DefinitionId, version.Id, version.Version); }, ct); }
}

public sealed class EfUpdateDraftCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IPayloadSerializer serializer, IDistributedLockProvider? lockProvider = null, IInlineEventPublisher? inlineEvents = null, IDeferredEventPublisher? deferredEvents = null) : EfDesignCommand(db, access, atomic), IUpdateDraftCommand
{
    public async Task Execute(DesignOperationKey key, UpdateDraftRequest request, CancellationToken ct = default) { ArgumentNullException.ThrowIfNull(request); Serializer = serializer; if (lockProvider is null) { await ExecuteCore(key, request, ct); return; } await using var handle = await lockProvider.AcquireLockAsync(WorkflowDesignPersistenceLockKeys.DraftKey(request.DraftId), null, ct); await ExecuteCore(key, request, ct); }
    private async Task ExecuteCore(DesignOperationKey key, UpdateDraftRequest request, CancellationToken ct)
    {
        IReadOnlyList<ValidationError> errors = [];
        await Atomic.ExecuteAsync(key, "workflow.draft.replace.v1", new { request.DraftId, request.State, request.Layout, request.ActivityPresentation }, async token => { var row = await Scoped(Db.Drafts, x => x.TenantId).SingleOrDefaultAsync(x => x.Id == request.DraftId, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), request.DraftId); row.State = request.State; SaveDraftState(row, request.State); errors = inlineEvents is null ? [] : (await inlineEvents.DeriveValidationErrorsAsync(row, token)).ToArray(); row.LastModifiedAt = Now; var layout = await Scoped(Db.DraftLayouts, x => x.TenantId).SingleOrDefaultAsync(x => x.WorkflowDefinitionDraftId == request.DraftId, token); if (layout is null) { layout = WorkflowDefinitionDraftLayout.CreateFor(new SequentialIdentityGenerator(), request.DraftId, request.Layout, request.ActivityPresentation); layout.TenantId = row.TenantId; Db.DraftLayouts.Add(layout); } else { EfDesignSupport.SetLayout(Db, layout, request.Layout, request.ActivityPresentation); layout.LastModifiedAt = Now; } return true; }, ct);
        if (deferredEvents is not null) { var row = await Scoped(Db.Drafts.AsNoTracking(), x => x.TenantId).SingleAsync(x => x.Id == request.DraftId, ct); row.State = request.State; await deferredEvents.Publish(new DraftValidated(row, errors), CancellationToken.None); }
    }
}

public sealed class EfDiscardDraftCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic) : EfDesignCommand(db, access, atomic), IDiscardDraftCommand
{
    public async Task Execute(DesignOperationKey key, string draftId, CancellationToken ct = default) { ArgumentException.ThrowIfNullOrWhiteSpace(draftId); await Atomic.ExecuteAsync(key, "workflow.draft.discard.v1", new { draftId }, async token => { var row = await Scoped(Db.Drafts, x => x.TenantId).SingleOrDefaultAsync(x => x.Id == draftId, token); if (row is not null) Db.Drafts.Remove(row); return true; }, ct); }
}

public sealed class EfDeleteWorkflowDefinitionPermanentlyCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IEnumerable<IWorkflowDefinitionPermanentDeletionGuard>? guards = null) : EfDesignCommand(db, access, atomic), IDeleteWorkflowDefinitionPermanentlyCommand
{
    public async Task Execute(DesignOperationKey key, string definitionId, CancellationToken ct = default) { ArgumentException.ThrowIfNullOrWhiteSpace(definitionId); var publicationGuards = (guards ?? []).OfType<IWorkflowDefinitionPublicationDeletionGuard>().ToArray(); await Atomic.ExecuteAsync(key, "workflow.definition.permanent-delete.v1", new { definitionId }, async token => { if (publicationGuards.Length == 0) throw new PermanentDeletionUnavailableException(definitionId); var row = await Scoped(Db.Definitions, x => x.TenantId).SingleOrDefaultAsync(x => x.Id == definitionId, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), definitionId); if (row.DeletedAt is null) throw new WorkflowDefinitionNotSoftDeletedException(definitionId); foreach (var guard in publicationGuards) await guard.EnsureCanDeleteAsync(definitionId, token); Db.Definitions.Remove(row); return true; }, ct); }
}

public sealed class EfPromoteDraftToVersionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IPayloadSerializer serializer, IIdentityGenerator identities, IDistributedLockProvider? lockProvider = null, IInlineEventPublisher? inlineEvents = null) : EfDesignCommand(db, access, atomic), IPromoteDraftToVersionCommand
{
    public Task<string> Execute(DesignOperationKey key, string draftId, CancellationToken ct = default) => Execute(key, draftId, null, ct);
    public async Task<string> Execute(DesignOperationKey key, string draftId, string? requestedVersion, CancellationToken ct = default) { Serializer = serializer; if (lockProvider is null) return await ExecuteCore(key, draftId, requestedVersion, ct); await using var handle = await lockProvider.AcquireLockAsync(WorkflowDesignPersistenceLockKeys.DraftKey(draftId), null, ct); return await ExecuteCore(key, draftId, requestedVersion, ct); }
    private Task<string> ExecuteCore(DesignOperationKey key, string draftId, string? requestedVersion, CancellationToken ct) => Atomic.ExecuteAsync(key, "workflow.draft.promote.v1", new { draftId, requestedVersion = requestedVersion?.Trim() }, async token => { var draft = await Scoped(Db.Drafts.AsNoTracking(), x => x.TenantId).SingleOrDefaultAsync(x => x.Id == draftId, token) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), draftId); if (inlineEvents is not null) { var errors = await inlineEvents.DeriveValidationErrorsAsync(draft, token); if (errors.Count > 0) throw new DraftHasValidationErrorsException(draftId, errors); } var latest = await Scoped(Db.Versions.AsNoTracking(), x => x.TenantId).Where(x => x.DefinitionId == draft.WorkflowDefinitionId).OrderByDescending(x => x.SemVerSortKey).FirstOrDefaultAsync(token); var version = requestedVersion?.Trim() ?? WorkflowVersionNumbering.NextMajor(latest?.Version); if (!SemVer.TryParse(version, out var parsed)) throw new WorkflowVersionSelectionException("invalid-version", "The requested version must be a valid semantic version."); if (latest is not null && SemVer.TryParse(latest.Version, out var current) && parsed <= current) throw new WorkflowVersionSelectionException("not-forward", "The requested version must be greater than the latest immutable version."); var row = new WorkflowDefinitionVersion(draft.WorkflowDefinitionId, version) { Id = identities.Generate(), TenantId = draft.TenantId, SourceDraftId = draft.Id, StateSource = draft.StateSource, State = EfDesignSupport.ReadState(serializer, draft.StateSource), CreatedAt = Now, LastModifiedAt = Now }; var draftLayout = await Scoped(Db.DraftLayouts, x => x.TenantId).SingleOrDefaultAsync(x => x.WorkflowDefinitionDraftId == draft.Id, token); var layout = new WorkflowDefinitionVersionLayout { Id = identities.Generate(), TenantId = draft.TenantId, WorkflowDefinitionVersionId = row.Id, Records = draftLayout is null ? [] : EfDesignSupport.ReadLayout(Db.Entry(draftLayout).Property<string>("RecordsJson").CurrentValue), ActivityPresentation = draftLayout is null ? [] : EfDesignSupport.ReadPresentation(Db.Entry(draftLayout).Property<string>("ActivityPresentationJson").CurrentValue) }; Db.Versions.Add(row); EfDesignSupport.SetLayout(Db, layout, layout.Records.ToArray(), layout.ActivityPresentation.ToArray()); Db.VersionLayouts.Add(layout); return row.Id; }, ct);
}

public sealed class EfSubmitWorkflowDefinitionCommand(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic, IPayloadSerializer serializer, IIdentityGenerator identities) : EfDesignCommand(db, access, atomic), ISubmitWorkflowDefinitionCommand
{
    public async Task<SubmittedWorkflowDefinition> Execute(DesignOperationKey key, string name, string? description, WorkflowDefinitionState state, CancellationToken ct = default) { ArgumentException.ThrowIfNullOrWhiteSpace(name); var tenant = Access.Current.Scope?.Value; var definition = new WorkflowDefinition { Id = identities.Generate(), TenantId = tenant, Name = name, Description = description, CreatedAt = Now, LastModifiedAt = Now }; var draft = Draft(identities.Generate(), definition.Id, tenant, state); draft.StateSource = serializer.Serialize(state); var version = new WorkflowDefinitionVersion(definition.Id, "1.0.0") { Id = identities.Generate(), TenantId = tenant, StateSource = draft.StateSource, State = state, CreatedAt = Now, LastModifiedAt = Now }; var layout = new WorkflowDefinitionVersionLayout { Id = identities.Generate(), TenantId = tenant, WorkflowDefinitionVersionId = version.Id, CreatedAt = Now, LastModifiedAt = Now, Records = [], ActivityPresentation = [] }; return await Atomic.ExecuteAsync(key, "workflow.definition.submit.v1", new { name, description, state }, async token => { Db.Definitions.Add(definition); Db.Drafts.Add(draft); Db.Versions.Add(version); EfDesignSupport.SetLayout(Db, layout, [], []); Db.VersionLayouts.Add(layout); return new SubmittedWorkflowDefinition(definition.Id, draft.Id, version.Id); }, ct); }
}

internal sealed class SequentialIdentityGenerator : IIdentityGenerator
{
    public string Generate() => Guid.NewGuid().ToString("N");
}
