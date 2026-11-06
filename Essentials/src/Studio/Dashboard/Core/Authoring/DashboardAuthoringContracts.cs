using Elsa.Studio.Dashboard.Core.Models;

namespace Elsa.Studio.Dashboard.Core.Authoring;

/// <summary>Single scoped authoring implementation. Conversation and inference remain with the configured Weaver runtime.</summary>
/// <remarks>Implementations resolve the current actor themselves; inputs never grant tenant, resource or data authority.</remarks>
public interface IDashboardAuthoringService
{
    ValueTask<DashboardAuthoringContext> PrepareAsync(DashboardAuthoringDraft draft, CancellationToken cancellationToken = default);
    IAsyncEnumerable<DashboardAuthoringEvent> AuthorAsync(DashboardAuthoringRequest request, CancellationToken cancellationToken = default);
}

public sealed record DashboardAuthoringTarget(string ModuleId, string ResourceType, string ResourceId, string Revision,
    string? DisplayName = null);

public sealed record DashboardAuthoringClient(string ClientId, string GenerationId);

/// <summary>Declarative working state, separate from a saved definition and from protected result evidence.</summary>
public sealed record DashboardAuthoringDraft(DashboardAuthoringTarget Target, DashboardAuthoringClient Client,
    string WorkingRevision, DashboardQueryRequest? Definition, string? Name = null, bool Dirty = true,
    string? ConfirmedInterpretation = null);

public sealed record DashboardAuthoringRequest(string Message, DashboardAuthoringDraft Draft, string? ConversationId = null);

public sealed record DashboardAuthoringContext(DashboardAuthoringDraft Draft,
    IReadOnlyList<DashboardDatasetDeclaration> Datasets, bool CanEdit, bool CanSave,
    IReadOnlyList<DashboardQueryProblem> Problems);

/// <summary>A proposal binds an exact editor generation/revision and includes native validated results; it is never a save receipt.</summary>
public sealed record DashboardAuthoringProposal(DashboardAuthoringTarget Target, DashboardAuthoringClient Client,
    string WorkingRevision, DashboardQueryRequest Definition, string DefinitionFingerprint,
    DashboardQueryResult? Preview, string Interpretation);

public sealed record DashboardAuthoringChoice(string Id, string Label, string Description);
public sealed record DashboardAuthoringClarification(string Question, IReadOnlyList<DashboardAuthoringChoice> Choices);

/// <summary>Provider-neutral authoring progress: started, message, clarification, proposal, completed, cancelled or failed.</summary>
public sealed record DashboardAuthoringEvent(string Kind, string ConversationId, string? Message = null,
    DashboardAuthoringProposal? Proposal = null, DashboardAuthoringClarification? Clarification = null, string? Code = null);
