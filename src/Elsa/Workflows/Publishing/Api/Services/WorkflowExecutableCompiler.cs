using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

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
    WorkflowExecutablePlacementSidecarContext? placementSidecars = null,
    IExecutableNodeMetadataEnricher? metadataEnricher = null,
    IWorkflowExecutableStore? executableStore = null,
    ActivityResultConversionPlanLinker? activityResultConversionPlanLinker = null)
    : IWorkflowExecutableCompiler
{
    private readonly IWorkflowExecutableStore? _executableStore = executableStore;

    /// <summary>Preserves current main's pre-dependency-store primary-constructor signature.</summary>
    public WorkflowExecutableCompiler(
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
        WorkflowExecutablePlacementSidecarContext? placementSidecars,
        IExecutableNodeMetadataEnricher? metadataEnricher)
        : this(
            workflowVersions,
            activityVersions,
            activityPublications,
            activityTemplates,
            sourceReferences,
            templatePlacer,
            inputBindingCompiler,
            outputCaptureCompiler,
            hasher,
            activityTreeProjector,
            executableNodeCompiler,
            placementSidecars,
            metadataEnricher,
            executableStore: null)
    {
    }

    public WorkflowExecutableCompiler(
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
        WorkflowExecutablePlacementSidecarContext? placementSidecars)
        : this(
            workflowVersions,
            activityVersions,
            activityPublications,
            activityTemplates,
            sourceReferences,
            templatePlacer,
            inputBindingCompiler,
            outputCaptureCompiler,
            hasher,
            activityTreeProjector,
            executableNodeCompiler,
            placementSidecars,
            metadataEnricher: null)
    {
    }

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
            executableNodeCompiler.ValidateIntrinsicVariableTargets(state, projection.Nodes);

            var activityRows = new Dictionary<string, ActivityDefinitionVersion>(StringComparer.Ordinal);
            var placedActivities = new Dictionary<string, ExecutableNode>(StringComparer.Ordinal);
            var placedResumeTargets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal);
            var placedNodeIds = new HashSet<string>(StringComparer.Ordinal);
            var placedStorageDriverRequirements = new HashSet<RuntimeStorageDriverRequirement>();
            var placedLayoutSegments = new List<ExecutableLayoutBoundarySegment>();
            foreach (var activity in projection.Nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (activity.Intrinsic is not null)
                    continue;

                var publication = await activityPublications.FindAsync(activity.ActivityVersionId, cancellationToken);
                if (publication is null ||
                    publication.ResolveWorkflowResolutionKind() == ActivityDefinitionVersionResolutionKind.AuthorableActivity)
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
                // #1007: a reusable placed as a child inside a workflow container (Sequence, Flowchart, …) must
                // expose the authored node id as its boundary ExecutableNodeId, because the container's compiled
                // structure addresses that child by its authored node id. A reusable that is the workflow root is
                // addressed by nothing structural, so it keeps its content-addressed placement id.
                var isWorkflowRoot = StringComparer.Ordinal.Equals(activity.NodeId, rootActivity.NodeId);
                var placement = await templatePlacer.PlaceAsync(new(
                    publication,
                    template,
                    sourceReference,
                    origin,
                    publication.ActivityTypeKey,
                    bindings,
                    outputCaptures,
                    BindBoundaryRootToAuthoredNode: !isWorkflowRoot), cancellationToken);
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

            var compiledRoot = executableNodeCompiler.CompileRoot(rootActivity, projection, activityRows, placedActivities, state.Variables);
            IReadOnlyCollection<ExecutableDependencyClaim> dependencyClaims = [];
            if (metadataEnricher is not null)
            {
                var enrichment = await metadataEnricher.EnrichCompilationAsync(request, source, compiledRoot, cancellationToken);
                compiledRoot = enrichment.RootActivity;
                dependencyClaims = enrichment.Dependencies;
            }

            ValidatePinnedActivityContracts(compiledRoot);

            // Direct result references can only be resolved once the complete executable tree (including
            // placed template boundaries and optional metadata enrichment) is available. Pin the producer
            // contract before computing the behavioral hash.
            compiledRoot = (activityResultConversionPlanLinker ?? new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()))
                .Link(compiledRoot);

            var inputContract = BuildInputContract(state.Inputs);
            var dependencies = BuildDependencies(dependencyClaims);
            var checkpointCadence = CompileCheckpointCadence(state.StrategyOptions);
            var workflowVariables = executableNodeCompiler.CompileWorkflowVariables(state.Variables);
            var artifactHash = hasher.ComputeHash(compiledRoot, inputContract, dependencies, checkpointCadence, workflowVariables);
            var artifactId = hasher.CreateArtifactId(request.ArtifactIdPrefix, artifactHash);
            await ValidateDependencyGraphAsync(artifactId, artifactHash, dependencies, cancellationToken);
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
                storageDriverRequirements: storageDriverRequirements,
                inputContract: inputContract,
                dependencies: dependencies,
                checkpointCadence: checkpointCadence,
                workflowVariables: workflowVariables);
            placementSidecars?.Set(source.DefinitionVersionId, placedLayoutSegments);
            return executable;
        }
        catch (Exception exception) when (
            exception is not WorkflowExecutableCompilationException &&
            (exception is WorkflowExecutableDependencyGraphException or ArgumentException or InvalidOperationException))
        {
            throw new WorkflowExecutableCompilationException(source?.DefinitionId, source?.DefinitionVersionId, exception.Message, exception);
        }
    }

    private static IReadOnlyDictionary<string, RuntimeInputBinding> CompileBoundaryInputs(
        Elsa.Workflows.Design.Core.Models.ActivityNode activity,
        Elsa.Activities.Design.Core.Models.ActivityContract contract,
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
                input.IsNullable,
                Description: input.Description,
                Order: input.Order,
                UiHint: input.UiHint,
                UISpecifications: input.UiSpecifications,
                IsRequired: input.IsRequired,
                DefaultValue: input.Default?.Value,
                DefaultSyntax: input.Default?.Syntax);
            result[input.ReferenceKey] = compiler.Compile(activity.NodeId, definition, inputState);
        }
        return result;
    }

    /// <summary>
    /// Fail-fast mirror of the runtime VF-ACT-001 dispatch check: a CLR activity node that reaches the
    /// runtime without a pinned contract poisons its scheduler work item, so publication refuses the
    /// artifact instead. The gate runs on the final tree — after node compilation, template placement,
    /// and metadata enrichment — so a contract dropped by any of those rebuilds is caught here.
    /// </summary>
    private static void ValidatePinnedActivityContracts(ExecutableNode root)
    {
        foreach (var node in Flatten(root))
        {
            if (node.IntrinsicKind is not null ||
                node.ActivityContract is not null ||
                !StringComparer.Ordinal.Equals(node.Descriptor.ConsumerKey, WellKnownRuntimeActivityConsumers.ClrActivity))
                continue;

            string? typeAlias = null;
            try
            {
                typeAlias = node.Descriptor.Payload
                    .Deserialize<ClrActivityDescriptor>(new JsonSerializerOptions(JsonSerializerDefaults.Web))?
                    .TypeAlias;
            }
            catch (JsonException)
            {
                // A malformed descriptor payload still fails the gate; the alias is just unavailable for the message.
            }

            throw new ArgumentException(
                $"VF-ACT-001: Executable CLR activity node '{node.ExecutableNodeId}' (activity type '{node.ActivityType}', type alias '{typeAlias ?? "<unknown>"}') compiled without a pinned activity contract. " +
                "The type alias must resolve to a registered CLR activity type that declares a typed result; publication is refused instead of deferring the failure to runtime dispatch.");
        }
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

    private async ValueTask ValidateDependencyGraphAsync(
        string candidateArtifactId,
        string candidateArtifactHash,
        IReadOnlyCollection<WorkflowExecutableDependency> dependencies,
        CancellationToken cancellationToken)
    {
        if (dependencies.Count == 0 || _executableStore is null)
            return;

        var storedExecutables = await _executableStore.ListAllAsync(cancellationToken);
        var roots = dependencies
            .Select(dependency => new WorkflowExecutableIdentity(
                dependency.ArtifactId,
                DefinitionId: string.Empty,
                DefinitionVersionId: string.Empty,
                ArtifactVersion: string.Empty,
                dependency.ArtifactHash))
            .ToArray();
        var closure = WorkflowExecutableDependencyGraph.ResolveClosure(roots, storedExecutables);
        var recurrencePath = FindDependencyPath(roots, closure, candidateArtifactId, candidateArtifactHash);
        if (recurrencePath is not null)
        {
            var renderedPath = string.Join(
                " -> ",
                recurrencePath.Select(identity => $"{identity.ArtifactId}@{identity.ArtifactHash}"));
            throw new ArgumentException(
                $"Candidate executable recurs in its dependency closure: {candidateArtifactId}@{candidateArtifactHash} -> {renderedPath}.",
                nameof(dependencies));
        }
    }

    private static IReadOnlyCollection<WorkflowExecutableIdentity>? FindDependencyPath(
        IReadOnlyCollection<WorkflowExecutableIdentity> roots,
        IReadOnlyCollection<WorkflowExecutable> closure,
        string targetArtifactId,
        string targetArtifactHash)
    {
        var byArtifactId = closure.ToDictionary(executable => executable.Identity.ArtifactId, StringComparer.Ordinal);
        var visited = new HashSet<(string ArtifactId, string ArtifactHash)>();
        var path = new List<WorkflowExecutableIdentity>();
        foreach (var root in roots
                     .OrderBy(identity => identity.ArtifactId, StringComparer.Ordinal)
                     .ThenBy(identity => identity.ArtifactHash, StringComparer.Ordinal))
        {
            if (Visit(root))
                return Array.AsReadOnly(path.ToArray());
        }

        return null;

        bool Visit(WorkflowExecutableIdentity expected)
        {
            if (!visited.Add((expected.ArtifactId, expected.ArtifactHash)))
                return false;

            var executable = byArtifactId[expected.ArtifactId];
            path.Add(executable.Identity);
            if (StringComparer.Ordinal.Equals(executable.Identity.ArtifactId, targetArtifactId) &&
                StringComparer.Ordinal.Equals(executable.Identity.ArtifactHash, targetArtifactHash))
            {
                return true;
            }

            foreach (var dependency in executable.Dependencies
                         .OrderBy(item => item.ArtifactId, StringComparer.Ordinal)
                         .ThenBy(item => item.ArtifactHash, StringComparer.Ordinal))
            {
                if (Visit(new WorkflowExecutableIdentity(
                        dependency.ArtifactId,
                        DefinitionId: string.Empty,
                        DefinitionVersionId: string.Empty,
                        ArtifactVersion: string.Empty,
                        dependency.ArtifactHash)))
                {
                    return true;
                }
            }

            path.RemoveAt(path.Count - 1);
            return false;
        }
    }

    // ADR 0032 R5: compile the authored per-workflow checkpoint cadence into the executable. The mode is a stable
    // string alias validated here (fail-fast at publish, mirroring the contract gate discipline); an unauthored cadence
    // compiles to null so the host default applies and the artifact hash is unchanged from before this field existed.
    private static WorkflowExecutableCheckpointCadence? CompileCheckpointCadence(WorkflowStrategyOptions? strategyOptions)
    {
        var authored = strategyOptions?.CheckpointCadence;
        if (authored is null || string.IsNullOrWhiteSpace(authored.Mode))
            return null;

        if (StringComparer.Ordinal.Equals(authored.Mode, WorkflowExecutableCheckpointCadence.ImmediateMode))
            return new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.ImmediateMode);

        if (StringComparer.Ordinal.Equals(authored.Mode, WorkflowExecutableCheckpointCadence.CoalescedMode))
        {
            if (authored.MaxSegmentCheckpoints is { } maxSegmentCheckpoints && maxSegmentCheckpoints <= 0)
                throw new ArgumentException(
                    $"Authored checkpoint cadence 'MaxSegmentCheckpoints' must be greater than zero, but was {maxSegmentCheckpoints}.");

            return new WorkflowExecutableCheckpointCadence(
                WorkflowExecutableCheckpointCadence.CoalescedMode,
                authored.MaxSegmentCheckpoints);
        }

        throw new ArgumentException(
            $"Authored checkpoint cadence mode '{authored.Mode}' is not a recognised alias. Use '{WorkflowExecutableCheckpointCadence.ImmediateMode}' or '{WorkflowExecutableCheckpointCadence.CoalescedMode}'.");
    }

    private static WorkflowExecutableInputContract BuildInputContract(IEnumerable<InputDefinition> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var declarations = inputs.Select(input =>
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!string.IsNullOrWhiteSpace(input.DefaultSyntax) &&
                !StringComparer.Ordinal.Equals(input.DefaultSyntax, "Literal"))
            {
                throw new ArgumentException(
                    $"Workflow input '{input.Name}' uses unsupported default syntax '{input.DefaultSyntax}'. Only literal defaults can be compiled.");
            }

            return new WorkflowDeclaredInput(input.Name, input.Type, input.IsRequired, input.DefaultValue);
        }).ToArray();

        return new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, declarations);
    }

    private static IReadOnlyCollection<WorkflowExecutableDependency> BuildDependencies(
        IReadOnlyCollection<ExecutableDependencyClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var snapshot = claims.ToArray();
        foreach (var claim in snapshot)
        {
            ArgumentNullException.ThrowIfNull(claim);
            ArgumentException.ThrowIfNullOrWhiteSpace(claim.ExecutableNodeId);
            ArgumentException.ThrowIfNullOrWhiteSpace(claim.ArtifactId);
            ArgumentException.ThrowIfNullOrWhiteSpace(claim.ArtifactHash);
        }

        var conflictingNode = snapshot
            .GroupBy(claim => claim.ExecutableNodeId, StringComparer.Ordinal)
            .FirstOrDefault(group => group
                .Select(claim => (claim.ArtifactId, claim.ArtifactHash))
                .Distinct()
                .Skip(1)
                .Any());
        if (conflictingNode is not null)
            throw new ArgumentException($"Executable node '{conflictingNode.Key}' has conflicting dependency claims.");

        var dependencies = new List<WorkflowExecutableDependency>();
        foreach (var artifactGroup in snapshot
                     .GroupBy(claim => claim.ArtifactId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var hashes = artifactGroup.Select(claim => claim.ArtifactHash).Distinct(StringComparer.Ordinal).ToArray();
            if (hashes.Length != 1)
                throw new ArgumentException($"Executable dependency artifact '{artifactGroup.Key}' has conflicting hashes.");

            dependencies.Add(new WorkflowExecutableDependency(
                artifactGroup.Key,
                hashes[0],
                artifactGroup.Select(claim => claim.ExecutableNodeId).ToArray()));
        }

        return Array.AsReadOnly(dependencies.ToArray());
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
