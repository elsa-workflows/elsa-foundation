using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record PublishedWorkflowView(
    string ArtifactId,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    string ArtifactHash,
    string RootActivityId,
    int NodeCount,
    string? SourceReferenceId = null)
{
    public static PublishedWorkflowView From(WorkflowExecutable executable) =>
        From(executable, reference: null);

    public static PublishedWorkflowView From(WorkflowExecutable executable, WorkflowExecutableSourceReference? reference) =>
        new(
            executable.Identity.ArtifactId,
            executable.Identity.DefinitionId,
            executable.Identity.DefinitionVersionId,
            executable.Identity.ArtifactVersion,
            executable.Identity.ArtifactHash,
            executable.RootActivity.ExecutableNodeId,
            executable.Nodes.Count,
            reference?.SourceReferenceId);
}
