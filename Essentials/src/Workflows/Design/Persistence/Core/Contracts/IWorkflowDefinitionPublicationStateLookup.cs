namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// The publication-lifecycle facts Design cannot see: whether a definition currently serves, was
/// unpublished, or only has retired publication records. Publishing implements this; a design-only
/// host omits it and definition views leave <c>publicationState</c> unset.
/// </summary>
public interface IWorkflowDefinitionPublicationStateLookup
{
    /// <summary>
    /// Resolves one state per supplied definition id. Every id is present in the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, WorkflowDefinitionPublicationState>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Design-facing publication lifecycle for one workflow definition.</summary>
public enum WorkflowDefinitionPublicationState
{
    /// <summary>Never published, or no publication journal / slot evidence remains.</summary>
    Draft = 0,

    /// <summary>At least one activation slot currently serves.</summary>
    Published = 1,

    /// <summary>A slot exists but none currently serve — unpublished and restorable.</summary>
    Retracted = 2,

    /// <summary>Publication records were retired and no slot remains.</summary>
    Retired = 3
}
