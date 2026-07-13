namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Immutable attribution for the authoritative source reference selected when an execution starts.
/// Kept separate from <see cref="WorkflowExecutableIdentity"/>, which identifies executable content.
/// </summary>
public sealed record WorkflowExecutableSourceProvenance(
    string SourceReferenceId,
    string SourceKind,
    string SourceId,
    string? SourceVersion,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    string? PublicationId,
    string? SlotId)
{
    public static WorkflowExecutableSourceProvenance From(WorkflowExecutableSourceReference reference) =>
        new(
            reference.SourceReferenceId,
            reference.SourceKind,
            reference.SourceId,
            reference.SourceVersion,
            reference.DefinitionId,
            reference.DefinitionVersionId,
            reference.ArtifactVersion,
            reference.PublicationId,
            reference.SlotId);
}
