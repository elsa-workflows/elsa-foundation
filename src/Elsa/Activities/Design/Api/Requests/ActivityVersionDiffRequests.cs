using Elsa.Activities.Design.Api.Models;
using Elsa.Mediator.Core.Contracts;
using System.Text.Json.Serialization;
using RouteParam = FastEndpoints.RouteParamAttribute;

namespace Elsa.Activities.Design.Api.Requests;

public sealed record CompareActivityVersions(
    string FromVersionId,
    string ToVersionId) : IRequest<ActivityVersionDiffView>;

public sealed record PreviewActivityDraftDiff(
    [property: RouteParam, JsonIgnore] string DraftId,
    long ExpectedRevision,
    string? BaseVersionId = null) : IRequest<ActivityVersionDiffView>;
