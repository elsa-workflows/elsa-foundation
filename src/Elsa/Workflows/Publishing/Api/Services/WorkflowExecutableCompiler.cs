using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
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
    WorkflowExecutableHasher hasher,
    ActivityTreeProjector activityTreeProjector,
    ExecutableNodeCompiler executableNodeCompiler)
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
            foreach (var activityVersionId in projection.Nodes.Select(x => x.ActivityVersionId).Distinct(StringComparer.Ordinal))
                activityRows[activityVersionId] = await activityVersions.GetWithDefinitionAsync(activityVersionId, cancellationToken);

            var compiledRoot = executableNodeCompiler.CompileRoot(rootActivity, projection, activityRows);
            var artifactHash = hasher.ComputeHash(compiledRoot);
            var artifactId = hasher.CreateArtifactId(request.ArtifactIdPrefix, artifactHash);
            var metadata = (request.CompatibilityMetadata ?? new Dictionary<string, string>())
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

            return new WorkflowExecutable(
                identity: new WorkflowExecutableIdentity(
                    ArtifactId: artifactId,
                    DefinitionId: source.DefinitionId,
                    DefinitionVersionId: source.DefinitionVersionId,
                    ArtifactVersion: source.ArtifactVersion,
                    ArtifactHash: artifactHash,
                    Source: source.SourceReference),
                rootActivity: compiledRoot,
                resumeTargets: executableNodeCompiler.BuildResumeTargets(compiledRoot),
                createdAt: request.CreatedAt,
                publishedAt: request.PublishedAt,
                compatibilityMetadata: metadata,
                scope: request.Scope,
                expiresAt: request.ExpiresAt);
        }
        catch (ArgumentException exception) when (exception is not WorkflowExecutableCompilationException)
        {
            throw new WorkflowExecutableCompilationException(source?.DefinitionId, source?.DefinitionVersionId, exception.Message, exception);
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
            SourceReference: new WorkflowExecutableSourceReference("WorkflowDefinitionVersion", version.Id, version.Version));
    }
}
