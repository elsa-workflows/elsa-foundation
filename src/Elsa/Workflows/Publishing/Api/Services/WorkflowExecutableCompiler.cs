using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
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
    WorkflowExecutableHasher hasher,
    ActivityTreeProjector activityTreeProjector,
    ExecutableNodeCompiler executableNodeCompiler,
    IExecutableNodeMetadataEnricher? metadataEnricher)
    : IWorkflowExecutableCompiler
{
    private readonly IWorkflowExecutableStore? _executableStore;

    /// <summary>
    /// Adds stored dependency-graph validation while preserving both pre-existing constructor shapes.
    /// </summary>
    public WorkflowExecutableCompiler(
        IWorkflowDefinitionVersionStore workflowVersions,
        IActivityDefinitionVersionStore activityVersions,
        WorkflowExecutableHasher hasher,
        ActivityTreeProjector activityTreeProjector,
        ExecutableNodeCompiler executableNodeCompiler,
        IExecutableNodeMetadataEnricher? metadataEnricher,
        IWorkflowExecutableStore executableStore)
        : this(workflowVersions, activityVersions, hasher, activityTreeProjector, executableNodeCompiler, metadataEnricher)
    {
        ArgumentNullException.ThrowIfNull(executableStore);
        _executableStore = executableStore;
    }

    public WorkflowExecutableCompiler(
        IWorkflowDefinitionVersionStore workflowVersions,
        IActivityDefinitionVersionStore activityVersions,
        WorkflowExecutableHasher hasher,
        ActivityTreeProjector activityTreeProjector,
        ExecutableNodeCompiler executableNodeCompiler)
        : this(workflowVersions, activityVersions, hasher, activityTreeProjector, executableNodeCompiler, null)
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

            var activityRows = new Dictionary<string, ActivityDefinitionVersion>(StringComparer.Ordinal);
            foreach (var activityVersionId in projection.Nodes.Select(x => x.ActivityVersionId).Distinct(StringComparer.Ordinal))
                activityRows[activityVersionId] = await activityVersions.GetWithDefinitionAsync(activityVersionId, cancellationToken);

            var compiledRoot = executableNodeCompiler.CompileRoot(rootActivity, projection, activityRows);
            IReadOnlyCollection<ExecutableDependencyClaim> dependencyClaims = [];
            if (metadataEnricher is not null)
            {
                var enrichment = await metadataEnricher.EnrichCompilationAsync(request, source, compiledRoot, cancellationToken);
                compiledRoot = enrichment.RootActivity;
                dependencyClaims = enrichment.Dependencies;
            }

            var inputContract = BuildInputContract(state.Inputs);
            var dependencies = BuildDependencies(dependencyClaims);
            var artifactHash = hasher.ComputeHash(compiledRoot, inputContract, dependencies);
            var artifactId = hasher.CreateArtifactId(request.ArtifactIdPrefix, artifactHash);
            await ValidateDependencyGraphAsync(artifactId, artifactHash, dependencies, cancellationToken);
            var metadata = (request.CompatibilityMetadata ?? new Dictionary<string, string>())
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

            return new WorkflowExecutable(
                identity: new WorkflowExecutableIdentity(
                    ArtifactId: artifactId,
                    DefinitionId: source.DefinitionId,
                    DefinitionVersionId: source.DefinitionVersionId,
                    ArtifactVersion: source.ArtifactVersion,
                    ArtifactHash: artifactHash),
                rootActivity: compiledRoot,
                resumeTargets: executableNodeCompiler.BuildResumeTargets(compiledRoot),
                createdAt: request.CreatedAt,
                compatibilityMetadata: metadata,
                inputContract: inputContract,
                dependencies: dependencies);
        }
        catch (Exception exception) when (
            exception is WorkflowExecutableDependencyGraphException ||
            exception is ArgumentException and not WorkflowExecutableCompilationException)
        {
            throw new WorkflowExecutableCompilationException(source?.DefinitionId, source?.DefinitionVersionId, exception.Message, exception);
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

        var storedExecutables = await _executableStore.ListAsync(cancellationToken);
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
