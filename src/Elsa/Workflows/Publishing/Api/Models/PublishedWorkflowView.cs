using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record PublishedWorkflowView(
    string ArtifactId,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    string ArtifactHash,
    string RootActivityId,
    int NodeCount)
{
    public static PublishedWorkflowView From(WorkflowExecutable executable) =>
        new(
            executable.Identity.ArtifactId,
            executable.Identity.DefinitionId,
            executable.Identity.DefinitionVersionId,
            executable.Identity.ArtifactVersion,
            executable.Identity.ArtifactHash,
            executable.RootActivity.ExecutableNodeId,
            executable.Nodes.Count);
}
