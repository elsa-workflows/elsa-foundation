using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Mapping.ActivityMap.Rewrites;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Mappings;
using Elsa3.Models;

namespace Elsa3.Mapping.Services;

/// <summary>
/// Turns a reviewed Elsa 3 collection plan into Elsa 4 Design documents. Reusable workflow behavior
/// becomes graph provider source; the original workflow identity becomes a thin direct-start wrapper.
/// Exact reusable references are rewritten to the deterministic activity-version identities from the plan.
/// </summary>
public sealed class Elsa3ReusableActivityImportMaterializer(
    IElsa3DefinitionConverter converter,
    Elsa3ArgumentDefinitionToInputOutput argumentMapper,
    IActivityDefinitionLookup activityLookup) : IReusableActivityImportMaterializer
{
    private const string SequenceTypeKey = "Elsa.Activities.Sequence.Activities.Sequence";
    private const string SequenceStructureKind = "elsa.sequence.structure";
    private const string StructureSchemaVersion = "1.0.0";

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
        var selectedDefinitionIds = sourceById.Values
            .Select(x => x.DefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        var canonicalByDefinition = collection.Definitions
            .Where(x => selectedDefinitionIds.Contains(x.DefinitionId))
            .GroupBy(x => x.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.Version).ThenBy(y => y.Id, StringComparer.Ordinal).First(),
                StringComparer.Ordinal);
        var activities = new List<ImportedReusableActivity>();
        var workflows = new List<ImportedWorkflow>();
        var failures = new List<Elsa3MigrationDiagnostic>();
        var warnings = new List<Elsa3MigrationDiagnostic>();

        // Reusable versions convert in publish order, so each caller reads the outcomes of the target it routes on
        // from that target's own conversion (spec 246 R-9); non-reusable workflows follow.
        var reusable = Elsa3ReusableImportOrder.Order(selection.Where(x => x.IsReusable).ToArray(), out var unordered);
        if (unordered.Count > 0)
            throw new ReusableActivityImportValidationException(
                "The selected reusable Elsa 3 versions cannot be published in any order.",
                unordered.Select(id => new Elsa3MigrationDiagnostic(
                    Elsa3MigrationDiagnosticSeverity.Error,
                    ReusableActivityImportDiagnosticCodes.DependencyCycle,
                    $"Reusable source version '{id}' cannot be published: it waits, directly or through the versions it uses, on a version that in turn waits on it, because Elsa 4 publishes each workflow's versions oldest first.",
                    guidance: "Typically an older version of one workflow uses a newer version of another, and an older version of that one uses a newer version of the first. Separate imports do not help. Select fewer versions, for example only the newest version of each workflow.",
                    metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["SourceVersionId"] = id },
                    pathSegments: [new(Elsa3MigrationPathSegmentKind.SourceVersion, id)])).ToArray());
        var convertedById = new Dictionary<string, Elsa3ConvertedDefinition>(StringComparer.Ordinal);

        foreach (var item in reusable.Concat(selection.Where(x => !x.IsReusable).OrderBy(x => x.SourceVersionId, StringComparer.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = sourceById[item.SourceVersionId];
            // A target that did not convert already fails the apply with its own diagnostics.
            if (item.Rewrites.Any(rewrite => !convertedById.ContainsKey(rewrite.TargetSourceVersionId)))
                continue;
            var replacements = BuildReplacements(item, sourceById, selectedById, convertedById);
            var conversion = await converter.ConvertAsync(
                new Elsa3ConversionRequest(source, item.IsReusable, replacements, DiagnosticMetadata(item)),
                cancellationToken);
            if (!conversion.Succeeded)
            {
                failures.AddRange(conversion.Diagnostics);
                continue;
            }

            warnings.AddRange(conversion.Diagnostics.Where(diagnostic => !diagnostic.IsError));
            var converted = conversion.Value!;
            var mappedState = converted.State;
            if (item.IsReusable)
            {
                convertedById[item.SourceVersionId] = converted;
                var lineage = canonicalByDefinition[source.DefinitionId];
                activities.Add(BuildActivity(item, source, lineage, converted));
                workflows.Add(BuildWorkflow(item, source, lineage, await BuildWrapperStateAsync(item, mappedState, cancellationToken)));
            }
            else
            {
                workflows.Add(BuildWorkflow(item, source, canonicalByDefinition[source.DefinitionId], mappedState));
            }
        }

        // FR-018: one unconvertible item refuses the whole apply, reporting every item's diagnostics.
        if (failures.Count > 0)
            throw new ReusableActivityImportValidationException(
                "One or more selected Elsa 3 definitions could not be converted.",
                failures);

        return new(
            plan.PlanId,
            plan.CollectionId,
            selection.Select(x => x.SourceVersionId).Order(StringComparer.Ordinal).ToArray(),
            activities,
            workflows.OrderBy(x => x.Version.Id, StringComparer.Ordinal).ToArray(),
            Warnings: warnings);
    }

    private static IReadOnlyDictionary<string, string> DiagnosticMetadata(ReusableActivityImportItem item) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SourceDefinitionId"] = item.SourceDefinitionId,
            ["SourceVersionId"] = item.SourceVersionId
        };

    private IReadOnlyDictionary<string, Elsa3ActivityExactReplacement> BuildReplacements(
        ReusableActivityImportItem owner,
        IReadOnlyDictionary<string, Elsa3WorkflowDefinition> sourceById,
        IReadOnlyDictionary<string, ReusableActivityImportItem> selectedById,
        IReadOnlyDictionary<string, Elsa3ConvertedDefinition> convertedById)
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
                    targetSource.Inputs.Select(argumentMapper.MapInput).ToArray(),
                    targetSource.Outputs.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                    CompletionOutcomes(convertedById[rewrite.TargetSourceVersionId]))))
                throw new InvalidOperationException($"Import plan contains multiple rewrites for node '{rewrite.NodeId}' in source '{owner.SourceVersionId}'.");
        }
        return replacements;
    }

    /// <summary>
    /// The outcomes a caller's connections route by (spec 239 R-3): the names of the target's converted boundary
    /// outcomes, the list its published contract declares (spec 246 R-9), so the two cannot disagree.
    /// </summary>
    private static IReadOnlySet<string> CompletionOutcomes(Elsa3ConvertedDefinition target) =>
        target.BoundaryOutcomes.Select(outcome => outcome.Name).ToHashSet(StringComparer.Ordinal);

    private static ImportedReusableActivity BuildActivity(
        ReusableActivityImportItem item,
        Elsa3WorkflowDefinition source,
        Elsa3WorkflowDefinition lineage,
        Elsa3ConvertedDefinition converted)
    {
        var definitionId = item.ActivityDefinitionId!;
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
        var version = new ImportedReusableActivityVersion(
            item.ActivityDefinitionVersionId!,
            definitionId,
            $"{source.Version}.0.0",
            item.SourceVersionId,
            Elsa3ImportedActivityContract.Build(converted),
            // Schema 2: the contract may declare several boundary outcomes, reached through Complete nodes (D-5).
            BuildGraphManifest(converted),
            item.Dependencies.Select(dependency => dependency.TargetActivityDefinitionVersionId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            source.CreatedAt);
        // No head: the definition has none until the publish phase publishes its first version (spec 246 FR-004).
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = ReusableActivityImportIdentity.Create("activity-authoring", definitionId),
            DefinitionId = definitionId,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa3.collection-import"),
            HeadVersionId = null,
            CreatedAt = lineage.CreatedAt,
            LastModifiedAt = lineage.CreatedAt
        };
        return new(definition, version, authoring, converted.State);
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
        return new(item.SourceDefinitionId, item.SourceVersionId, definition, version);
    }

    /// <summary>
    /// The direct-start wrapper: the reusable workflow's original identity, running the graph activity. When the
    /// workflow declares outputs, the wrapper is a Sequence of the graph node and one <c>set-output</c> per output that
    /// copies the graph node's output into the wrapper's own (spec FR-004e).
    /// </summary>
    private async ValueTask<WorkflowDefinitionState> BuildWrapperStateAsync(
        ReusableActivityImportItem item,
        WorkflowDefinitionState source,
        CancellationToken cancellationToken)
    {
        var state = BuildWrapperState(item, source);
        if (!state.Outputs.Any())
            return state;

        var graphNode = state.RootActivity;
        var sequenceId = ReusableActivityImportIdentity.Create("wrapper-sequence", item.SourceVersionId);
        var children = new List<ActivityNode> { graphNode };
        foreach (var output in state.Outputs)
        {
            children.Add(new ActivityNode(
                ReusableActivityImportIdentity.Create("wrapper-output", $"{item.SourceVersionId}:{output.ReferenceKey}"),
                "elsa.intrinsic.set-output@1",
                [
                    new ArgumentState("name", new ArgumentValue(output.ReferenceKey, "Literal"), null, null, null, null),
                    new ArgumentState(
                        "value",
                        new ArgumentValue(JsonSerializer.SerializeToElement(new
                        {
                            producerNodeId = graphNode.NodeId,
                            projectionKey = output.ReferenceKey,
                            producerScopeId = sequenceId
                        }), "ActivityResult"),
                        null, null, null, null)
                ],
                [])
            {
                Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.SetOutput, output.Type)
            });
        }

        var payload = JsonSerializer.SerializeToElement(new { activities = children, variables = Array.Empty<object>() }, Elsa3NativeStructureBuilder.PayloadJsonOptions);
        var sequence = new ActivityNode(
            sequenceId,
            await SequenceVersionIdAsync(cancellationToken),
            [],
            [],
            new ActivityNodeStructure(SequenceStructureKind, StructureSchemaVersion, payload));
        return state with { RootActivity = sequence };
    }

    /// <summary>The catalog version of Elsa 4's Sequence with the highest valid SemVer.</summary>
    private async ValueTask<string> SequenceVersionIdAsync(CancellationToken cancellationToken)
    {
        var definition = await activityLookup.GetDefinition(SequenceTypeKey, cancellationToken);
        var versions = await activityLookup.ListVersions(definition.Id, cancellationToken);
        return versions
            .Select(summary => (summary, valid: Elsa.Primitives.Versioning.SemVer.TryParse(summary.Version, out var semVer), semVer))
            .Where(item => item.valid)
            .OrderByDescending(item => item.semVer)
            .ThenBy(item => item.summary.Id, StringComparer.Ordinal)
            .Select(item => item.summary.Id)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"The activity catalog holds no valid version of '{SequenceTypeKey}'.");
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
                // The wrapper's own workflow request member: Elsa 4 JavaScript has no getInput.
                new ArgumentValue(JsonSerializer.SerializeToElement(new { memberKey = x.Name }), "WorkflowRequest"),
                null,
                null,
                x.StorageDriverType,
                null)).ToArray(),
            outputs.Select(x => ArgumentState.Null(x.ReferenceKey)).ToArray());
        return new([], wrapperRoot, inputs, outputs, null);
    }

    /// <summary>
    /// The schema-2 graph manifest: the converted body, its variables (the synthesized output variables included), each
    /// declared output mapped to its variable (FR-004e) and Done mapped to the natural completion (D-5).
    /// </summary>
    internal static JsonElement BuildGraphManifest(Elsa3ConvertedDefinition converted)
    {
        var state = converted.State;
        var variables = state.Variables.Select(variable => new
        {
            referenceKey = variable.ReferenceKey,
            name = variable.Name,
            type = new
            {
                alias = variable.Type.Alias,
                // The graph manifest reads the exact CollectionKind name (Single, Array, ...).
                collectionKind = variable.Type.CollectionKind.ToString()
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
            outputMappings = converted.OutputMappings.Select(mapping => new
            {
                outputReferenceKey = mapping.OutputReferenceKey,
                source = new
                {
                    syntax = "Variable",
                    value = new { referenceKey = mapping.VariableReferenceKey, declaringScopeId = VariableReference.WorkflowScopeId }
                }
            }).ToArray(),
            outcomeMappings = converted.OutcomeMappings.Select(mapping => new
            {
                sourceOutcomeReferenceKey = mapping.SourceOutcomeReferenceKey,
                boundaryOutcomeReferenceKey = mapping.BoundaryOutcomeReferenceKey
            }).ToArray()
        }, JsonOptions);
    }
}
