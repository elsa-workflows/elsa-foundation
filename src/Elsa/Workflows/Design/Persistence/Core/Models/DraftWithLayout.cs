using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Models;

/// <summary>
/// Combined read result: a <see cref="WorkflowDefinitionDraft"/> together with its designer-layout
/// records. Returned by <c>IWorkflowDefinitionDraftStore.FindWithLayoutByIdAsync</c> so a caller that
/// needs both (e.g. the GET-draft API path) loads the draft document once instead of round-tripping
/// for the draft and then again for its layout sibling.
/// </summary>
public sealed record DraftWithLayout(
    WorkflowDefinitionDraft Draft,
    IReadOnlyCollection<DesignMetadataRecord> Layout);
