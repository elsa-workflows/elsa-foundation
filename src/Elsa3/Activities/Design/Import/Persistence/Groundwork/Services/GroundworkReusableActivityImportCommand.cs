using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa3.Activities.Design.Import.Persistence.Groundwork.Services;

/// <summary>
/// The only durable Elsa 3 selected-closure adapter. Every Activity Design and Workflow Design
/// document is preflighted before one cross-kind SaveAllAsync call. Identical documents are skipped,
/// which makes a reviewed plan safe to reapply; identity/content collisions fail before any write.
/// </summary>
public sealed class GroundworkReusableActivityImportCommand(
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    GroundworkActivityManagementProjectionWriter managementProjectionWriter,
    IPersistenceAccessContextAccessor accessContextAccessor,
    TimeProvider? timeProvider = null) : IReusableActivityImportCommand
{
    private const string ActivitySchema = "1.0.0";
    private const string WorkflowSchema = "1.0.0";
    private const string ActivityDefinitionKind = "activityDefinition";
    private const string ActivityDefinitionCollection = "activityDefinition";
    private const string ActivityVersionKind = "activityDefinitionVersion";
    private const string ActivityVersionCollection = "activityDefinitionVersion";
    private const string ActivityAuthoringKind = "activityDefinitionAuthoringState";
    private const string ActivityAuthoringCollection = "activityDefinitionAuthoringState";
    private const string WorkflowDefinitionKind = "workflowDefinition";
    private const string WorkflowDefinitionCollection = "workflowDefinition";
    private const string WorkflowVersionKind = "workflowDefinitionVersion";
    private const string WorkflowVersionCollection = "workflowDefinitionVersion";
    private const string Elsa3WorkflowDefinitionSourceKind = "Elsa3WorkflowDefinition";

    private static readonly JsonSerializerOptions PlainJson = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<ReusableActivityImportCommitResult> CommitAsync(
        ReusableActivityImportMutation mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ValidateMutation(mutation);
        if (mutation.AccessScope is not null)
        {
            EnsureCurrentScope(mutation.AccessScope);
            var prior = await FindReceiptAsync(mutation, cancellationToken);
            if (prior is not null)
                return new(true, prior with { Status = ReusableActivityImportReceiptStatus.AlreadyImported });
        }

        List<SaveDocumentRequest> candidates;
        try
        {
            candidates = [];
            foreach (var activity in mutation.Activities)
            {
                candidates.Add(ToSave(ActivityDefinitionKind, ActivityDefinitionCollection, ActivitySchema, activity.Definition, PlainJson));
                candidates.Add(ToSave(ActivityVersionKind, ActivityVersionCollection, ActivitySchema, activity.Version, ActivityVersionJson()));
                candidates.Add(ToSave(ActivityAuthoringKind, ActivityAuthoringCollection, ActivitySchema, activity.AuthoringState, PlainJson));
            }
            foreach (var workflow in mutation.Workflows)
            {
                candidates.Add(ToSave(WorkflowDefinitionKind, WorkflowDefinitionCollection, WorkflowSchema, workflow.Definition, PlainJson));
                candidates.Add(ToSave(WorkflowVersionKind, WorkflowVersionCollection, WorkflowSchema, workflow.Version, WorkflowVersionJson()));
            }
            foreach (var binding in BuildDefinitionBindings(mutation))
            {
                candidates.Add(ToSave(
                    Elsa3ImportStorageManifest.DefinitionBindingDocumentKind,
                    Elsa3ImportStorageManifest.DefinitionBindingDocumentKind,
                    Elsa3ImportStorageManifest.SchemaVersion,
                    binding,
                    PlainJson));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReusableActivityImportPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException(
                "serialize mutation",
                mutation.IdempotencyKey ?? mutation.PlanId,
                exception);
        }

        var uniqueCandidates = Coalesce(candidates, mutation.AccessScope is not null);
        var activityVersionCandidates = uniqueCandidates
            .Where(x => x.DocumentKind == ActivityVersionKind)
            .ToDictionary(x => x.Id, StringComparer.Ordinal);
        var definitionBindingCandidates = uniqueCandidates
            .Where(x => x.DocumentKind == Elsa3ImportStorageManifest.DefinitionBindingDocumentKind)
            .Select(x => ReadCandidate<DefinitionImportBinding>(x, PlainJson))
            .ToDictionary(x => (x.TargetDocumentKind, x.TargetDefinitionId));
        var pending = new List<SaveDocumentRequest>();
        var created = new HashSet<(string Kind, string Id)>();
        var persistedActivityDefinitions = new Dictionary<string, ActivityDefinition>(StringComparer.Ordinal);
        var changedAuthoring = new Dictionary<string, ActivityDefinitionAuthoringState>(StringComparer.Ordinal);
        foreach (var candidate in uniqueCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await LoadAsync(candidate.DocumentKind, candidate.Id, cancellationToken);
            if (existing is null)
            {
                pending.Add(candidate with { ExpectedVersion = 0 });
                created.Add((candidate.DocumentKind, candidate.Id));
                continue;
            }

            if (candidate.DocumentKind == ActivityDefinitionKind)
            {
                var definition = EnsureReusableDefinitionCompatible(existing, candidate, mutation.AccessScope is not null);
                await EnsureDefinitionBindingAsync(
                    ActivityDefinitionKind,
                    definition.Id,
                    definitionBindingCandidates,
                    mutation.AccessScope is not null,
                    cancellationToken);
                persistedActivityDefinitions.Add(definition.Id, definition);
                continue;
            }

            if (candidate.DocumentKind == WorkflowDefinitionKind)
            {
                var definition = EnsureWorkflowDefinitionCompatible(existing, candidate, mutation.AccessScope is not null);
                await EnsureDefinitionBindingAsync(
                    WorkflowDefinitionKind,
                    definition.Id,
                    definitionBindingCandidates,
                    mutation.AccessScope is not null,
                    cancellationToken);
                continue;
            }

            if (JsonEquals(existing.ContentJson, candidate.ContentJson))
                continue;

            if (candidate.DocumentKind == ActivityAuthoringKind)
            {
                var update = await ResolveAuthoringUpdateAsync(
                    existing,
                    candidate,
                    activityVersionCandidates,
                    cancellationToken);
                if (update is not null)
                {
                    pending.Add(update.Request);
                    changedAuthoring.Add(update.Authoring.DefinitionId, update.Authoring);
                }
                continue;
            }

            throw Collision(
                $"Elsa 3 import identity '{candidate.DocumentKind}/{candidate.Id}' is already bound to different content.",
                mutation.AccessScope is not null);
        }

        ReusableActivityImportReceipt? receipt = null;
        if (mutation.AccessScope is not null)
        {
            receipt = BuildReceipt(mutation, created);
            pending.Add(GroundworkReusableActivityImportOperationStore.SaveReceipt(receipt));
        }

        if (pending.Count == 0)
            return new(true, receipt);

        var newDefinitionIds = pending
            .Where(x => x.DocumentKind == ActivityDefinitionKind)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);
        var activitiesByDefinition = mutation.Activities
            .GroupBy(x => x.Definition.Id, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var projectionDefinitionIds = newDefinitionIds
            .Concat(changedAuthoring.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        try
        {
            if (projectionDefinitionIds.Length == 0)
            {
                await store.SaveAllAsync(
                    DocumentCommitScope.Of(
                        ActivityDefinitionKind,
                        ActivityVersionKind,
                        ActivityAuthoringKind,
                        WorkflowDefinitionKind,
                        WorkflowVersionKind,
                        Elsa3ImportStorageManifest.DefinitionBindingDocumentKind,
                        Elsa3ImportStorageManifest.ReceiptDocumentKind),
                    pending,
                    cancellationToken);
                return new(false, receipt);
            }

            var definitionChanges = projectionDefinitionIds.Select(definitionId =>
            {
                var materialized = activitiesByDefinition[definitionId];
                var definition = persistedActivityDefinitions.GetValueOrDefault(definitionId) ?? materialized.Definition;
                var authoring = changedAuthoring.GetValueOrDefault(definitionId) ?? materialized.AuthoringState;
                return new ActivityManagementDefinitionChange(definition, authoring);
            }).ToArray();
            var changedAt = definitionChanges.Max(x =>
                x.Authoring.LastModifiedAt > x.Definition.LastModifiedAt
                    ? x.Authoring.LastModifiedAt
                    : x.Definition.LastModifiedAt);
            await using var managementProjection = await managementProjectionWriter.PrepareAsync(
                new(
                    changedAt,
                    definitionChanges,
                    [],
                    []),
                cancellationToken);
            await managementProjection.CommitAsync(
                [
                    ActivityDefinitionKind,
                    ActivityVersionKind,
                    ActivityAuthoringKind,
                    WorkflowDefinitionKind,
                    WorkflowVersionKind,
                    Elsa3ImportStorageManifest.DefinitionBindingDocumentKind,
                    Elsa3ImportStorageManifest.ReceiptDocumentKind
                ],
                pending,
                cancellationToken);
            return new(false, receipt);
        }
        catch (DocumentAtomicWriteException exception) when (mutation.AccessScope is not null)
        {
            var reconciled = await FindReceiptAsync(mutation, cancellationToken);
            if (reconciled is not null)
                return new(true, reconciled with { Status = ReusableActivityImportReceiptStatus.AlreadyImported });
            throw new ReusableActivityImportCollisionException(
                "The Elsa 3 import identities changed before the atomic commit; no partial import was written.",
                exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReusableActivityImportValidationException)
        {
            throw;
        }
        catch (ReusableActivityImportCollisionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException(
                "atomic apply",
                mutation.IdempotencyKey ?? mutation.PlanId,
                exception);
        }
    }

    private static void ValidateMutation(ReusableActivityImportMutation mutation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutation.PlanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mutation.CollectionId);
        if (mutation.SourceVersionIds.Count != mutation.SourceVersionIds.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("Elsa 3 import source version identities must be unique.", nameof(mutation));
        foreach (var activity in mutation.Activities)
        {
            if (!StringComparer.Ordinal.Equals(activity.Version.DefinitionId, activity.Definition.Id) ||
                !StringComparer.Ordinal.Equals(activity.AuthoringState.DefinitionId, activity.Definition.Id))
                throw new ArgumentException("Imported activity definition/version/authoring identities do not align.", nameof(mutation));
            if (!StringComparer.Ordinal.Equals(activity.Version.SourceKind, Elsa3WorkflowDefinitionSourceKind) ||
                string.IsNullOrWhiteSpace(activity.Version.SourceId))
                throw new ArgumentException("Imported activity versions must identify their Elsa 3 source version.", nameof(mutation));
        }
        foreach (var workflow in mutation.Workflows)
        {
            if (!StringComparer.Ordinal.Equals(workflow.Version.DefinitionId, workflow.Definition.Id))
                throw new ArgumentException("Imported workflow definition/version identities do not align.", nameof(mutation));
            if (!mutation.SourceVersionIds.Contains(workflow.SourceVersionId, StringComparer.Ordinal))
                throw new ArgumentException("Imported workflow source identity is not part of the selected source versions.", nameof(mutation));
        }
    }

    private static IReadOnlyList<DefinitionImportBinding> BuildDefinitionBindings(
        ReusableActivityImportMutation mutation)
    {
        var sourceDefinitionsByVersion = mutation.Workflows.ToDictionary(
            x => x.SourceVersionId,
            x => x.SourceDefinitionId,
            StringComparer.Ordinal);
        var bindings = mutation.Workflows.Select(workflow => new DefinitionImportBinding
            {
                Id = BindingId(WorkflowDefinitionKind, workflow.Definition.Id),
                TargetDocumentKind = WorkflowDefinitionKind,
                TargetDefinitionId = workflow.Definition.Id,
                SourceKind = Elsa3WorkflowDefinitionSourceKind,
                SourceDefinitionId = workflow.SourceDefinitionId,
                TenantId = workflow.Definition.TenantId,
                CreatedAt = workflow.Definition.CreatedAt,
                LastModifiedAt = workflow.Definition.CreatedAt
            })
            .Concat(mutation.Activities.Select(activity =>
            {
                if (!sourceDefinitionsByVersion.TryGetValue(activity.Version.SourceId!, out var sourceDefinitionId))
                    throw new ArgumentException(
                        $"Imported activity source version '{activity.Version.SourceId}' has no matching workflow lineage.",
                        nameof(mutation));
                return new DefinitionImportBinding
                {
                    Id = BindingId(ActivityDefinitionKind, activity.Definition.Id),
                    TargetDocumentKind = ActivityDefinitionKind,
                    TargetDefinitionId = activity.Definition.Id,
                    SourceKind = Elsa3WorkflowDefinitionSourceKind,
                    SourceDefinitionId = sourceDefinitionId,
                    TenantId = activity.Definition.TenantId,
                    CreatedAt = activity.Definition.CreatedAt,
                    LastModifiedAt = activity.Definition.CreatedAt
                };
            }))
            .ToArray();
        return bindings;
    }

    private static IReadOnlyList<SaveDocumentRequest> Coalesce(
        IEnumerable<SaveDocumentRequest> candidates,
        bool useDomainException)
    {
        var result = new List<SaveDocumentRequest>();
        foreach (var group in candidates.GroupBy(x => (x.DocumentKind, x.Id)))
        {
            var first = group.First();
            if (group.Skip(1).Any(x => !StringComparer.Ordinal.Equals(x.SchemaVersion, first.SchemaVersion) || !JsonEquals(x.ContentJson, first.ContentJson)))
                throw Collision(
                    $"Elsa 3 import mutation binds '{first.DocumentKind}/{first.Id}' to multiple contents.",
                    useDomainException);
            result.Add(first);
        }
        return result.OrderBy(x => x.DocumentKind, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }

    private SaveDocumentRequest ToSave<TEntity>(
        string kind,
        string collection,
        string schema,
        TEntity entity,
        JsonSerializerOptions options)
        where TEntity : TenantEntity =>
        GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            kind,
            collection,
            schema,
            entity,
            options,
            accessContextAccessor.Current);

    private static TEntity ReadEntity<TEntity>(
        DocumentEnvelope envelope,
        JsonSerializerOptions options)
        where TEntity : Entity
    {
        try
        {
            return JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(envelope.ContentJson, options)?.Entity
                   ?? throw new JsonException("Groundwork document is empty.");
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException(
                "deserialize document",
                $"{envelope.DocumentKind}/{envelope.Id}",
                exception);
        }
    }

    private static ActivityDefinition EnsureReusableDefinitionCompatible(
        DocumentEnvelope existing,
        SaveDocumentRequest candidate,
        bool useDomainException)
    {
        var current = ReadEntity<ActivityDefinition>(existing, PlainJson);
        var next = ReadCandidate<ActivityDefinition>(candidate, PlainJson);
        if (!StringComparer.Ordinal.Equals(current.Id, next.Id) ||
            !StringComparer.Ordinal.Equals(current.ActivityTypeKey, next.ActivityTypeKey) ||
            !StringComparer.Ordinal.Equals(current.TenantId, next.TenantId) ||
            !StringComparer.Ordinal.Equals(current.Category, next.Category) ||
            !StringComparer.Ordinal.Equals(current.DisplayName, next.DisplayName) ||
            !StringComparer.Ordinal.Equals(current.Description, next.Description) ||
            current.CreatedAt != next.CreatedAt)
            throw Collision(
                $"Elsa 3 activity definition identity '{candidate.Id}' is already owned by a different resource.",
                useDomainException);
        return current;
    }

    private static WorkflowDefinition EnsureWorkflowDefinitionCompatible(
        DocumentEnvelope existing,
        SaveDocumentRequest candidate,
        bool useDomainException)
    {
        var current = ReadEntity<WorkflowDefinition>(existing, PlainJson);
        var next = ReadCandidate<WorkflowDefinition>(candidate, PlainJson);
        if (!StringComparer.Ordinal.Equals(current.Id, next.Id) ||
            !StringComparer.Ordinal.Equals(current.TenantId, next.TenantId) ||
            !StringComparer.Ordinal.Equals(current.Name, next.Name) ||
            !StringComparer.Ordinal.Equals(current.Description, next.Description) ||
            current.CreatedAt != next.CreatedAt ||
            current.DeletedAt != next.DeletedAt ||
            !StringComparer.Ordinal.Equals(current.DeletedReason, next.DeletedReason))
            throw Collision(
                $"Elsa 3 workflow definition identity '{candidate.Id}' is already owned by a different resource.",
                useDomainException);
        return current;
    }

    private async ValueTask EnsureDefinitionBindingAsync(
        string targetDocumentKind,
        string targetDefinitionId,
        IReadOnlyDictionary<(string TargetDocumentKind, string TargetDefinitionId), DefinitionImportBinding> candidates,
        bool useDomainException,
        CancellationToken cancellationToken)
    {
        if (!candidates.TryGetValue((targetDocumentKind, targetDefinitionId), out var candidate))
            throw new ReusableActivityImportPersistenceException(
                "preflight definition binding",
                $"{targetDocumentKind}/{targetDefinitionId}",
                new InvalidOperationException("The import mutation does not contain a definition provenance binding."));
        var existing = await LoadAsync(
            Elsa3ImportStorageManifest.DefinitionBindingDocumentKind,
            candidate.Id,
            cancellationToken);
        if (existing is null)
            throw Collision(
                $"Elsa 3 {targetDocumentKind} identity '{targetDefinitionId}' has no matching import provenance.",
                useDomainException);
        var current = ReadEntity<DefinitionImportBinding>(existing, PlainJson);
        if (!StringComparer.Ordinal.Equals(current.Id, candidate.Id) ||
            !StringComparer.Ordinal.Equals(current.TargetDocumentKind, candidate.TargetDocumentKind) ||
            !StringComparer.Ordinal.Equals(current.TargetDefinitionId, candidate.TargetDefinitionId) ||
            !StringComparer.Ordinal.Equals(current.SourceKind, candidate.SourceKind) ||
            !StringComparer.Ordinal.Equals(current.SourceDefinitionId, candidate.SourceDefinitionId) ||
            !StringComparer.Ordinal.Equals(current.TenantId, candidate.TenantId))
            throw Collision(
                $"Elsa 3 {targetDocumentKind} identity '{targetDefinitionId}' is bound to different import provenance.",
                useDomainException);
    }

    private static TEntity ReadCandidate<TEntity>(
        SaveDocumentRequest candidate,
        JsonSerializerOptions options)
        where TEntity : Entity
    {
        try
        {
            return JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(candidate.ContentJson, options)?.Entity
                   ?? throw new JsonException("Groundwork candidate document is empty.");
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException(
                "deserialize candidate",
                $"{candidate.DocumentKind}/{candidate.Id}",
                exception);
        }
    }

    private async ValueTask<AuthoringUpdate?> ResolveAuthoringUpdateAsync(
        DocumentEnvelope existing,
        SaveDocumentRequest candidate,
        IReadOnlyDictionary<string, SaveDocumentRequest> activityVersionCandidates,
        CancellationToken cancellationToken)
    {
        var current = ReadEntity<ActivityDefinitionAuthoringState>(existing, PlainJson);
        var next = ReadCandidate<ActivityDefinitionAuthoringState>(candidate, PlainJson);
        if (!StringComparer.Ordinal.Equals(current.DefinitionId, next.DefinitionId) ||
            current.ContentAuthority != next.ContentAuthority ||
            !StringComparer.Ordinal.Equals(current.TenantId, next.TenantId))
            throw new ReusableActivityImportCollisionException(
                $"Elsa 3 activity authoring identity '{candidate.Id}' is already owned by a different resource.");

        if (next.HeadVersionId is null || StringComparer.Ordinal.Equals(current.HeadVersionId, next.HeadVersionId))
            return null;
        if (current.HeadVersionId is not null &&
            await CompareActivityVersionsAsync(
                current.HeadVersionId,
                next.HeadVersionId,
                candidate,
                activityVersionCandidates,
                cancellationToken) >= 0)
            return null;

        current.HeadVersionId = next.HeadVersionId;
        current.LastModifiedAt = next.LastModifiedAt > current.LastModifiedAt
            ? next.LastModifiedAt
            : current.LastModifiedAt;
        return new(
            ToSave(ActivityAuthoringKind, ActivityAuthoringCollection, ActivitySchema, current, PlainJson)
                with { ExpectedVersion = existing.Version },
            current);
    }

    private async ValueTask<int> CompareActivityVersionsAsync(
        string currentVersionId,
        string nextVersionId,
        SaveDocumentRequest nextAuthoring,
        IReadOnlyDictionary<string, SaveDocumentRequest> activityVersionCandidates,
        CancellationToken cancellationToken)
    {
        var currentEnvelope = await LoadAsync(ActivityVersionKind, currentVersionId, cancellationToken)
                              ?? throw new ReusableActivityImportCollisionException(
                                  $"Elsa 3 activity authoring identity '{nextAuthoring.Id}' refers to missing head version '{currentVersionId}'.");
        var nextEnvelope = await LoadAsync(ActivityVersionKind, nextVersionId, cancellationToken);
        var current = ReadEntity<ActivityDefinitionVersion>(currentEnvelope, ActivityVersionJson());
        ActivityDefinitionVersion next;
        if (nextEnvelope is not null)
            next = ReadEntity<ActivityDefinitionVersion>(nextEnvelope, ActivityVersionJson());
        else if (activityVersionCandidates.TryGetValue(nextVersionId, out var nextCandidate))
            next = ReadCandidate<ActivityDefinitionVersion>(nextCandidate, ActivityVersionJson());
        else
        {
            throw new ReusableActivityImportCollisionException(
                $"Elsa 3 activity authoring identity '{nextAuthoring.Id}' refers to a head version that is not available for comparison.");
        }
        return StringComparer.Ordinal.Compare(current.SemVerSortKey, next.SemVerSortKey);
    }

    private JsonSerializerOptions ActivityVersionJson() => GroundworkDocumentSerialization.Create(
        payloadSerializer,
        [nameof(Entity.RowNumber), "LegacyDescriptorType", "DescriptorPayloadSource", "InputsSource", "OutputsSource", "DesignFacetsSource", "Definition"],
        [typeof(IEnumerable<InputDefinition>), typeof(IEnumerable<OutputDefinition>), typeof(IEnumerable<ActivityDesignFacet>)]);

    private JsonSerializerOptions WorkflowVersionJson() => GroundworkDocumentSerialization.Create(
        payloadSerializer,
        [nameof(Entity.RowNumber), "StateSource", "Definition", "WorkflowDefinition"],
        [typeof(WorkflowDefinitionState)]);

    private static bool JsonEquals(string left, string right)
    {
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));
        }
        catch (JsonException exception)
        {
            throw new ReusableActivityImportPersistenceException("compare document content", "JSON", exception);
        }
    }

    private static InvalidOperationException Collision(string message, bool useDomainException) =>
        useDomainException
            ? new ReusableActivityImportCollisionException(message)
            : new InvalidOperationException(message);

    private async ValueTask<ReusableActivityImportReceipt?> FindReceiptAsync(
        ReusableActivityImportMutation mutation,
        CancellationToken cancellationToken)
    {
        var scope = mutation.AccessScope!;
        var idempotencyKey = mutation.IdempotencyKey!;
        var receiptId = Elsa3ImportStorageManifest.ReceiptId(idempotencyKey, scope);
        var envelope = await LoadAsync(Elsa3ImportStorageManifest.ReceiptDocumentKind, receiptId, cancellationToken);
        if (envelope is null)
            return null;
        var receipt = GroundworkReusableActivityImportOperationStore.ReadReceipt(envelope);
        var expectedFingerprint = ReusableActivityImportOperationService.SelectionFingerprint(
            mutation.CollectionId,
            mutation.PlanId,
            mutation.SourceVersionIds,
            scope);
        if (!StringComparer.Ordinal.Equals(receipt.CollectionHandle, mutation.CollectionId) ||
            !StringComparer.Ordinal.Equals(receipt.PlanId, mutation.PlanId) ||
            !StringComparer.Ordinal.Equals(receipt.SelectionFingerprint, expectedFingerprint) ||
            !StringComparer.Ordinal.Equals(receipt.AccessScope.TenantId, scope.TenantId) ||
            !StringComparer.Ordinal.Equals(receipt.AccessScope.UserId, scope.UserId))
            throw new ReusableActivityImportIdempotencyConflictException(idempotencyKey);
        return receipt;
    }

    private ReusableActivityImportReceipt BuildReceipt(
        ReusableActivityImportMutation mutation,
        IReadOnlySet<(string Kind, string Id)> created)
    {
        var scope = mutation.AccessScope!;
        var idempotencyKey = mutation.IdempotencyKey!;
        var sources = mutation.SourceVersionIds.Order(StringComparer.Ordinal).Select(sourceVersionId =>
        {
            var workflow = mutation.Workflows.Single(x => StringComparer.Ordinal.Equals(x.SourceVersionId, sourceVersionId));
            var activity = mutation.Activities.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.Version.SourceId, sourceVersionId));
            return new ReusableActivityImportSourceReceipt(
                workflow.SourceDefinitionId,
                sourceVersionId,
                workflow.Definition.Id,
                workflow.Version.Id,
                Disposition(WorkflowVersionKind, workflow.Version.Id, created),
                $"/design/workflows/definitions/{Uri.EscapeDataString(workflow.Definition.Id)}/versions/{Uri.EscapeDataString(workflow.Version.Id)}",
                activity?.Definition.Id,
                activity?.Version.Id,
                activity is null ? null : Disposition(ActivityDefinitionKind, activity.Definition.Id, created),
                activity is null ? null : Disposition(ActivityVersionKind, activity.Version.Id, created),
                activity is null ? null : $"/design/activities/definitions/{Uri.EscapeDataString(activity.Definition.Id)}",
                activity is null ? null : $"/design/activities/versions/{Uri.EscapeDataString(activity.Version.Id)}");
        }).ToArray();
        var receiptId = Elsa3ImportStorageManifest.ReceiptId(idempotencyKey, scope);
        return new(
            receiptId,
            mutation.CollectionId,
            mutation.PlanId,
            idempotencyKey,
            ReusableActivityImportOperationService.SelectionFingerprint(
                mutation.CollectionId,
                mutation.PlanId,
                mutation.SourceVersionIds,
                scope),
            scope,
            ReusableActivityImportReceiptStatus.Applied,
            _timeProvider.GetUtcNow(),
            sources);
    }

    private static ReusableActivityImportResourceDisposition Disposition(
        string kind,
        string id,
        IReadOnlySet<(string Kind, string Id)> created) =>
        created.Contains((kind, id))
            ? ReusableActivityImportResourceDisposition.Created
            : ReusableActivityImportResourceDisposition.Reused;

    private async ValueTask<DocumentEnvelope?> LoadAsync(
        string kind,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            return await store.LoadAsync(kind, id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReusableActivityImportPersistenceException("load", $"{kind}/{id}", exception);
        }
    }

    private void EnsureCurrentScope(ReusableActivityImportAccessScope accessScope)
    {
        try
        {
            if (accessScope.TenantId is { } tenantId)
            {
                accessContextAccessor.Current.EnsureScope(new PersistenceScope(tenantId));
                return;
            }

            if (!accessContextAccessor.Current.IsGlobal)
                throw new InvalidOperationException("The requested resource does not belong to the current persistence scope.");
        }
        catch (InvalidOperationException exception)
        {
            throw new ReusableActivityImportPersistenceException(
                "validate persistence scope",
                ScopeIdentity(accessScope),
                exception);
        }
    }

    private static string ScopeIdentity(ReusableActivityImportAccessScope accessScope) =>
        ReusableActivityImportIdentity.Create("scope", accessScope.TenantScope, accessScope.UserId);

    private static string BindingId(string targetDocumentKind, string targetDefinitionId) =>
        ReusableActivityImportIdentity.Create("definition-binding", targetDocumentKind, targetDefinitionId);

    private sealed class DefinitionImportBinding : TenantEntity
    {
        public string TargetDocumentKind { get; init; } = null!;
        public string TargetDefinitionId { get; init; } = null!;
        public string SourceKind { get; init; } = null!;
        public string SourceDefinitionId { get; init; } = null!;
    }

    private sealed record AuthoringUpdate(
        SaveDocumentRequest Request,
        ActivityDefinitionAuthoringState Authoring);
}
