namespace Elsa.Workflows.Runtime.Core.Models;

public static class WorkflowExecutableIdentityComparer
{
    public static bool MatchesPinnedSnapshot(WorkflowExecutableIdentity executable, WorkflowExecutableIdentity pinned)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(pinned);

        return executable.ArtifactId == pinned.ArtifactId &&
               executable.DefinitionId == pinned.DefinitionId &&
               executable.DefinitionVersionId == pinned.DefinitionVersionId &&
               executable.ArtifactVersion == pinned.ArtifactVersion &&
               executable.ArtifactHash == pinned.ArtifactHash;
    }

    public static string Format(WorkflowExecutableIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return $"{identity.ArtifactId}@{identity.ArtifactVersion}/{identity.ArtifactHash} ({identity.DefinitionId}/{identity.DefinitionVersionId})";
    }
}