using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Orchestrates workflow-executable compilation (W30b, #418): resolves the compile source, drives a single
/// authored-tree walk, and assembles the durable <see cref="WorkflowExecutable"/> artifact. Per-phase work is
/// delegated to focused collaborators — <see cref="ActivityTreeProjector"/> (walk + validate),
/// <see cref="ExecutableNodeCompiler"/> (node/resume-target compilation), and
/// <see cref="WorkflowExecutableHasher"/> (content-addressable identity).
/// </summary>
public sealed class WorkflowExecutableCompiler(
    IWorkflowDefinitionVersionStore workflowVersions,
    IActivityDefinitionVersionStore activityVersions,
    IActivityDefinitionVersionPublicationStore activityPublications,
    IExecutableActivityTemplateReader activityTemplates,
    IWorkflowExecutableSourceReferenceReader sourceReferences,
    ActivityTemplatePlacer templatePlacer,
    RuntimeInputBindingCompiler inputBindingCompiler,
    RuntimeOutputCaptureCompiler outputCaptureCompiler,
    WorkflowExecutableHasher hasher,
    ActivityTreeProjector activityTreeProjector,
    ExecutableNodeCompiler executableNodeCompiler,
    WorkflowExecutablePlacementSidecarContext? placementSidecars = null)
    : IWorkflowExecutableCompiler
{
    public async ValueTask<WorkflowExecutable> CompileAsync(
        WorkflowExecutableCompileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        WorkflowExecutableCompileSource? source = null;
        try
        {
            source = request.Source ?? await GetVersionSourceAsync(request.VersionId, cancellationToken);
            var state = source.State;
            ArgumentNullException.ThrowIfNull(state);

            var rootActivity = state.RootActivity
                ?? throw new ArgumentException(ActivityTreeProjector.NoRootActivityMessage);

            // Single tree walk: children are projected once here and reused for both flattening and node
            // compilation, replacing the former double ProjectChildren traversal.
            var projection = activityTreeProjector.Project(rootActivity);
            ActivityTreeProjector.Validate(projection.Nodes);

            var activityRows = new Dictionary<string, ActivityDefinitionVersion>(StringComparer.Ordinal);
            var placedActivities = new Dictionary<string, ExecutableNode>(StringComparer.Ordinal);
            var placedResumeTargets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal);
            var placedNodeIds = new HashSet<string>(StringComparer.Ordinal);
            var placedStorageDriverRequirements = new HashSet<RuntimeStorageDriverRequirement>();
            var placedLayoutSegments = new List<ExecutableLayoutBoundarySegment>();
            foreach (var activity in projection.Nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var publication = await activityPublications.FindAsync(activity.ActivityVersionId, cancellationToken);
                if (publication is null)
                {
                    if (!activityRows.ContainsKey(activity.ActivityVersionId))
                        activityRows[activity.ActivityVersionId] = await activityVersions.GetWithDefinitionAsync(activity.ActivityVersionId, cancellationToken);
                    continue;
                }

                if (projection.ChildProjections(activity).SelectMany(x => x.Activities).Any())
                    throw new ArgumentException($"Reusable activity node '{activity.NodeId}' cannot contain authored child activities; its structure is supplied by the exact published activity template.");

                var template = await activityTemplates.FindAsync(publication.TemplateId, cancellationToken)
                               ?? throw new ArgumentException($"Published activity version '{publication.DefinitionVersionId}' has no executable template '{publication.TemplateId}'.");
                placedStorageDriverRequirements.UnionWith(template.StorageDriverRequirements);
                var sourceReference = await sourceReferences.FindAsync(publication.SourceReferenceId, cancellationToken)
                                      ?? throw new ArgumentException($"Published activity version '{publication.DefinitionVersionId}' has no Source Reference '{publication.SourceReferenceId}'.");
                var bindings = CompileBoundaryInputs(activity, publication.Contract, inputBindingCompiler);
                var outputCaptures = outputCaptureCompiler.CompileBoundaryOutputs(
                    activity.NodeId,
                    publication.Contract.Outputs,
                    activity.Outputs,
                    state.Variables);
                var origin = new ActivityInvocationOrigin([
                    new(ActivityInvocationOriginSegmentKind.WorkflowRoot, source.DefinitionVersionId),
                    new(ActivityInvocationOriginSegmentKind.AuthoredNode, activity.NodeId),
                    new(ActivityInvocationOriginSegmentKind.TemplateBoundary, publication.DefinitionVersionId)
                ]);
                var placement = await templatePlacer.PlaceAsync(new(
                    publication,
                    template,
                    sourceReference,
                    origin,
                    publication.ActivityTypeKey,
                    bindings,
                    outputCaptures), cancellationToken);
                placedActivities.Add(activity.NodeId, placement.Root);
                placedLayoutSegments.AddRange(placement.LayoutSidecar.BoundarySegments);
                foreach (var nodeId in Flatten(placement.Root).Select(x => x.ExecutableNodeId))
                    placedNodeIds.Add(nodeId);
                foreach (var target in placement.ResumeTargets)
                {
                    if (!placedResumeTargets.TryAdd(target.Key, target.Value))
                        throw new ArgumentException($"Placed reusable activities produced duplicate resume target '{target.Key}'.");
                }
            }

            var compiledRoot = executableNodeCompiler.CompileRoot(rootActivity, projection, activityRows, placedActivities);
            var artifactHash = hasher.ComputeHash(compiledRoot);
            var artifactId = hasher.CreateArtifactId(request.ArtifactIdPrefix, artifactHash);
            var metadata = (request.CompatibilityMetadata ?? new Dictionary<string, string>())
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

            var resumeTargets = executableNodeCompiler.BuildResumeTargets(compiledRoot, placedNodeIds).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            foreach (var target in placedResumeTargets)
                if (!resumeTargets.TryAdd(target.Key, target.Value))
                    throw new ArgumentException($"Resume target '{target.Key}' collides with an ordinary workflow activity resume target.");

            var executableNodes = Flatten(compiledRoot).ToArray();
            var runtimeRequirements = executableNodes
                .Select(x => new RuntimeRequirement(x.Descriptor.ConsumerKey, x.Descriptor.SchemaVersion))
                .Distinct()
                .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
                .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
                .ToArray();
            var storageDriverRequirements = executableNodes
                .SelectMany(x => x.OutputCaptures.Values)
                .Select(x => new RuntimeStorageDriverRequirement(x.StorageDriverKey))
                .Concat(placedStorageDriverRequirements)
                .Distinct()
                .OrderBy(x => x.DriverKey, StringComparer.Ordinal)
                .ToArray();

            var executable = new WorkflowExecutable(
                identity: new WorkflowExecutableIdentity(
                    ArtifactId: artifactId,
                    DefinitionId: source.DefinitionId,
                    DefinitionVersionId: source.DefinitionVersionId,
                    ArtifactVersion: source.ArtifactVersion,
                    ArtifactHash: artifactHash),
                rootActivity: compiledRoot,
                resumeTargets: resumeTargets,
                createdAt: request.CreatedAt,
                compatibilityMetadata: metadata,
                runtimeRequirements: runtimeRequirements,
                storageDriverRequirements: storageDriverRequirements);
            placementSidecars?.Set(source.DefinitionVersionId, placedLayoutSegments);
            return executable;
        }
        catch (Exception exception) when (
            exception is not WorkflowExecutableCompilationException &&
            exception is ArgumentException or InvalidOperationException)
        {
            throw new WorkflowExecutableCompilationException(source?.DefinitionId, source?.DefinitionVersionId, exception.Message, exception);
        }
    }

    private static IReadOnlyDictionary<string, RuntimeInputBinding> CompileBoundaryInputs(
        Elsa.Workflows.Design.Core.Models.ActivityNode activity,
        ActivityContract contract,
        RuntimeInputBindingCompiler compiler)
    {
        var definitions = contract.Inputs.ToDictionary(x => x.ReferenceKey, StringComparer.Ordinal);
        var authored = activity.Inputs.ToArray();
        var duplicate = authored.GroupBy(x => x.ReferenceKey, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Activity node '{activity.NodeId}' declares input '{duplicate.Key}' more than once.");
        foreach (var input in authored)
            if (!definitions.ContainsKey(input.ReferenceKey))
                throw new ArgumentException($"Activity node '{activity.NodeId}' input '{input.ReferenceKey}' does not match the published activity contract.");

        var authoredByKey = authored.ToDictionary(x => x.ReferenceKey, StringComparer.Ordinal);
        var result = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in contract.Inputs.OrderBy(x => x.ReferenceKey, StringComparer.Ordinal))
        {
            ArgumentState? inputState = authoredByKey.TryGetValue(input.ReferenceKey, out var state)
                ? state
                : input.Default is not null
                    ? new(input.ReferenceKey, new ArgumentValue(input.Default.Value, input.Default.Syntax), null, null, null, null)
                    : null;
            if (inputState is null)
            {
                if (input.IsRequired)
                    throw new ArgumentException($"Activity node '{activity.NodeId}' is missing required input '{input.ReferenceKey}'.");
                continue;
            }

            var definition = new InputDefinition(
                input.ReferenceKey,
                input.Name,
                input.Type,
                input.StorageDriverKey,
                input.DisplayName ?? input.Name,
                input.Category,
                Description: input.Description,
                Order: input.Order,
                UiHint: input.UiHint,
                UISpecifications: input.UiSpecifications,
                IsRequired: input.IsRequired,
                DefaultValue: input.Default?.Value,
                DefaultSyntax: input.Default?.Syntax);
            result[input.Name] = compiler.Compile(activity.NodeId, definition, inputState);
        }
        return result;
    }

    private static IEnumerable<ExecutableNode> Flatten(ExecutableNode root)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            yield return node;
            foreach (var child in node.ChildSlots.SelectMany(x => x.Activities).Reverse())
                stack.Push(child);
        }
    }

    private async Task<WorkflowExecutableCompileSource> GetVersionSourceAsync(
        string versionId,
        CancellationToken cancellationToken)
    {
        var version = await workflowVersions.GetWithDefinitionAsync(versionId, cancellationToken);
        return new WorkflowExecutableCompileSource(
            DefinitionId: version.DefinitionId,
            DefinitionVersionId: version.Id,
            ArtifactVersion: version.Version,
            State: version.State,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: version.Id,
            SourceVersion: version.Version);
    }
}
