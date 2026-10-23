using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Exceptions;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Microsoft.EntityFrameworkCore;
using static Elsa3.Activities.Design.Import.Services.ReusableActivityImportCommitRules;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Commits one selected Elsa 3 closure atomically across the import ledger, Activities Design, and
/// Workflows Design. One <see cref="EfSharedTransaction"/> owns a single physical connection and
/// transaction; the import rows, the Workflows Design materialization commands, the Activities Design add
/// commands, and the management projection writer all run inside it through their own atomic writers, so
/// either every row of the import is durable or none is.
/// </summary>
/// <remarks>
/// Imported reusable activity versions are not written here: the commit creates each new activity definition with its
/// authoring state (no head) and the draft of its first version, and the import's publish phase publishes the versions
/// afterwards (spec 246 D-1). Every Design identity is preflighted inside the transaction before the first write. Identical resources
/// are reused, which makes a reviewed plan safe to reapply; an identity bound to different content or to no
/// import provenance fails before any write. A write conflict retries the whole unit from durable state; a
/// commit whose outcome is unknown is reconciled from the durable receipt and never reported as applied
/// without it.
/// </remarks>
public sealed class EfReusableActivityImportCommand(
    Elsa3ImportDbContext importDb,
    ActivitiesDesignDbContext activitiesDb,
    WorkflowsDesignDbContext workflowsDb,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IPayloadSerializer payloadSerializer,
    TimeProvider? timeProvider = null) : IReusableActivityImportCommand
{
    private static readonly EfWriteRetry WriteConflicts = new(EfWriteRetry.DefaultMaxAttempts, IsWriteConflict);
    private const int ReconciliationReads = 3;
    private const string DefinitionBindingKind = "elsa3ReusableImportDefinitionBinding";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async ValueTask<ReusableActivityImportCommitResult> CommitAsync(
        ReusableActivityImportMutation mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        Validate(mutation);
        var scoped = mutation.AccessScope is not null;
        if (scoped)
        {
            Elsa3ImportScopeGuard.EnsureCurrent(accessContextAccessor, mutation.AccessScope!, hideMismatch: false, ScopeIdentity(mutation.AccessScope!));
            var prior = await FindReceiptAsync(importDb, mutation, cancellationToken);
            if (prior is not null)
                return new(true, prior.Receipt with { Status = ReusableActivityImportReceiptStatus.AlreadyImported });
        }

        var tenantId = WritableTenant(mutation);
        var plan = CreatePlan(mutation, tenantId, scoped);

        // Every attempt opens its own shared transaction over fresh contexts, so no caller transaction can be in scope.
        return await WriteConflicts.RunUntilSettledAsync<ReusableActivityImportCommitResult>(null, async () =>
        {
            var commitAttemptId = Guid.NewGuid().ToString("N");
            var outcome = await AttemptAsync(mutation, plan, tenantId, scoped, commitAttemptId, cancellationToken);
            if (outcome.Result is not null)
                return outcome.Result;

            var failure = outcome.Failure!;
            if (failure is SchemaWriteRefusedException)
                ExceptionDispatchInfo.Throw(failure);
            if (failure is EfCommitOutcomeUnknownException)
                return await ReconcileUncertainCommitAsync(mutation, scoped, commitAttemptId, failure, cancellationToken);
            if (!WriteConflicts.ShouldRetry(null, failure))
                throw Persistence("atomic apply", mutation, failure);
            return EfWriteAttempt<ReusableActivityImportCommitResult>.Retry(failure);
        }, async failure =>
        {
            if (scoped)
            {
                var reconciled = await ReconcileAsync(mutation, failure!);
                if (reconciled is not null)
                    return new(true, reconciled.Receipt with { Status = ReusableActivityImportReceiptStatus.AlreadyImported });
                throw new ReusableActivityImportCollisionException(
                    "The Elsa 3 import identities changed before the atomic commit; no partial import was written.",
                    failure!);
            }

            throw Persistence("atomic apply", mutation, failure!);
        }, cancellationToken);
    }

    private async Task<AttemptOutcome> AttemptAsync(
        ReusableActivityImportMutation mutation,
        ImportPlan plan,
        string tenantId,
        bool scoped,
        string commitAttemptId,
        CancellationToken cancellationToken)
    {
        EfSharedTransaction shared;
        try
        {
            shared = await EfSharedTransaction.BeginAsync([importDb, activitiesDb, workflowsDb], cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Persistence("atomic apply", mutation, exception);
        }

        try
        {
            if (scoped)
            {
                // Authoritative inside the transaction: a concurrent apply of the same key that committed
                // after the early check is found here, and the attempt returns its receipt without writing.
                var prior = await FindReceiptAsync(shared.Context<Elsa3ImportDbContext>(), mutation, cancellationToken);
                if (prior is not null)
                    return new(new(true, prior.Receipt with { Status = ReusableActivityImportReceiptStatus.AlreadyImported }), null);
            }

            var preflight = await PreflightAsync(shared, plan, tenantId, scoped, cancellationToken);
            var receipt = scoped
                ? BuildReceipt(mutation, (kind, id) => preflight.Created.Contains((kind, id)), clock.GetUtcNow())
                : null;
            if (!preflight.HasWrites && receipt is null)
                return new(new(true, null), null);

            try
            {
                await WriteAsync(shared, plan, preflight, receipt, tenantId, commitAttemptId, cancellationToken);
                await shared.CommitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!shared.CommitWasAttempted)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new(null, exception);
            }

            return new(new(false, receipt), null);
        }
        finally
        {
            await shared.DisposeAsync();
        }
    }

    private async Task<Preflight> PreflightAsync(
        EfSharedTransaction shared,
        ImportPlan plan,
        string tenantId,
        bool scoped,
        CancellationToken cancellationToken)
    {
        var import = shared.Context<Elsa3ImportDbContext>();
        var activities = shared.Context<ActivitiesDesignDbContext>();
        var workflows = shared.Context<WorkflowsDesignDbContext>();
        var workflowDefinitions = new EfWorkflowDefinitionStore(workflows, accessContextAccessor);
        var workflowVersions = new EfWorkflowDefinitionVersionStore(workflows, payloadSerializer, workflowDefinitions, accessContextAccessor);
        var preflight = new Preflight();

        // One read per collection instead of one per item. Each lookup keeps the identity the single-item read used
        // — the hashed identity inside the tenant partition, or the Design stores' own by-id route — so an identity
        // absent from a batch means exactly what `existing is null` meant before, and a drifted row still fails
        // closed under its own identity. Nothing is written during preflight, so the batches see the same rows the
        // sequential reads would have seen.
        var bindingRows = await LoadBindingsAsync(import, plan.Bindings.Select(binding => binding.Id), tenantId, cancellationToken);
        var definitionRows = await LoadActivityDefinitionsAsync(activities, plan.Definitions.Select(definition => definition.Id), tenantId, cancellationToken);
        var takenActivityTypeKeys = await LoadTakenActivityTypeKeysAsync(activities, plan.Definitions.Select(definition => definition.ActivityTypeKey), tenantId, cancellationToken);
        var workflowDefinitionRows = await LoadWorkflowDefinitionsAsync(workflowDefinitions, plan.WorkflowDefinitions.Select(definition => definition.Id), cancellationToken);
        var authoringRows = await LoadAuthoringStatesAsync(activities, plan.Authoring.Select(authoring => authoring.DefinitionId), tenantId, cancellationToken);
        var workflowVersionRows = await LoadWorkflowVersionsAsync(workflowVersions, plan.WorkflowVersions.Select(version => version.Id), cancellationToken);

        // A catalog row under a plan version id exists only once that version is published (spec 246 FR-004).
        var versionRows = await LoadActivityVersionsAsync(activities, plan.Versions.Select(version => version.Id), tenantId, cancellationToken);

        var existingBindings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in plan.Bindings)
        {
            if (bindingRows.GetValueOrDefault(binding.Id) is not { } existing)
            {
                preflight.NewBindings.Add(binding);
                continue;
            }

            if (!SameBinding(existing, binding))
                throw Collision($"Elsa 3 import identity '{DefinitionBindingKind}/{binding.Id}' is already bound to different content.", scoped);
            existingBindings.Add(binding.Id);
        }

        foreach (var definition in plan.Definitions)
        {
            if (definitionRows.GetValueOrDefault(definition.Id) is not { } existing)
            {
                if (takenActivityTypeKeys.Contains(definition.ActivityTypeKey))
                    throw Collision($"Elsa 3 activity definition identity '{definition.Id}' is already owned by a different resource.", scoped);
                preflight.NewDefinitions.Add(definition);
                preflight.Created.Add((ActivityDefinitionKind, definition.Id));
                continue;
            }

            EnsureReusableDefinitionCompatible(existing, definition, scoped);
            EnsureDefinitionBinding(ActivityDefinitionKind, definition.Id, plan, existingBindings, scoped);
            preflight.PersistedDefinitions[definition.Id] = existing;
        }

        foreach (var definition in plan.WorkflowDefinitions)
        {
            if (workflowDefinitionRows.GetValueOrDefault(definition.Id) is not { } existing)
            {
                preflight.NewWorkflowDefinitions.Add(definition);
                preflight.Created.Add((WorkflowDefinitionKind, definition.Id));
                continue;
            }

            EnsureWorkflowDefinitionCompatible(existing, definition, scoped);
            EnsureDefinitionBinding(WorkflowDefinitionKind, definition.Id, plan, existingBindings, scoped);
        }

        // The commit writes no version. A version that is not yet published is reported as created by this apply,
        // which its publish phase publishes; a published one must belong to the definition the plan names, and
        // whether it is the same version is the publish phase's call.
        foreach (var version in plan.Versions)
        {
            if (versionRows.GetValueOrDefault(version.Id) is not { } existing)
            {
                preflight.Created.Add((ActivityVersionKind, version.Id));
                continue;
            }

            if (!StringComparer.Ordinal.Equals(existing.DefinitionId, version.DefinitionId))
                throw Collision($"Elsa 3 import identity '{ActivityVersionKind}/{version.Id}' is already bound to different content.", scoped);
        }

        // A new definition is created with its authoring state; an existing one keeps the authoring state it has,
        // which must be this import's.
        foreach (var authoring in plan.Authoring)
        {
            if (authoringRows.GetValueOrDefault(authoring.DefinitionId) is { } existing)
            {
                EnsureAuthoringCompatible(existing, authoring, scoped);
                continue;
            }

            if (!preflight.NewDefinitions.Any(definition => StringComparer.Ordinal.Equals(definition.Id, authoring.DefinitionId)))
                throw Collision($"Elsa 3 activity definition identity '{authoring.DefinitionId}' has no authoring state.", scoped);
        }

        foreach (var version in plan.WorkflowVersions)
        {
            if (workflowVersionRows.GetValueOrDefault(version.Id) is not { } existing)
            {
                preflight.NewWorkflowVersions.Add(version);
                preflight.Created.Add((WorkflowVersionKind, version.Id));
                continue;
            }

            if (!SameWorkflowVersion(existing, version, plan.WorkflowStateJson[version.Id]))
                throw Collision($"Elsa 3 import identity '{WorkflowVersionKind}/{version.Id}' is already bound to different content.", scoped);
        }

        // The Workflows Design stores read through tracking queries. The write phase must start from a
        // clean change tracker so a preflight read can never be saved as a side effect.
        workflows.ChangeTracker.Clear();
        return preflight;
    }

    private async Task WriteAsync(
        EfSharedTransaction shared,
        ImportPlan plan,
        Preflight preflight,
        ReusableActivityImportReceipt? receipt,
        string tenantId,
        string commitAttemptId,
        CancellationToken cancellationToken)
    {
        var import = shared.Context<Elsa3ImportDbContext>();
        var activities = shared.Context<ActivitiesDesignDbContext>();
        var workflows = shared.Context<WorkflowsDesignDbContext>();

        // Import-owned rows go first: the receipt row is the idempotency lock, so a concurrent apply of the
        // same key stops on it before any Design row is touched.
        foreach (var binding in preflight.NewBindings)
            import.DefinitionBindings.Add(Elsa3ImportRecordCodec.ToRecord(binding));
        if (receipt is not null)
            import.Receipts.Add(Elsa3ImportRecordCodec.ToRecord(receipt, commitAttemptId));
        if (import.ChangeTracker.HasChanges())
            await import.SaveChangesAsync(cancellationToken);

        // Workflows Design rows through the lane's own materialization commands and atomic writer.
        var workflowWriter = new EfDesignAtomicWriter(workflows, accessContextAccessor, clock, transactionFactory: shared.BeginOperationAsync);
        var materializeDefinition = new EfMaterializeWorkflowDefinitionCommand(workflows, accessContextAccessor, workflowWriter);
        var materializeVersion = new EfMaterializeWorkflowDefinitionVersionCommand(workflows, accessContextAccessor, workflowWriter, payloadSerializer);
        foreach (var definition in preflight.NewWorkflowDefinitions)
        {
            var row = CloneWorkflowDefinition(definition);
            await materializeDefinition.Execute(OperationKey(commitAttemptId, WorkflowDefinitionKind, definition.Id), row, cancellationToken);
            EnsureWritten(workflows.Entry(row).State == EntityState.Unchanged, WorkflowDefinitionKind, definition.Id);
        }
        foreach (var version in preflight.NewWorkflowVersions)
        {
            await materializeVersion.Execute(OperationKey(commitAttemptId, WorkflowVersionKind, version.Id), CloneWorkflowVersion(version, plan.WorkflowStateJson[version.Id]), cancellationToken);
            EnsureWritten(workflows.ChangeTracker.Entries<WorkflowDefinitionVersion>().Any(entry =>
                entry.State == EntityState.Unchanged && StringComparer.Ordinal.Equals(entry.Entity.Id, version.Id)), WorkflowVersionKind, version.Id);
        }

        // Activities Design rows through the lane's own create command and atomic writer: each new definition with
        // its authoring state and the draft of its first imported version, as Studio creates a definition.
        var activityWriter = new EfDesignAtomicWrite(activities, accessContextAccessor, shared.BeginOperationAsync);
        var projections = new EfActivityManagementProjectionWriter(activities, accessContextAccessor);
        var activityStores = new EfActivityDesignStores(activities, accessContextAccessor, activityWriter, projections);
        foreach (var definition in preflight.NewDefinitions)
        {
            var candidate = plan.ActivityFor(definition.Id);
            var row = CloneActivityDefinition(definition);
            var authoring = CloneAuthoring(candidate.Authoring);
            var draft = ReusableActivityImportDrafts.Draft(candidate.Version, headVersionId: null);
            var layout = ReusableActivityImportDrafts.Layout(candidate.Version);
            await activityStores.ExecuteAsync(new CreateActivityDefinitionRequest(row, authoring, draft, layout), cancellationToken);
            EnsureWritten(
                activities.Entry(row).State == EntityState.Unchanged &&
                activities.Entry(authoring).State == EntityState.Unchanged &&
                activities.Entry(draft).State == EntityState.Unchanged,
                ActivityDefinitionKind,
                definition.Id);
        }
    }

    private static void EnsureAuthoringCompatible(
        ActivityDefinitionAuthoringState current,
        ActivityDefinitionAuthoringState next,
        bool scoped)
    {
        if (!StringComparer.Ordinal.Equals(current.Id, next.Id) ||
            !StringComparer.Ordinal.Equals(current.DefinitionId, next.DefinitionId) ||
            current.ContentAuthority != next.ContentAuthority ||
            !StringComparer.Ordinal.Equals(current.TenantId, next.TenantId))
            throw Collision($"Elsa 3 activity authoring identity '{next.Id}' is already owned by a different resource.", scoped);
    }

    private async Task<ReusableActivityImportCommitResult> ReconcileUncertainCommitAsync(
        ReusableActivityImportMutation mutation,
        bool scoped,
        string commitAttemptId,
        Exception commitFailure,
        CancellationToken cancellationToken)
    {
        // The durable receipt is the only authority on an unknown commit outcome. It is never assumed.
        var stored = scoped ? await ReconcileAsync(mutation, commitFailure) : null;
        if (stored is not null)
            return StringComparer.Ordinal.Equals(stored.CommitAttemptId, commitAttemptId)
                ? new(false, stored.Receipt)
                : new(true, stored.Receipt with { Status = ReusableActivityImportReceiptStatus.AlreadyImported });

        // Nothing durable proves the commit landed. A cancellation that raced the commit is reported as the
        // cancellation it was, never as a persistence failure; an unscoped mutation has no receipt to consult.
        if (scoped && cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(
                "The Elsa 3 import was cancelled before its commit became durable; nothing was written.",
                commitFailure,
                cancellationToken);
        throw Persistence("atomic apply", mutation, commitFailure);
    }

    /// <summary>Re-reads the receipt on a fresh connection after the shared transaction is released.</summary>
    private async Task<StoredReceipt?> ReconcileAsync(ReusableActivityImportMutation mutation, Exception failure)
    {
        for (var read = 1; ; read++)
        {
            try
            {
                importDb.ChangeTracker.Clear();
                return await FindReceiptAsync(importDb, mutation, CancellationToken.None);
            }
            catch (ReusableActivityImportIdempotencyConflictException)
            {
                throw;
            }
            catch (ReusableActivityImportPersistenceException) when (read < ReconciliationReads)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * read), clock);
            }
            catch (ReusableActivityImportPersistenceException exception)
            {
                throw Persistence("reconcile atomic apply", mutation, new AggregateException(failure, exception));
            }
        }
    }

    private static async Task<StoredReceipt?> FindReceiptAsync(
        Elsa3ImportDbContext db,
        ReusableActivityImportMutation mutation,
        CancellationToken cancellationToken)
    {
        var scope = mutation.AccessScope!;
        var idempotencyKey = mutation.IdempotencyKey!;
        var receiptId = ReusableActivityImportIdentity.Receipt(idempotencyKey, scope);
        StoredReceipt? stored;
        try
        {
            stored = await EfReusableActivityImportOperationStore.FindReceiptRecordAsync(db, receiptId, scope, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException("load", $"elsa3ReusableImportReceipt/{receiptId}", exception);
        }

        if (stored is not null && !Matches(stored.Receipt, mutation))
            throw new ReusableActivityImportIdempotencyConflictException(idempotencyKey);
        return stored;
    }

    private string WritableTenant(ReusableActivityImportMutation mutation)
    {
        // The Workflows Design lane writes only inside one explicit tenant scope, so a global import cannot be
        // committed through EF. Refuse before any connection opens rather than half-way through the unit.
        var tenantId = accessContextAccessor.Current.Scope?.Value;
        if (tenantId is not null)
            return tenantId;
        throw new ReusableActivityImportPersistenceException(
            "validate persistence scope",
            mutation.AccessScope is { } scope ? ScopeIdentity(scope) : mutation.PlanId,
            new InvalidOperationException("The EF Core Elsa 3 import writes Workflows Design rows, which require an explicit tenant persistence scope."));
    }

    private ImportPlan CreatePlan(ReusableActivityImportMutation mutation, string tenantId, bool scoped)
    {
        ImportedActivity[] activities;
        (WorkflowDefinition Definition, JsonNode? Material)[] workflowDefinitions;
        (WorkflowDefinitionVersion Version, string StateJson, JsonNode? Material)[] workflowVersions;
        ReusableActivityImportDefinitionBinding[] bindings;
        try
        {
            activities = mutation.Activities.Select(activity => new ImportedActivity(
                    WithTenant(CloneActivityDefinition(activity.Definition), tenantId),
                    WithTenant(activity.Version, tenantId),
                    WithTenant(CloneAuthoring(activity.AuthoringState), tenantId)))
                .ToArray();
            workflowDefinitions = mutation.Workflows
                .Select(workflow => WithTenant(CloneWorkflowDefinition(workflow.Definition), tenantId))
                .Select(definition => (definition, WorkflowDefinitionMaterial(definition)))
                .ToArray();
            workflowVersions = mutation.Workflows.Select(workflow =>
                {
                    var version = WithTenant(CloneWorkflowVersion(workflow.Version, null), tenantId);
                    var stateJson = payloadSerializer.Serialize(version.State);
                    return (version, stateJson, WorkflowVersionMaterial(version, stateJson));
                })
                .ToArray();
            bindings = DefinitionBindings(mutation).Select(binding => binding with { TenantId = binding.TenantId ?? tenantId }).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Persistence("serialize mutation", mutation, exception);
        }

        // Coalescing is outside the serialization guard: one identity bound to two contents is a collision.
        var coalescedVersions = Coalesce(workflowVersions.Select(entry => (entry.Version, entry.Material)), WorkflowVersionKind, scoped);
        return new ImportPlan(
            activities,
            Coalesce(activities.Select(activity => (activity.Definition, ActivityDefinitionMaterial(activity.Definition))), ActivityDefinitionKind, scoped),
            CoalesceVersions(activities.Select(activity => activity.Version), scoped),
            Coalesce(activities.Select(activity => (activity.Authoring, AuthoringMaterial(activity.Authoring))), "activityDefinitionAuthoringState", scoped),
            Coalesce(workflowDefinitions, WorkflowDefinitionKind, scoped),
            coalescedVersions,
            coalescedVersions.ToDictionary(
                version => version.Id,
                version => workflowVersions.First(entry => ReferenceEquals(entry.Version, version)).StateJson,
                StringComparer.Ordinal),
            CoalesceBindings(bindings, scoped));
    }

    private TEntity WithTenant<TEntity>(TEntity entity, string tenantId) where TEntity : Elsa.Primitives.Entities.TenantEntity
    {
        accessContextAccessor.Current.EnsureTenantScope(entity.TenantId);
        entity.TenantId ??= tenantId;
        return entity;
    }

    private ImportedReusableActivityVersion WithTenant(ImportedReusableActivityVersion version, string tenantId)
    {
        accessContextAccessor.Current.EnsureTenantScope(version.TenantId);
        var clone = version with { };
        clone.TenantId = version.TenantId ?? tenantId;
        return clone;
    }

    private static IReadOnlyList<ImportedReusableActivityVersion> CoalesceVersions(
        IEnumerable<ImportedReusableActivityVersion> candidates,
        bool scoped)
    {
        var result = new List<ImportedReusableActivityVersion>();
        foreach (var group in candidates.GroupBy(candidate => candidate.Id, StringComparer.Ordinal))
        {
            var first = group.First();
            if (group.Skip(1).Any(candidate => !JsonNode.DeepEquals(ActivityVersionMaterial(first), ActivityVersionMaterial(candidate))))
                throw Collision($"Elsa 3 import mutation binds '{ActivityVersionKind}/{first.Id}' to multiple contents.", scoped);
            result.Add(first);
        }
        return result.OrderBy(version => version.Id, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<TEntity> Coalesce<TEntity>(
        IEnumerable<(TEntity Entity, JsonNode? Material)> candidates,
        string kind,
        bool scoped)
        where TEntity : Elsa.Primitives.Entities.Entity
    {
        var result = new List<TEntity>();
        foreach (var group in candidates.GroupBy(candidate => candidate.Entity.Id, StringComparer.Ordinal))
        {
            var first = group.First();
            if (group.Skip(1).Any(candidate => !JsonNode.DeepEquals(first.Material, candidate.Material)))
                throw Collision($"Elsa 3 import mutation binds '{kind}/{first.Entity.Id}' to multiple contents.", scoped);
            result.Add(first.Entity);
        }
        return result.OrderBy(entity => entity.Id, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<ReusableActivityImportDefinitionBinding> CoalesceBindings(
        IEnumerable<ReusableActivityImportDefinitionBinding> candidates,
        bool scoped)
    {
        var result = new List<ReusableActivityImportDefinitionBinding>();
        foreach (var group in candidates.GroupBy(candidate => candidate.Id, StringComparer.Ordinal))
        {
            var first = group.First();
            if (group.Skip(1).Any(candidate => candidate != first))
                throw Collision($"Elsa 3 import mutation binds '{DefinitionBindingKind}/{first.Id}' to multiple contents.", scoped);
            result.Add(first);
        }
        return result.OrderBy(binding => binding.Id, StringComparer.Ordinal).ToArray();
    }

    private static JsonNode? ActivityDefinitionMaterial(ActivityDefinition definition) => JsonSerializer.SerializeToNode(new
    {
        definition.Id, definition.TenantId, definition.ActivityTypeKey, definition.Category, definition.DisplayName,
        definition.Description, definition.CreatedAt, definition.LastModifiedAt
    }, Json);

    private static JsonNode? ActivityVersionMaterial(ImportedReusableActivityVersion version) => JsonSerializer.SerializeToNode(new
    {
        version.Id, version.TenantId, version.DefinitionId, version.Version, version.SourceVersionId, version.Contract,
        GraphManifest = PayloadNode(version.GraphManifest), version.DependsOn, version.CreatedAt
    }, Json);

    private static JsonNode? AuthoringMaterial(ActivityDefinitionAuthoringState authoring) => JsonSerializer.SerializeToNode(new
    {
        authoring.Id, authoring.TenantId, authoring.DefinitionId, authoring.ContentAuthority, authoring.ForkedFrom,
        authoring.HeadVersionId, authoring.RecommendedVersionId, authoring.CreatedAt, authoring.LastModifiedAt
    }, Json);

    private static JsonNode? WorkflowDefinitionMaterial(WorkflowDefinition definition) => JsonSerializer.SerializeToNode(new
    {
        definition.Id, definition.TenantId, definition.Name, definition.Description, definition.DeletedAt,
        definition.DeletedReason, definition.IsSourceOwned, definition.CreatedAt, definition.LastModifiedAt
    }, Json);

    private static JsonNode? WorkflowVersionMaterial(WorkflowDefinitionVersion version, string stateJson) => JsonSerializer.SerializeToNode(new
    {
        version.Id, version.TenantId, version.DefinitionId, version.Version, version.SourceDraftId, version.SourceCreatedAt,
        version.CreatedAt, version.LastModifiedAt, State = JsonNode.Parse(stateJson)
    }, Json);

    private static JsonNode? PayloadNode(JsonElement payload) =>
        payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : JsonNode.Parse(payload.GetRawText());

    private static void EnsureReusableDefinitionCompatible(ActivityDefinition current, ActivityDefinition next, bool scoped)
    {
        if (!StringComparer.Ordinal.Equals(current.Id, next.Id) ||
            !StringComparer.Ordinal.Equals(current.ActivityTypeKey, next.ActivityTypeKey) ||
            !StringComparer.Ordinal.Equals(current.TenantId, next.TenantId) ||
            !StringComparer.Ordinal.Equals(current.Category, next.Category) ||
            !StringComparer.Ordinal.Equals(current.DisplayName, next.DisplayName) ||
            !StringComparer.Ordinal.Equals(current.Description, next.Description) ||
            !SameInstant(current.CreatedAt, next.CreatedAt))
            throw Collision($"Elsa 3 activity definition identity '{next.Id}' is already owned by a different resource.", scoped);
    }

    private static void EnsureWorkflowDefinitionCompatible(WorkflowDefinition current, WorkflowDefinition next, bool scoped)
    {
        if (!StringComparer.Ordinal.Equals(current.Id, next.Id) ||
            !StringComparer.Ordinal.Equals(current.TenantId, next.TenantId) ||
            !StringComparer.Ordinal.Equals(current.Name, next.Name) ||
            !StringComparer.Ordinal.Equals(current.Description, next.Description) ||
            !SameInstant(current.CreatedAt, next.CreatedAt) ||
            !SameInstant(current.DeletedAt, next.DeletedAt) ||
            !StringComparer.Ordinal.Equals(current.DeletedReason, next.DeletedReason))
            throw Collision($"Elsa 3 workflow definition identity '{next.Id}' is already owned by a different resource.", scoped);
    }

    /// <summary>
    /// An existing Design definition is reused only when it carries this import's provenance binding, so a
    /// binding never attaches to an unrelated definition that merely has the same identity and presentation.
    /// </summary>
    private static void EnsureDefinitionBinding(
        string targetDocumentKind,
        string targetDefinitionId,
        ImportPlan plan,
        IReadOnlySet<string> existingBindings,
        bool scoped)
    {
        var candidate = plan.Bindings.FirstOrDefault(binding =>
                            StringComparer.Ordinal.Equals(binding.TargetDocumentKind, targetDocumentKind) &&
                            StringComparer.Ordinal.Equals(binding.TargetDefinitionId, targetDefinitionId))
                        ?? throw new ReusableActivityImportPersistenceException(
                            "preflight definition binding",
                            $"{targetDocumentKind}/{targetDefinitionId}",
                            new InvalidOperationException("The import mutation does not contain a definition provenance binding."));
        if (!existingBindings.Contains(candidate.Id))
            throw Collision($"Elsa 3 {targetDocumentKind} identity '{targetDefinitionId}' has no matching import provenance.", scoped);
    }

    private static bool SameBinding(ReusableActivityImportDefinitionBinding current, ReusableActivityImportDefinitionBinding next) =>
        StringComparer.Ordinal.Equals(current.Id, next.Id) &&
        StringComparer.Ordinal.Equals(current.TargetDocumentKind, next.TargetDocumentKind) &&
        StringComparer.Ordinal.Equals(current.TargetDefinitionId, next.TargetDefinitionId) &&
        StringComparer.Ordinal.Equals(current.SourceKind, next.SourceKind) &&
        StringComparer.Ordinal.Equals(current.SourceDefinitionId, next.SourceDefinitionId) &&
        StringComparer.Ordinal.Equals(current.TenantId, next.TenantId) &&
        current.CreatedAt == next.CreatedAt;

    private static bool SameWorkflowVersion(WorkflowDefinitionVersion current, WorkflowDefinitionVersion next, string nextStateJson) =>
        StringComparer.Ordinal.Equals(current.Id, next.Id) &&
        StringComparer.Ordinal.Equals(current.TenantId, next.TenantId) &&
        StringComparer.Ordinal.Equals(current.DefinitionId, next.DefinitionId) &&
        StringComparer.Ordinal.Equals(current.Version, next.Version) &&
        StringComparer.Ordinal.Equals(current.SourceDraftId, next.SourceDraftId) &&
        SameInstant(current.SourceCreatedAt, next.SourceCreatedAt) &&
        SameInstant(current.CreatedAt, next.CreatedAt) &&
        JsonEquals(current.StateSource, nextStateJson);

    // Relational providers keep timestamps to at least microsecond precision (MySQL datetime(6),
    // PostgreSQL timestamptz), so identity comparisons of stored instants are made at that precision.
    private static bool SameInstant(DateTimeOffset left, DateTimeOffset right) => left.UtcTicks / 10 == right.UtcTicks / 10;

    private static bool SameInstant(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null || right is null ? left is null && right is null : SameInstant(left.Value, right.Value);

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private static bool JsonEquals(string? left, string? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));
        }
        catch (JsonException exception)
        {
            throw new ReusableActivityImportPersistenceException("compare document content", "JSON", exception);
        }
    }

    private static Task<Dictionary<string, ActivityDefinition>> LoadActivityDefinitionsAsync(ActivitiesDesignDbContext db, IEnumerable<string> ids, string tenantId, CancellationToken cancellationToken) =>
        FindScopedManyAsync(db.ActivityDefinitions.AsNoTracking(), "Id", ids, row => row.Id, tenantId, ActivityDefinitionKind, cancellationToken);

    private static Task<Dictionary<string, ActivityDefinitionVersion>> LoadActivityVersionsAsync(ActivitiesDesignDbContext db, IEnumerable<string> ids, string tenantId, CancellationToken cancellationToken) =>
        FindScopedManyAsync(db.ActivityDefinitionVersions.AsNoTracking(), "Id", ids, row => row.Id, tenantId, ActivityVersionKind, cancellationToken);

    private static Task<Dictionary<string, ActivityDefinitionAuthoringState>> LoadAuthoringStatesAsync(ActivitiesDesignDbContext db, IEnumerable<string> definitionIds, string tenantId, CancellationToken cancellationToken) =>
        FindScopedManyAsync(db.ActivityDefinitionAuthoringStates.AsNoTracking(), "DefinitionId", definitionIds, row => row.DefinitionId, tenantId, "activityDefinitionAuthoringState", cancellationToken);

    /// <summary>
    /// The activity type keys already owned in this tenant among the ones the plan wants. The single-item form asked
    /// the provider one <c>Any</c> per new definition; the set answers the same question for all of them at once, and
    /// is still consulted only for definitions that turned out to be new.
    /// </summary>
    private static async Task<HashSet<string>> LoadTakenActivityTypeKeysAsync(
        ActivitiesDesignDbContext db,
        IEnumerable<string> activityTypeKeys,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var wanted = activityTypeKeys.Distinct(StringComparer.Ordinal).ToArray();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        if (wanted.Length == 0)
            return taken;

        var tenantKey = ActivitiesDesignDbContext.NormalizeTenantKey(tenantId);
        foreach (var chunk in wanted.Chunk(ProviderSafeIdBatchSize))
        {
            var rows = await ReadAsync(ActivityDefinitionKind, () => db.ActivityDefinitions.AsNoTracking()
                .Where(row => EF.Property<string>(row, "TenantScopeKey") == tenantKey && chunk.Contains(row.ActivityTypeKey))
                .Select(row => row.ActivityTypeKey)
                .ToListAsync(cancellationToken));
            foreach (var row in rows)
                taken.Add(row);
        }

        return taken;
    }

    /// <summary>
    /// Reads the import ledger's definition bindings by hashed identity inside the tenant partition. One hash
    /// resolving to more than one row is refused, and each returned row is decoded under the binding identity that
    /// asked for it, so a drifted row fails closed instead of being reported as a different binding.
    /// </summary>
    private static async Task<Dictionary<string, ReusableActivityImportDefinitionBinding>> LoadBindingsAsync(
        Elsa3ImportDbContext db,
        IEnumerable<string> bindingIds,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var wanted = bindingIds.Distinct(StringComparer.Ordinal).ToArray();
        var found = new Dictionary<string, ReusableActivityImportDefinitionBinding>(StringComparer.Ordinal);
        if (wanted.Length == 0)
            return found;

        var tenantKey = Elsa3ImportRecordCodec.TenantKey(tenantId);
        var byHash = wanted.ToLookup(Elsa3ImportRecordCodec.Hash, StringComparer.Ordinal);
        foreach (var chunk in byHash.Select(group => group.Key).Chunk(ProviderSafeIdBatchSize))
        {
            var rows = await ReadAsync(DefinitionBindingKind, () => db.DefinitionBindings.AsNoTracking()
                .Where(row => row.TenantKey == tenantKey && chunk.Contains(row.BindingIdHash))
                .ToListAsync(cancellationToken));
            foreach (var group in rows.GroupBy(row => row.BindingIdHash, StringComparer.Ordinal))
            {
                var candidates = byHash[group.Key].ToArray();
                Read($"{DefinitionBindingKind}/{candidates[0]}", () =>
                {
                    if (group.Count() > 1)
                        throw new InvalidDataException("The Elsa 3 import definition binding identity resolves to more than one row.");
                    foreach (var candidate in candidates)
                        found[candidate] = Elsa3ImportRecordCodec.ReadBinding(group.Single(), candidate, tenantId);
                    return candidates.Length;
                });
            }
        }

        return found;
    }

    private static async Task<Dictionary<string, WorkflowDefinition>> LoadWorkflowDefinitionsAsync(
        EfWorkflowDefinitionStore store,
        IEnumerable<string> ids,
        CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct(StringComparer.Ordinal).ToArray();
        var found = new Dictionary<string, WorkflowDefinition>(StringComparer.Ordinal);
        foreach (var chunk in wanted.Chunk(ProviderSafeIdBatchSize))
        {
            var rows = await ReadAsync(WorkflowDefinitionKind, () => store.ListAsync(new() { Ids = chunk }, cancellationToken));
            foreach (var row in rows)
                found[row.Id] = row;
        }

        return found;
    }

    private static async Task<Dictionary<string, WorkflowDefinitionVersion>> LoadWorkflowVersionsAsync(
        EfWorkflowDefinitionVersionStore store,
        IEnumerable<string> ids,
        CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct(StringComparer.Ordinal).ToArray();
        var rows = await ReadAsync(WorkflowVersionKind, () => store.FindByIdsAsync(wanted, cancellationToken));
        return rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// Identities per query. Well under every supported provider's host-parameter ceiling, so a plan larger than one
    /// batch simply issues another query rather than failing on a provider limit.
    /// </summary>
    private const int ProviderSafeIdBatchSize = 200;

    /// <summary>
    /// Reads many Activities Design rows in one query per chunk, the batched sibling of <see cref="FindScopedAsync{TRow}"/>.
    /// Every check the single read makes is made here per row: the lookup is by hashed identity inside the tenant
    /// partition, one hash resolving to more than one row is refused, and the exact residual and tenant comparison
    /// still decides whether the row is the one that was asked for. An identity missing from the result therefore
    /// means exactly what <c>null</c> means for the single read, and a drifted row still fails closed under its own
    /// identity rather than being re-inserted or silently skipped.
    /// </summary>
    private static async Task<Dictionary<string, TRow>> FindScopedManyAsync<TRow>(
        IQueryable<TRow> rows,
        string identityProperty,
        IEnumerable<string> identities,
        Func<TRow, string> residual,
        string tenantId,
        string kind,
        CancellationToken cancellationToken)
        where TRow : Elsa.Primitives.Entities.TenantEntity
    {
        var wanted = identities.Distinct(StringComparer.Ordinal).ToArray();
        var found = new Dictionary<string, TRow>(StringComparer.Ordinal);
        if (wanted.Length == 0)
            return found;

        var tenantKey = ActivitiesDesignDbContext.NormalizeTenantKey(tenantId);
        var hashProperty = identityProperty + "IdentityHash";
        var byHash = wanted.ToLookup(ActivitiesDesignDbContext.ComputeIdentityHash, StringComparer.Ordinal);
        foreach (var chunk in byHash.Select(group => group.Key).Chunk(ProviderSafeIdBatchSize))
        {
            // The batch query is a whole-collection read, so a provider failure here names the collection rather
            // than an item. Everything that can name one item is checked per row below, under that item's identity.
            var matches = await ReadAsync(kind, () => rows
                .Where(row => EF.Property<string>(row, "TenantScopeKey") == tenantKey &&
                              chunk.Contains(EF.Property<string>(row, hashProperty)))
                .Select(row => new HashedRow<TRow>(row, EF.Property<string>(row, hashProperty)))
                .ToListAsync(cancellationToken));

            foreach (var group in matches.GroupBy(match => match.Hash, StringComparer.Ordinal))
            {
                var candidates = byHash[group.Key].ToArray();
                Read($"{kind}/{candidates[0]}", () =>
                {
                    if (group.Count() > 1)
                        throw new InvalidDataException($"The {kind} identity resolves to more than one row.");
                    var row = group.Single().Row;
                    var identity = Array.Find(candidates, candidate => StringComparer.Ordinal.Equals(residual(row), candidate));
                    if (identity is null || !StringComparer.Ordinal.Equals(row.TenantId, tenantId))
                        throw new InvalidDataException($"The {kind} row does not match its hashed identity and tenant partition.");
                    found[identity] = row;
                    return identity;
                });
            }
        }

        return found;
    }

    private sealed record HashedRow<TRow>(TRow Row, string Hash);

    /// <summary>
    /// Reads one Activities Design row in exactly the tenant partition by its hashed identity. The lookup ends
    /// with an exact residual comparison, so a hash collision or a corrupted row can never alias another resource.
    /// </summary>
    private static async Task<TRow?> FindScopedAsync<TRow>(
        IQueryable<TRow> rows,
        string identityProperty,
        string identity,
        Func<TRow, string> residual,
        string tenantId,
        string kind,
        CancellationToken cancellationToken)
        where TRow : Elsa.Primitives.Entities.TenantEntity
    {
        var tenantKey = ActivitiesDesignDbContext.NormalizeTenantKey(tenantId);
        var identityHash = ActivitiesDesignDbContext.ComputeIdentityHash(identity);
        var hashProperty = identityProperty + "IdentityHash";
        var matches = await ActivitiesDesignDbContext.InPhysicalIdentityOrder(rows
                .Where(row => EF.Property<string>(row, "TenantScopeKey") == tenantKey && EF.Property<string>(row, hashProperty) == identityHash))
            .Take(2)
            .ToListAsync(cancellationToken);
        if (matches.Count > 1)
            throw new InvalidDataException($"The {kind} identity resolves to more than one row.");
        var row = matches.SingleOrDefault();
        if (row is not null && (!StringComparer.Ordinal.Equals(residual(row), identity) || !StringComparer.Ordinal.Equals(row.TenantId, tenantId)))
            throw new InvalidDataException($"The {kind} row does not match its hashed identity and tenant partition.");
        return row;
    }

    /// <summary>The synchronous sibling of <see cref="ReadAsync{T}"/>, for a check made on an already-loaded row.</summary>
    private static T Read<T>(string identity, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is not (ReusableActivityImportCollisionException or ReusableActivityImportPersistenceException))
        {
            throw new ReusableActivityImportPersistenceException("load", identity, exception);
        }
    }

    private static async Task<T> ReadAsync<T>(string identity, Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not (ReusableActivityImportCollisionException or ReusableActivityImportPersistenceException))
        {
            throw new ReusableActivityImportPersistenceException("load", identity, exception);
        }
    }

    private static void EnsureWritten(bool written, string kind, string id)
    {
        if (!written)
            throw new InvalidOperationException($"The Design write path reported success for '{kind}/{id}' without writing it in the shared transaction.");
    }

    /// <summary>
    /// A write that lost a race to a concurrent change. The unit is retried from durable state; nothing about
    /// the failed attempt survives, because the shared transaction rolled it back.
    /// </summary>
    private static bool IsWriteConflict(Exception exception)
    {
        if (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) ||
            EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            return true;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException or
                EfSharedTransactionRollbackOnlyException or
                DesignPersistenceOperationConflictException or
                ActivityDefinitionVersionConflictException or
                WorkflowDefinitionVersionConflictException)
                return true;
        }
        return false;
    }

    private static DesignOperationKey OperationKey(string commitAttemptId, string kind, string id) =>
        new(ReusableActivityImportIdentity.Create("design-operation", commitAttemptId, kind, id));

    private static InvalidOperationException Collision(string message, bool scoped) =>
        scoped ? new ReusableActivityImportCollisionException(message) : new InvalidOperationException(message);

    private static ReusableActivityImportPersistenceException Persistence(string operation, ReusableActivityImportMutation mutation, Exception exception) =>
        exception as ReusableActivityImportPersistenceException ??
        new ReusableActivityImportPersistenceException(operation, mutation.IdempotencyKey ?? mutation.PlanId, exception);

    private static string ScopeIdentity(ReusableActivityImportAccessScope accessScope) =>
        ReusableActivityImportIdentity.Create("scope", accessScope.TenantScope, accessScope.UserId);

    private static ActivityDefinition CloneActivityDefinition(ActivityDefinition source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        ActivityTypeKey = source.ActivityTypeKey,
        Category = source.Category,
        DisplayName = source.DisplayName,
        Description = source.Description,
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static ActivityDefinitionAuthoringState CloneAuthoring(ActivityDefinitionAuthoringState source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        DefinitionId = source.DefinitionId,
        ContentAuthority = source.ContentAuthority,
        ForkedFrom = source.ForkedFrom,
        HeadVersionId = source.HeadVersionId,
        RecommendedVersionId = source.RecommendedVersionId,
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static WorkflowDefinition CloneWorkflowDefinition(WorkflowDefinition source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        Name = source.Name,
        Description = source.Description,
        DeletedAt = source.DeletedAt,
        DeletedReason = source.DeletedReason,
        IsSourceOwned = source.IsSourceOwned,
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static WorkflowDefinitionVersion CloneWorkflowVersion(WorkflowDefinitionVersion source, string? stateJson) =>
        new(source.DefinitionId, source.Version, stateJson, source.SourceCreatedAt)
        {
            Id = source.Id,
            TenantId = source.TenantId,
            State = source.State,
            SourceDraftId = source.SourceDraftId,
            CreatedAt = source.CreatedAt,
            LastModifiedAt = source.LastModifiedAt
        };

    private sealed record AttemptOutcome(ReusableActivityImportCommitResult? Result, Exception? Failure);

    private sealed record ImportedActivity(
        ActivityDefinition Definition,
        ImportedReusableActivityVersion Version,
        ActivityDefinitionAuthoringState Authoring);

    private sealed record ImportPlan(
        IReadOnlyList<ImportedActivity> Activities,
        IReadOnlyList<ActivityDefinition> Definitions,
        IReadOnlyList<ImportedReusableActivityVersion> Versions,
        IReadOnlyList<ActivityDefinitionAuthoringState> Authoring,
        IReadOnlyList<WorkflowDefinition> WorkflowDefinitions,
        IReadOnlyList<WorkflowDefinitionVersion> WorkflowVersions,
        IReadOnlyDictionary<string, string> WorkflowStateJson,
        IReadOnlyList<ReusableActivityImportDefinitionBinding> Bindings)
    {
        /// <summary>The definition's lowest selected version: activities are in publish order, ascending per definition.</summary>
        public ImportedActivity ActivityFor(string definitionId) =>
            Activities.First(activity => StringComparer.Ordinal.Equals(activity.Definition.Id, definitionId));
    }

    private sealed class Preflight
    {
        public HashSet<(string Kind, string Id)> Created { get; } = [];
        public List<ReusableActivityImportDefinitionBinding> NewBindings { get; } = [];
        public List<ActivityDefinition> NewDefinitions { get; } = [];
        public Dictionary<string, ActivityDefinition> PersistedDefinitions { get; } = new(StringComparer.Ordinal);
        public List<WorkflowDefinition> NewWorkflowDefinitions { get; } = [];
        public List<WorkflowDefinitionVersion> NewWorkflowVersions { get; } = [];

        public bool HasWrites =>
            NewBindings.Count > 0 || NewDefinitions.Count > 0 || NewWorkflowDefinitions.Count > 0 || NewWorkflowVersions.Count > 0;
    }

}
