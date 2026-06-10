namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record PublishedWorkflowView(
    string ArtifactId,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    string ArtifactHash,
    int NodeCount,
    int EdgeCount,
    IReadOnlyList<string> StartNodeIds);
