using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Exceptions;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
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
/// Every Design identity is preflighted inside the transaction before the first write. Identical resources
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
    private const int MaximumAttempts = 3;
    private const int ReconciliationReads = 3;
    private const string DefinitionBindingKind = "elsa3ReusableImportDefinitionBinding";
    private const string AuthoringOperationKind = "elsa3.import.activity-authoring.v1";
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

        for (var attempt = 1; ; attempt++)
        {
            var commitAttemptId = Guid.NewGuid().ToString("N");
            var outcome = await AttemptAsync(mutation, plan, tenantId, scoped, commitAttemptId, cancellationToken);
            if (outcome.Result is not null)
                return outcome.Result;

            var failure = outcome.Failure!;
            if (failure is EfCommitOutcomeUnknownException)
                return await ReconcileUncertainCommitAsync(mutation, scoped, commitAttemptId, failure, cancellationToken);
            if (!IsWriteConflict(failure))
                throw Persistence("atomic apply", mutation, failure);
            if (attempt < MaximumAttempts)
                continue;

            if (scoped)
            {
                var reconciled = await ReconcileAsync(mutation, failure);
                if (reconciled is not null)
                    return new(true, reconciled.Receipt with { Status = ReusableActivityImportReceiptStatus.AlreadyImported });
                throw new ReusableActivityImportCollisionException(
                    "The Elsa 3 import identities changed before the atomic commit; no partial import was written.",
                    failure);
            }

            throw Persistence("atomic apply", mutation, failure);
        }
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

        var existingBindings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in plan.Bindings)
        {
            var existing = await ReadAsync($"{DefinitionBindingKind}/{binding.Id}", () => LoadBindingAsync(import, binding.Id, tenantId, cancellationToken));
            if (existing is null)
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
            var existing = await ReadAsync($"{ActivityDefinitionKind}/{definition.Id}", () => LoadActivityDefinitionAsync(activities, definition.Id, tenantId, cancellationToken));
            if (existing is null)
            {
                if (await ReadAsync($"{ActivityDefinitionKind}/{definition.Id}", () => ActivityTypeKeyTakenAsync(activities, definition, tenantId, cancellationToken)))
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
            var existing = await ReadAsync($"{WorkflowDefinitionKind}/{definition.Id}", () => workflowDefinitions.FindByIdAsync(definition.Id, cancellationToken));
            if (existing is null)
            {
                preflight.NewWorkflowDefinitions.Add(definition);
                preflight.Created.Add((WorkflowDefinitionKind, definition.Id));
                continue;
            }

            EnsureWorkflowDefinitionCompatible(existing, definition, scoped);
            EnsureDefinitionBinding(WorkflowDefinitionKind, definition.Id, plan, existingBindings, scoped);
        }

        foreach (var version in plan.Versions)
        {
            var existing = await ReadAsync($"{ActivityVersionKind}/{version.Id}", () => LoadActivityVersionAsync(activities, version.Id, tenantId, cancellationToken));
            if (existing is null)
            {
                preflight.NewVersions.Add(version);
                preflight.Created.Add((ActivityVersionKind, version.Id));
                continue;
            }

            if (!SameActivityVersion(existing, version))
                throw Collision($"Elsa 3 import identity '{ActivityVersionKind}/{version.Id}' is already bound to different content.", scoped);
        }

        // A new definition is created together with its first new version; versions that already exist without
        // the definition that owns them are orphans, and adopting them would bind provenance to unowned rows.
        foreach (var definition in preflight.NewDefinitions)
        {
            if (!preflight.NewVersions.Any(version => StringComparer.Ordinal.Equals(version.DefinitionId, definition.Id)))
                throw Collision($"Elsa 3 activity definition identity '{definition.Id}' has immutable versions without the definition that owns them.", scoped);
        }

        foreach (var authoring in plan.Authoring)
        {
            var existing = await ReadAsync($"activityDefinitionAuthoringState/{authoring.Id}", () => LoadAuthoringAsync(activities, authoring.DefinitionId, tenantId, cancellationToken));
            if (existing is null)
            {
                preflight.NewAuthoring.Add(authoring);
                continue;
            }

            var update = await ResolveAuthoringUpdateAsync(activities, existing, authoring, plan, tenantId, cancellationToken);
            if (update is not null)
                preflight.AuthoringUpdates.Add(update);
        }

        foreach (var version in plan.WorkflowVersions)
        {
            var existing = await ReadAsync($"{WorkflowVersionKind}/{version.Id}", () => workflowVersions.FindByIdAsync(version.Id, cancellationToken));
            if (existing is null)
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

        // Activities Design rows through the lane's own add commands and atomic writer.
        var activityWriter = new EfDesignAtomicWrite(activities, accessContextAccessor, shared.BeginOperationAsync);
        var projections = new EfActivityManagementProjectionWriter(activities, accessContextAccessor);
        var activityStores = new EfActivityDesignStores(activities, accessContextAccessor, activityWriter, projections);
        var createdDefinitions = new Dictionary<string, ActivityDefinition>(StringComparer.Ordinal);
        var firstVersions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in preflight.NewDefinitions)
        {
            var first = preflight.NewVersions
                .Where(version => StringComparer.Ordinal.Equals(version.DefinitionId, definition.Id))
                .OrderBy(version => version.SemVerSortKey, StringComparer.Ordinal)
                .ThenBy(version => version.Id, StringComparer.Ordinal)
                .First();
            var row = CloneActivityDefinition(definition);
            var version = CloneActivityVersion(first);
            await activityStores.Execute(OperationKey(commitAttemptId, ActivityDefinitionKind, definition.Id), row, version, cancellationToken);
            EnsureWritten(activities.Entry(row).State == EntityState.Unchanged && activities.Entry(version).State == EntityState.Unchanged, ActivityDefinitionKind, definition.Id);
            createdDefinitions[definition.Id] = row;
            firstVersions.Add(first.Id);
        }
        foreach (var version in preflight.NewVersions.Where(version => !firstVersions.Contains(version.Id)))
        {
            var row = CloneActivityVersion(version);
            await activityStores.Execute(OperationKey(commitAttemptId, ActivityVersionKind, version.Id), row, cancellationToken);
            EnsureWritten(activities.Entry(row).State == EntityState.Unchanged, ActivityVersionKind, version.Id);
        }

        if (preflight.NewAuthoring.Count > 0 || preflight.AuthoringUpdates.Count > 0)
            await WriteAuthoringAndProjectionAsync(activities, activityWriter, projections, plan, preflight, createdDefinitions, tenantId, commitAttemptId, cancellationToken);
    }

    /// <summary>
    /// Authoring rows and the management projection checkpoint, as one Activities Design atomic operation.
    /// The projection covers newly created definitions and definitions whose head advanced, as in Groundwork.
    /// </summary>
    private async Task WriteAuthoringAndProjectionAsync(
        ActivitiesDesignDbContext activities,
        EfDesignAtomicWrite activityWriter,
        EfActivityManagementProjectionWriter projections,
        ImportPlan plan,
        Preflight preflight,
        IReadOnlyDictionary<string, ActivityDefinition> createdDefinitions,
        string tenantId,
        string commitAttemptId,
        CancellationToken cancellationToken)
    {
        var projectionDefinitionIds = preflight.NewDefinitions.Select(definition => definition.Id)
            .Concat(preflight.AuthoringUpdates.Select(update => update.DefinitionId))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var material = new
        {
            Created = preflight.NewAuthoring.Select(authoring => new { authoring.Id, authoring.DefinitionId, authoring.HeadVersionId }).ToArray(),
            Advanced = preflight.AuthoringUpdates.Select(update => new { update.Id, update.DefinitionId, update.ExpectedHeadVersionId, update.HeadVersionId }).ToArray(),
            Projected = projectionDefinitionIds
        };
        var result = await EfDesignAtomicCommand.ExecuteAsync(
            activityWriter,
            OperationKey(commitAttemptId, "activityDefinitionAuthoringState", "*"),
            AuthoringOperationKind,
            material,
            ["activityDefinitionAuthoringState", "activityDefinitionManagementProjection"],
            async (context, token) =>
            {
                var authoringByDefinition = new Dictionary<string, ActivityDefinitionAuthoringState>(StringComparer.Ordinal);
                foreach (var authoring in preflight.NewAuthoring)
                {
                    var row = CloneAuthoring(authoring);
                    context.Db.ActivityDefinitionAuthoringStates.Add(row);
                    authoringByDefinition[row.DefinitionId] = row;
                }

                foreach (var update in preflight.AuthoringUpdates)
                {
                    var row = await LoadTrackedAuthoringAsync(context.Db, update.DefinitionId, tenantId, token);
                    if (row is null ||
                        !StringComparer.Ordinal.Equals(row.Id, update.Id) ||
                        !StringComparer.Ordinal.Equals(row.HeadVersionId, update.ExpectedHeadVersionId))
                        throw new DbUpdateConcurrencyException($"Activity authoring '{update.Id}' changed after the import preflight.");
                    row.HeadVersionId = update.HeadVersionId;
                    row.LastModifiedAt = update.LastModifiedAt;
                    authoringByDefinition[row.DefinitionId] = row;
                }

                if (projectionDefinitionIds.Length == 0)
                {
                    await context.SaveChangesAsync(token);
                    return new AuthoringResult(0);
                }

                var changes = projectionDefinitionIds.Select(definitionId =>
                {
                    var candidate = plan.ActivityFor(definitionId);
                    var definition = preflight.PersistedDefinitions.GetValueOrDefault(definitionId)
                                     ?? createdDefinitions.GetValueOrDefault(definitionId)
                                     ?? throw new InvalidOperationException($"Activity definition '{definitionId}' was not written before its projection.");
                    var authoring = authoringByDefinition.GetValueOrDefault(definitionId) ?? CloneAuthoring(candidate.Authoring);
                    return (Change: new EfActivityManagementDefinitionChange(definition, authoring), ChangedAt: Max(candidate.Authoring.LastModifiedAt, (preflight.PersistedDefinitions.GetValueOrDefault(definitionId) ?? candidate.Definition).LastModifiedAt));
                }).ToArray();
                var sequence = await projections.WriteInCurrentTransactionAsync(
                    new EfActivityManagementProjectionMutation(changes.Max(change => change.ChangedAt), changes.Select(change => change.Change).ToArray(), [], []),
                    token);
                return new AuthoringResult(sequence);
            },
            cancellationToken: cancellationToken,
            tenantId: tenantId);
        // A fresh operation key per attempt means the only legitimate outcome is a commit inside this
        // transaction; a replay here would mean the authoring rows were never written.
        if (result.Status != EfDesignAtomicWriteStatus.Committed)
            throw new InvalidOperationException($"The Elsa 3 import authoring operation reported {result.Status} instead of a commit.");
    }

    private async Task<AuthoringUpdate?> ResolveAuthoringUpdateAsync(
        ActivitiesDesignDbContext activities,
        ActivityDefinitionAuthoringState current,
        ActivityDefinitionAuthoringState next,
        ImportPlan plan,
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (!StringComparer.Ordinal.Equals(current.Id, next.Id) ||
            !StringComparer.Ordinal.Equals(current.DefinitionId, next.DefinitionId) ||
            current.ContentAuthority != next.ContentAuthority ||
            !StringComparer.Ordinal.Equals(current.TenantId, next.TenantId))
            throw new ReusableActivityImportCollisionException(
                $"Elsa 3 activity authoring identity '{next.Id}' is already owned by a different resource.");

        if (next.HeadVersionId is null || StringComparer.Ordinal.Equals(current.HeadVersionId, next.HeadVersionId))
            return null;
        if (current.HeadVersionId is not null)
        {
            var currentHead = await ReadAsync($"{ActivityVersionKind}/{current.HeadVersionId}", () => LoadActivityVersionAsync(activities, current.HeadVersionId, tenantId, cancellationToken))
                              ?? throw new ReusableActivityImportCollisionException(
                                  $"Elsa 3 activity authoring identity '{next.Id}' refers to missing head version '{current.HeadVersionId}'.");
            var nextHead = await ReadAsync($"{ActivityVersionKind}/{next.HeadVersionId}", () => LoadActivityVersionAsync(activities, next.HeadVersionId, tenantId, cancellationToken))
                           ?? plan.Versions.FirstOrDefault(version => StringComparer.Ordinal.Equals(version.Id, next.HeadVersionId))
                           ?? throw new ReusableActivityImportCollisionException(
                               $"Elsa 3 activity authoring identity '{next.Id}' refers to a head version that is not available for comparison.");
            if (StringComparer.Ordinal.Compare(currentHead.SemVerSortKey, nextHead.SemVerSortKey) >= 0)
                return null;
        }

        return new AuthoringUpdate(
            current.Id,
            current.DefinitionId,
            current.HeadVersionId,
            next.HeadVersionId,
            next.LastModifiedAt > current.LastModifiedAt ? next.LastModifiedAt : current.LastModifiedAt);
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
                    WithTenant(CloneActivityVersion(activity.Version), tenantId),
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
            Coalesce(activities.Select(activity => (activity.Version, ActivityVersionMaterial(activity.Version))), ActivityVersionKind, scoped),
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

    private static JsonNode? ActivityVersionMaterial(ActivityDefinitionVersion version) => JsonSerializer.SerializeToNode(new
    {
        version.Id, version.TenantId, version.DefinitionId, version.Version, version.ProviderKey, version.ProviderSchemaVersion,
        version.ConsumerKey, version.ConsumerSchemaVersion, DescriptorPayload = PayloadNode(version.DescriptorPayload),
        version.Inputs, version.Outputs, version.DesignFacets, version.ExecutionType, version.SourceKind, version.SourceId,
        version.Hash, version.CreatedAt, version.LastModifiedAt
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

    private static bool SameActivityVersion(ActivityDefinitionVersion current, ActivityDefinitionVersion next) =>
        StringComparer.Ordinal.Equals(current.Id, next.Id) &&
        StringComparer.Ordinal.Equals(current.TenantId, next.TenantId) &&
        StringComparer.Ordinal.Equals(current.DefinitionId, next.DefinitionId) &&
        StringComparer.Ordinal.Equals(current.Version, next.Version) &&
        StringComparer.Ordinal.Equals(current.SemVerSortKey, next.SemVerSortKey) &&
        StringComparer.Ordinal.Equals(current.ProviderKey, next.ProviderKey) &&
        StringComparer.Ordinal.Equals(current.ProviderSchemaVersion, next.ProviderSchemaVersion) &&
        StringComparer.Ordinal.Equals(current.ConsumerKey, next.ConsumerKey) &&
        StringComparer.Ordinal.Equals(current.ConsumerSchemaVersion, next.ConsumerSchemaVersion) &&
        current.ExecutionType == next.ExecutionType &&
        StringComparer.Ordinal.Equals(current.SourceKind, next.SourceKind) &&
        StringComparer.Ordinal.Equals(current.SourceId, next.SourceId) &&
        StringComparer.Ordinal.Equals(current.Hash, next.Hash) &&
        JsonEquals(current.DescriptorPayloadSource, PayloadNode(next.DescriptorPayload)?.ToJsonString()) &&
        JsonEquals(current.InputsSource, JsonSerializer.Serialize(next.Inputs ?? [], Json)) &&
        JsonEquals(current.OutputsSource, JsonSerializer.Serialize(next.Outputs ?? [], Json)) &&
        JsonEquals(current.DesignFacetsSource, JsonSerializer.Serialize(next.DesignFacets ?? [], Json)) &&
        SameInstant(current.CreatedAt, next.CreatedAt);

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

    private static Task<ActivityDefinition?> LoadActivityDefinitionAsync(ActivitiesDesignDbContext db, string id, string tenantId, CancellationToken cancellationToken) =>
        FindScopedAsync(db.ActivityDefinitions.AsNoTracking(), "Id", id, row => row.Id, tenantId, ActivityDefinitionKind, cancellationToken);

    private static Task<ActivityDefinitionVersion?> LoadActivityVersionAsync(ActivitiesDesignDbContext db, string id, string tenantId, CancellationToken cancellationToken) =>
        FindScopedAsync(db.ActivityDefinitionVersions.AsNoTracking(), "Id", id, row => row.Id, tenantId, ActivityVersionKind, cancellationToken);

    private static Task<ActivityDefinitionAuthoringState?> LoadAuthoringAsync(ActivitiesDesignDbContext db, string definitionId, string tenantId, CancellationToken cancellationToken) =>
        FindScopedAsync(db.ActivityDefinitionAuthoringStates.AsNoTracking(), "DefinitionId", definitionId, row => row.DefinitionId, tenantId, "activityDefinitionAuthoringState", cancellationToken);

    private static Task<ActivityDefinitionAuthoringState?> LoadTrackedAuthoringAsync(ActivitiesDesignDbContext db, string definitionId, string tenantId, CancellationToken cancellationToken) =>
        FindScopedAsync(db.ActivityDefinitionAuthoringStates, "DefinitionId", definitionId, row => row.DefinitionId, tenantId, "activityDefinitionAuthoringState", cancellationToken);

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
        var matches = await rows
            .Where(row => EF.Property<string>(row, "TenantScopeKey") == tenantKey && EF.Property<string>(row, hashProperty) == identityHash)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (matches.Count > 1)
            throw new InvalidDataException($"The {kind} identity resolves to more than one row.");
        var row = matches.SingleOrDefault();
        if (row is not null && (!StringComparer.Ordinal.Equals(residual(row), identity) || !StringComparer.Ordinal.Equals(row.TenantId, tenantId)))
            throw new InvalidDataException($"The {kind} row does not match its hashed identity and tenant partition.");
        return row;
    }

    private static Task<bool> ActivityTypeKeyTakenAsync(ActivitiesDesignDbContext db, ActivityDefinition definition, string tenantId, CancellationToken cancellationToken) =>
        db.ActivityDefinitions.AsNoTracking()
            .Where(row => EF.Property<string>(row, "TenantScopeKey") == ActivitiesDesignDbContext.NormalizeTenantKey(tenantId) &&
                          row.ActivityTypeKey == definition.ActivityTypeKey)
            .AnyAsync(cancellationToken);

    private static async Task<ReusableActivityImportDefinitionBinding?> LoadBindingAsync(Elsa3ImportDbContext db, string bindingId, string tenantId, CancellationToken cancellationToken)
    {
        var tenantKey = Elsa3ImportRecordCodec.TenantKey(tenantId);
        var bindingIdHash = Elsa3ImportRecordCodec.Hash(bindingId);
        var rows = await db.DefinitionBindings.AsNoTracking()
            .Where(row => row.TenantKey == tenantKey && row.BindingIdHash == bindingIdHash)
            .OrderBy(row => row.TenantKey).ThenBy(row => row.BindingIdHash)
            .Take(2)
            .ToListAsync(cancellationToken);
        return rows.Count switch
        {
            0 => null,
            1 => Elsa3ImportRecordCodec.ReadBinding(rows[0], bindingId, tenantId),
            _ => throw new InvalidDataException("The Elsa 3 import definition binding identity resolves to more than one row.")
        };
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

    private static ActivityDefinitionVersion CloneActivityVersion(ActivityDefinitionVersion source)
    {
        var clone = ActivityDefinitionVersion.From(source);
        clone.TenantId = source.TenantId;
        clone.CreatedAt = source.CreatedAt;
        clone.LastModifiedAt = source.LastModifiedAt;
        return clone;
    }

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
        ActivityDefinitionVersion Version,
        ActivityDefinitionAuthoringState Authoring);

    private sealed record ImportPlan(
        IReadOnlyList<ImportedActivity> Activities,
        IReadOnlyList<ActivityDefinition> Definitions,
        IReadOnlyList<ActivityDefinitionVersion> Versions,
        IReadOnlyList<ActivityDefinitionAuthoringState> Authoring,
        IReadOnlyList<WorkflowDefinition> WorkflowDefinitions,
        IReadOnlyList<WorkflowDefinitionVersion> WorkflowVersions,
        IReadOnlyDictionary<string, string> WorkflowStateJson,
        IReadOnlyList<ReusableActivityImportDefinitionBinding> Bindings)
    {
        public ImportedActivity ActivityFor(string definitionId) =>
            Activities.First(activity => StringComparer.Ordinal.Equals(activity.Definition.Id, definitionId));
    }

    private sealed class Preflight
    {
        public HashSet<(string Kind, string Id)> Created { get; } = [];
        public List<ReusableActivityImportDefinitionBinding> NewBindings { get; } = [];
        public List<ActivityDefinition> NewDefinitions { get; } = [];
        public Dictionary<string, ActivityDefinition> PersistedDefinitions { get; } = new(StringComparer.Ordinal);
        public List<ActivityDefinitionVersion> NewVersions { get; } = [];
        public List<ActivityDefinitionAuthoringState> NewAuthoring { get; } = [];
        public List<AuthoringUpdate> AuthoringUpdates { get; } = [];
        public List<WorkflowDefinition> NewWorkflowDefinitions { get; } = [];
        public List<WorkflowDefinitionVersion> NewWorkflowVersions { get; } = [];

        public bool HasWrites =>
            NewBindings.Count > 0 || NewDefinitions.Count > 0 || NewVersions.Count > 0 || NewAuthoring.Count > 0 ||
            AuthoringUpdates.Count > 0 || NewWorkflowDefinitions.Count > 0 || NewWorkflowVersions.Count > 0;
    }

    private sealed record AuthoringUpdate(
        string Id,
        string DefinitionId,
        string? ExpectedHeadVersionId,
        string HeadVersionId,
        DateTimeOffset LastModifiedAt);

    private sealed record AuthoringResult(long ProjectionSequence);
}
