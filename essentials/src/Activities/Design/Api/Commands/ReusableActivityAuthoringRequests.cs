using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Design.Api.Commands;

// The endpoint binder reconstructs positional contracts. Separate wire envelopes carry the
// additive field without changing existing commands' constructor/deconstructor signatures.
public sealed record CreateReusableActivityDefinitionRequest(
    string Category,
    string DisplayName,
    string? Description,
    ActivityProviderManifest Provider,
    ActivityContractView Contract,
    IReadOnlyList<ActivityLayoutRecord> Layout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActivityTypeKey = null,
    ActivityNodePresentationPayload? ActivityPresentation = null)
{
    internal CreateReusableActivityDefinition ToCommand() =>
        new(Category, DisplayName, Description, Provider, Contract, Layout, ActivityTypeKey) { ActivityPresentation = ActivityPresentation };
}

public sealed record CreateReusableActivityDraftBody(
    [property: JsonIgnore] string DefinitionId,
    string? SourceVersionId,
    ActivityProviderManifest? Provider = null,
    ActivityContractView? Contract = null,
    ActivityLayoutRecordsPayload? Layout = null,
    string? PresentationLabel = null,
    ActivityNodePresentationPayload? ActivityPresentation = null)
{
    internal CreateReusableActivityDraft ToCommand() =>
        new(DefinitionId, SourceVersionId, Provider, Contract, Layout?.Records, PresentationLabel) { ActivityPresentation = ActivityPresentation };
}

public sealed record ReplaceReusableActivityDraftRequest(
    [property: JsonIgnore] string DraftId,
    long ExpectedRevision,
    ActivityContractView Contract,
    ActivityProviderManifest Provider,
    IReadOnlyList<ActivityLayoutRecord> Layout,
    string? PresentationLabel = null,
    ActivityNodePresentationPayload? ActivityPresentation = null)
{
    internal ReplaceReusableActivityDraft ToCommand() =>
        new(DraftId, ExpectedRevision, Contract, Provider, Layout, PresentationLabel) { ActivityPresentation = ActivityPresentation };
}

public sealed record DraftConflictCopyRequest(
    [property: JsonIgnore] string DraftId,
    long ExpectedSourceRevision,
    ActivityContractView Contract,
    ActivityProviderManifest Provider,
    IReadOnlyList<ActivityLayoutRecord> Layout,
    string? PresentationLabel = null,
    ActivityNodePresentationPayload? ActivityPresentation = null)
{
    internal CreateReusableActivityDraftConflictCopy ToCommand() =>
        new(DraftId, ExpectedSourceRevision, Contract, Provider, Layout, PresentationLabel) { ActivityPresentation = ActivityPresentation };
}
