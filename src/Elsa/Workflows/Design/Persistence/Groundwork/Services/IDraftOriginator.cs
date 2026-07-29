using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Provider-feature contract for the draft-origination lifecycle shared by fresh creation and
/// clone-from-version.
/// </summary>
public interface IDraftOriginator
{
    Task<string> ExecuteAsync<TRequest>(
        DesignOperationKey operationKey,
        string operationKind,
        TRequest requestMaterial,
        Func<CancellationToken, Task<DraftOriginationInput>> resolveInput,
        CancellationToken cancellationToken)
        where TRequest : notnull;
}

public sealed record DraftOriginationInput(
    string WorkflowDefinitionId,
    WorkflowDefinitionState State,
    IReadOnlyCollection<DesignMetadataRecord> Layout,
    string? SourceVersionId,
    string? TenantId = null,
    IReadOnlyCollection<ActivityPresentationRecord>? ActivityPresentation = null);
