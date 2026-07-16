using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Mapping.Mappings;
using Elsa3.Models;

namespace Elsa3.Mapping.Services;

/// <summary>
/// Turns a reviewed Elsa 3 collection plan into Elsa 4 Design documents. Reusable workflow behavior
/// becomes graph provider source; the original workflow identity becomes a thin direct-start wrapper.
/// Exact reusable references are rewritten to the deterministic activity-version identities from the plan.
/// </summary>
public sealed class Elsa3ReusableActivityImportMaterializer(
    Elsa3WorkflowDefinitionToState stateMapper,
    Elsa3ArgumentDefinitionToInputOutput argumentMapper) : IReusableActivityImportMaterializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<ReusableActivityImportMutation> MaterializeAsync(
        ReusableActivityImportCollection collection,
        ReusableActivityImportPlan plan,
        IReadOnlyList<ReusableActivityImportItem> selection,
        CancellationToken cancellationToken = default)
    {
        var selectedById = selection.ToDictionary(x => x.SourceVersionId, StringComparer.Ordinal);
        var sourceById = collection.Definitions
            .Where(x => selectedById.ContainsKey(x.Id))
            .ToDictionary(x => x.Id, StringComparer.Ordinal);
        var canonicalByDefinition = sourceById.Values
            .GroupBy(x => x.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.Version).ThenBy(y => y.Id, StringComparer.Ordinal).First(),
                StringComparer.Ordinal);
        var headByDefinition = selection.Where(x => x.IsReusable)
            .GroupBy(x => x.SourceDefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.SourceVersion).ThenByDescending(y => y.SourceVersionId, StringComparer.Ordinal).First(),
                StringComparer.Ordinal);
        var activities = new List<ImportedReusableActivity>();
        var workflows = new List<ImportedWorkflow>();

        foreach (var item in selection.OrderBy(x => x.SourceVersionId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = sourceById[item.SourceVersionId];
            var replacements = BuildReplacements(item, sourceById, selectedById);
            var mappedState = await stateMapper.Map(source, replacements, cancellationToken);
            if (item.IsReusable)
            {
                var head = headByDefinition[source.DefinitionId];
                var lineage = canonicalByDefinition[source.DefinitionId];
                activities.Add(BuildActivity(item, source, lineage, sourceById[head.SourceVersionId], head.ActivityDefinitionVersionId!, mappedState));
                workflows.Add(BuildWorkflow(item, source, lineage, BuildWrapperState(item, mappedState)));
            }
            else
            {
                workflows.Add(BuildWorkflow(item, source, canonicalByDefinition[source.DefinitionId], mappedState));
            }
        }

        return new(
            plan.PlanId,
            plan.CollectionId,
            selection.Select(x => x.SourceVersionId).Order(StringComparer.Ordinal).ToArray(),
            activities.OrderBy(x => x.Definition.Id, StringComparer.Ordinal).ToArray(),
            workflows.OrderBy(x => x.Version.Id, StringComparer.Ordinal).ToArray());
    }

    private IReadOnlyDictionary<string, Elsa3ActivityExactReplacement> BuildReplacements(
        ReusableActivityImportItem owner,
        IReadOnlyDictionary<string, Elsa3WorkflowDefinition> sourceById,
        IReadOnlyDictionary<string, ReusableActivityImportItem> selectedById)
    {
        var replacements = new Dictionary<string, Elsa3ActivityExactReplacement>(StringComparer.Ordinal);
        foreach (var rewrite in owner.Rewrites)
        {
            var targetSource = sourceById[rewrite.TargetSourceVersionId];
            var targetItem = selectedById[rewrite.TargetSourceVersionId];
            if (!targetItem.IsReusable || !StringComparer.Ordinal.Equals(targetItem.ActivityDefinitionVersionId, rewrite.TargetActivityDefinitionVersionId))
                throw new InvalidOperationException($"Import rewrite target '{rewrite.TargetSourceVersionId}' is not the selected reusable version declared by the plan.");
            if (!replacements.TryAdd(rewrite.NodeId, new(
                    rewrite.TargetActivityDefinitionVersionId,
                    targetSource.Inputs.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                    targetSource.Outputs.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase))))
                throw new InvalidOperationException($"Import plan contains multiple rewrites for node '{rewrite.NodeId}' in source '{owner.SourceVersionId}'.");
        }
        return replacements;
    }

    private ImportedReusableActivity BuildActivity(
        ReusableActivityImportItem item,
        Elsa3WorkflowDefinition source,
        Elsa3WorkflowDefinition lineage,
        Elsa3WorkflowDefinition headSource,
        string headVersionId,
        WorkflowDefinitionState mappedState)
    {
        var definitionId = item.ActivityDefinitionId!;
        var versionId = item.ActivityDefinitionVersionId!;
        var definition = new ActivityDefinition
        {
            Id = definitionId,
            ActivityTypeKey = item.ActivityTypeKey!,
            Category = "Elsa 3 reusable workflows",
            DisplayName = lineage.Name,
            Description = lineage.Description,
            CreatedAt = lineage.CreatedAt,
            LastModifiedAt = lineage.CreatedAt
        };
        var payload = BuildGraphManifest(mappedState);
        var version = new ActivityDefinitionVersion($"{source.Version}.0.0", definitionId)
        {
            Id = versionId,
            ProviderKey = "elsa.activity-graph",
            ProviderSchemaVersion = "1",
            ConsumerKey = "elsa.graph-activity",
            ConsumerSchemaVersion = "1",
            DescriptorPayload = payload,
            Inputs = (source.Inputs ?? []).Select(argumentMapper.MapInput).ToArray(),
            Outputs = (source.Outputs ?? []).Select(argumentMapper.MapOutput).ToArray(),
            DesignFacets = [],
            SourceKind = "Elsa3WorkflowDefinition",
            SourceId = source.Id,
            Hash = item.SourceFingerprint,
            CreatedAt = source.CreatedAt,
            LastModifiedAt = source.CreatedAt
        };
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = ReusableActivityImportIdentity.Create("activity-authoring", definitionId),
            DefinitionId = definitionId,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa3.collection-import"),
            HeadVersionId = headVersionId,
            CreatedAt = lineage.CreatedAt,
            LastModifiedAt = headSource.CreatedAt
        };
        return new(definition, version, authoring);
    }

    private static ImportedWorkflow BuildWorkflow(
        ReusableActivityImportItem item,
        Elsa3WorkflowDefinition source,
        Elsa3WorkflowDefinition lineage,
        WorkflowDefinitionState state)
    {
        var definition = new WorkflowDefinition
        {
            Id = item.WorkflowDefinitionId,
            Name = lineage.Name,
            Description = lineage.Description,
            CreatedAt = lineage.CreatedAt,
            LastModifiedAt = lineage.CreatedAt
        };
        var version = new WorkflowDefinitionVersion(item.WorkflowDefinitionId, $"{source.Version}.0.0", sourceCreatedAt: source.CreatedAt)
        {
            Id = item.WorkflowVersionId,
            State = state,
            CreatedAt = source.CreatedAt,
            LastModifiedAt = source.CreatedAt
        };
        return new(definition, version);
    }

    private static WorkflowDefinitionState BuildWrapperState(
        ReusableActivityImportItem item,
        WorkflowDefinitionState source)
    {
        var inputs = source.Inputs.ToArray();
        var outputs = source.Outputs.ToArray();
        var wrapperRoot = new ActivityNode(
            ReusableActivityImportIdentity.Create("wrapper-node", item.SourceVersionId),
            item.ActivityDefinitionVersionId!,
            inputs.Select(x => new ArgumentState(
                x.ReferenceKey,
                new ArgumentValue($"getInput({JsonSerializer.Serialize(x.Name)})", "JavaScript"),
                null,
                null,
                x.StorageDriverType,
                null)).ToArray(),
            outputs.Select(x => ArgumentState.Null(x.ReferenceKey)).ToArray());
        return new([], wrapperRoot, inputs, outputs, null);
    }

    private static JsonElement BuildGraphManifest(WorkflowDefinitionState state)
    {
        var variables = state.Variables.Select(variable => new
        {
            referenceKey = variable.ReferenceKey,
            name = variable.Name,
            type = new
            {
                alias = variable.Type.Alias,
                collectionKind = variable.Type.CollectionKind.ToString() == "Single" ? "None" : variable.Type.CollectionKind.ToString()
            },
            storageDriverKey = variable.StorageDriverType ?? "elsa.json",
            initialValue = variable.Default is null ? null : new
            {
                syntax = variable.Default.ExpressionType ?? "Literal",
                value = variable.Default.Value
            }
        });
        return JsonSerializer.SerializeToElement(new
        {
            rootActivity = state.RootActivity,
            variables,
            outputMappings = Array.Empty<object>()
        }, JsonOptions);
    }
}
