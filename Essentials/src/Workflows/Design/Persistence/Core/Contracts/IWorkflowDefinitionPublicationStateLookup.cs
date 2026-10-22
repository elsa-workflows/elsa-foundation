namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// The publication-lifecycle facts Design cannot see: whether a definition currently serves, was
/// unpublished, or only has retired publication records. Design registers a null-object default so
/// NativeEndpoints can resolve it; Publishing replaces that with the slot/journal lookup.
/// </summary>
public interface IWorkflowDefinitionPublicationStateLookup
{
    /// <summary>
    /// Resolves publication state and the active publication's immutable source version id for
    /// supplied definition ids when the available stores provide enough evidence. An id omitted
    /// from the result has an unknown publication state. A source version is absent when no
    /// publication is active or the active publication record is unavailable.
    /// </summary>
    Task<IReadOnlyDictionary<string, WorkflowDefinitionPublicationSummary>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Publication lifecycle facts used to enrich a workflow-definition summary.</summary>
public sealed record WorkflowDefinitionPublicationSummary(
    WorkflowDefinitionPublicationState State,
    string? PublishedVersionId = null);

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
