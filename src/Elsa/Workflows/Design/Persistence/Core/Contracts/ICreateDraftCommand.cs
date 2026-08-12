using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Lifecycle-origination command that creates a <c>WorkflowDefinitionDraft</c> with the supplied
/// (or empty) State and a corresponding <c>WorkflowDefinitionDraftLayout</c> (empty unless
/// <paramref name="initialLayout"/> is supplied). It owns the full origination flow — per-Draft
/// lock, in-lock validation gate, atomic flush — and publishes <c>DraftCreated</c> followed by
/// <c>DraftValidated</c>.
/// </summary>
/// <remarks>
/// Fresh creation and <c>ICloneDraftFromVersionCommand</c> must use the same provider-owned
/// origination lifecycle path: generate the Draft id, acquire its lock, validate, persist
/// atomically, then publish the lifecycle events. Providers need not implement that invariant by
/// making one public command call the other. The origin is carried by
/// <c>DraftCreated.SourceVersionId</c> (<c>null</c> for fresh, set for a clone).
/// </remarks>
public interface ICreateDraftCommand
{
    /// <param name="workflowDefinitionId">The owning definition's id.</param>
    /// <param name="initialState">The Draft's initial State; an empty State when omitted.</param>
    /// <param name="initialLayout">The Draft's initial layout records; empty when omitted.</param>
    /// <param name="sourceVersionId">The version this Draft was cloned from, or <c>null</c> for a fresh Draft. Surfaced on <c>DraftCreated.SourceVersionId</c>.</param>
    Task<string> Execute(
        DesignOperationKey operationKey,
        string workflowDefinitionId,
        WorkflowDefinitionState? initialState = null,
        IReadOnlyCollection<DesignMetadataRecord>? initialLayout = null,
        string? sourceVersionId = null,
        CancellationToken cancellationToken = default);
}
