using System.Text.Json.Serialization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record SetRecommendedReusableActivityVersion(
    [property: JsonIgnore] string DefinitionId,
    string? ExpectedDefinitionHeadVersionId,
    string? ExpectedRecommendedVersionId,
    string? RecommendedVersionId,
    ActivityDefinitionVersionLifecycle? ExpectedRecommendedVersionLifecycle,
    string Reason) : ICommand<ActivityDefinitionRecommendationView>;
