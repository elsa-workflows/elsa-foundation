using Elsa.Activities.Design.Api.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Requests;

public sealed record ListRecommendedActivityDefinitions(int Offset = 0, int Limit = 25)
    : IRequest<RecommendedActivityDefinitionPageView>;
