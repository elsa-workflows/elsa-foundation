namespace Elsa.Workflows.Design.Persistence.Core.Models;

public sealed record SubmittedWorkflowDefinition(
    string DefinitionId,
    string DraftId,
    string VersionId);
