using System.Text.Json.Serialization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using RouteParam = FastEndpoints.RouteParamAttribute;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record SetRecommendedReusableActivityVersion(
    [property: RouteParam, JsonIgnore] string DefinitionId,
    string? ExpectedDefinitionHeadVersionId,
    string? ExpectedRecommendedVersionId,
    string? RecommendedVersionId,
    ActivityDefinitionVersionLifecycle? ExpectedRecommendedVersionLifecycle,
    string Reason) : ICommand<ActivityDefinitionRecommendationView>;
