using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Querying;
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
            var prior = await FindReceiptAsync(mutation, cancellationToken);
            if (prior is not null)
                return new(true, prior with { Status = ReusableActivityImportReceiptStatus.AlreadyImported });
        }

        var candidates = new List<SaveDocumentRequest>();
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

        var uniqueCandidates = Coalesce(candidates, mutation.AccessScope is not null);
        var pending = new List<SaveDocumentRequest>();
        var created = new HashSet<(string Kind, string Id)>();
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

            if (!JsonEquals(existing.ContentJson, candidate.ContentJson))
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
        var newActivities = mutation.Activities.Where(x => newDefinitionIds.Contains(x.Definition.Id)).ToArray();
        try
        {
            if (newActivities.Length == 0)
            {
                await store.SaveAllAsync(
                    DocumentCommitScope.Of(
                        ActivityDefinitionKind,
                        ActivityVersionKind,
                        ActivityAuthoringKind,
                        WorkflowDefinitionKind,
                        WorkflowVersionKind,
                        Elsa3ImportStorageManifest.ReceiptDocumentKind),
                    pending,
                    cancellationToken);
                return new(false, receipt);
            }

            var newDefinitions = newActivities
                .GroupBy(activity => activity.Definition.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            var changedAt = newDefinitions.Max(x => x.Definition.LastModifiedAt);
            await using var managementProjection = await managementProjectionWriter.PrepareAsync(
                new(
                    changedAt,
                    newDefinitions.Select(x => new ActivityManagementDefinitionChange(x.Definition, x.AuthoringState)).ToArray(),
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
        }
        foreach (var workflow in mutation.Workflows)
        {
            if (!StringComparer.Ordinal.Equals(workflow.Version.DefinitionId, workflow.Definition.Id))
                throw new ArgumentException("Imported workflow definition/version identities do not align.", nameof(mutation));
            if (!mutation.SourceVersionIds.Contains(workflow.SourceVersionId, StringComparer.Ordinal))
                throw new ArgumentException("Imported workflow source identity is not part of the selected source versions.", nameof(mutation));
        }
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

    private static SaveDocumentRequest ToSave<TEntity>(
        string kind,
        string collection,
        string schema,
        TEntity entity,
        JsonSerializerOptions options)
        where TEntity : Entity =>
        GroundworkDocumentWriter.ToSaveRequest(kind, collection, schema, entity, options);

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
}
