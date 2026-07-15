using Elsa.Activities.Design.Api.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Requests;

public sealed record CompareActivityVersions(
    string FromVersionId,
    string ToVersionId) : IRequest<ActivityVersionDiffView>;

public sealed record PreviewActivityDraftDiff(
    string DraftId,
    long ExpectedRevision,
    string? BaseVersionId = null) : IRequest<ActivityVersionDiffView>;
