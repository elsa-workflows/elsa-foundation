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
    GroundworkActivityManagementProjectionWriter managementProjectionWriter) : IReusableActivityImportCommand
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

    public async ValueTask<ReusableActivityImportCommitResult> CommitAsync(
        ReusableActivityImportMutation mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ValidateMutation(mutation);

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

        var uniqueCandidates = Coalesce(candidates);
        var pending = new List<SaveDocumentRequest>();
        foreach (var candidate in uniqueCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await store.LoadAsync(candidate.DocumentKind, candidate.Id, cancellationToken);
            if (existing is null)
            {
                pending.Add(candidate);
                continue;
            }

            if (!JsonEquals(existing.ContentJson, candidate.ContentJson))
                throw new InvalidOperationException($"Elsa 3 import identity '{candidate.DocumentKind}/{candidate.Id}' is already bound to different content.");
        }

        if (pending.Count == 0)
            return new(true);

        var newDefinitionIds = pending
            .Where(x => x.DocumentKind == ActivityDefinitionKind)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);
        var newActivities = mutation.Activities.Where(x => newDefinitionIds.Contains(x.Definition.Id)).ToArray();
        if (newActivities.Length == 0)
        {
            await store.SaveAllAsync(
                DocumentCommitScope.Of(
                    ActivityDefinitionKind,
                    ActivityVersionKind,
                    ActivityAuthoringKind,
                    WorkflowDefinitionKind,
                    WorkflowVersionKind),
                pending,
                cancellationToken);
            return new(false);
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
                WorkflowVersionKind
            ],
            pending,
            cancellationToken);
        return new(false);
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
            if (!StringComparer.Ordinal.Equals(workflow.Version.DefinitionId, workflow.Definition.Id))
                throw new ArgumentException("Imported workflow definition/version identities do not align.", nameof(mutation));
    }

    private static IReadOnlyList<SaveDocumentRequest> Coalesce(IEnumerable<SaveDocumentRequest> candidates)
    {
        var result = new List<SaveDocumentRequest>();
        foreach (var group in candidates.GroupBy(x => (x.DocumentKind, x.Id)))
        {
            var first = group.First();
            if (group.Skip(1).Any(x => !StringComparer.Ordinal.Equals(x.SchemaVersion, first.SchemaVersion) || !JsonEquals(x.ContentJson, first.ContentJson)))
                throw new InvalidOperationException($"Elsa 3 import mutation binds '{first.DocumentKind}/{first.Id}' to multiple contents.");
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

    private static bool JsonEquals(string left, string right) =>
        JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));
}
