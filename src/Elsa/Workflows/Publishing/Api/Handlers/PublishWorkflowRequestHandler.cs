using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Handlers;

/// <summary>
/// Publish = compile → hash → resolve-or-create the content-addressed artifact by ArtifactId (idempotent: an
/// existing artifact is never overwritten) → ALWAYS append a new <see cref="WorkflowExecutableSourceReference"/>
/// carrying the source identity, artifact-version label, publish time, Published scope and the layout sidecar
/// copied verbatim from the definition version's layout store (ADR 0038/0039/0040).
/// </summary>
public sealed class PublishWorkflowRequestHandler(
    IWorkflowExecutableCompiler compiler,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowTriggerIndexer triggerIndexer,
    IWorkflowDefinitionVersionLayoutStore layoutStore,
    WorkflowExecutablePlacementSidecarContext? placementSidecars = null)
    : IRequestHandler<PublishWorkflow, PublishedWorkflowView>
{
    private const string PublishedArtifactPrefix = "artifact-";

    public async Task<PublishedWorkflowView> Handle(PublishWorkflow request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // Compilation failures surface as WorkflowExecutableCompilationException (itself an ArgumentException),
        // carrying the DefinitionId/VersionId context. Let the typed exception propagate rather than rewrapping
        // it into a bare ArgumentException that discards that context (#397).
        var executable = await compiler.CompileAsync(
            new WorkflowExecutableCompileRequest(
                request.VersionId,
                WorkflowExecutableReferenceScope.Published,
                now,
                now,
                ExpiresAt: null,
                PublishedArtifactPrefix,
                new Dictionary<string, string>
                {
                    ["slice"] = "workflow-execution-vertical-slice"
                }),
            cancellationToken);

        // Idempotent by artifact id: a behaviorally identical republish resolves to the existing artifact and
        // leaves it untouched (ADR 0038). The reference below is appended unconditionally.
        await executableStore.SaveAsync(executable, cancellationToken);

        var reference = await BuildSourceReferenceAsync(executable, now, cancellationToken);
        await sourceReferenceStore.SaveAsync(reference, cancellationToken);

        // Index this artifact's start-triggers within the publish flow (W7, E3-1). A failure here propagates and
        // fails the publish by design: a silently unindexed published trigger — one that can never start a
        // workflow — is a worse outcome than a failed publish the caller can retry (indexing is idempotent).
        await triggerIndexer.IndexAsync(executable, cancellationToken);

        return PublishedWorkflowView.From(executable, reference);
    }

    private async Task<WorkflowExecutableSourceReference> BuildSourceReferenceAsync(
        WorkflowExecutable executable,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var identity = executable.Identity;
        var layout = await layoutStore.FindByVersionIdAsync(identity.DefinitionVersionId, cancellationToken);

        return new WorkflowExecutableSourceReference(
            SourceReferenceId: ShortIdentityGenerator.Generate(now),
            ArtifactId: identity.ArtifactId,
            SourceKind: WorkflowExecutableSourceKinds.WorkflowDefinitionVersion,
            SourceId: identity.DefinitionVersionId,
            SourceVersion: identity.ArtifactVersion,
            DefinitionId: identity.DefinitionId,
            DefinitionVersionId: identity.DefinitionVersionId,
            ArtifactVersion: identity.ArtifactVersion,
            CreatedAt: now,
            PublishedAt: now,
            Scope: WorkflowExecutableReferenceScope.Published,
            Layout: WorkflowExecutableLayoutSidecar.CopyFrom(layout),
            LayoutSidecar: placementSidecars?.Get(identity.DefinitionVersionId));
    }
}
