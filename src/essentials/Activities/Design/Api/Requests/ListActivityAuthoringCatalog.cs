using Elsa.Activities.Design.Api.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Requests;

public enum ActivityCatalogAvailability
{
    Addable,
    All
}

public sealed record ListActivityAuthoringCatalog(
    ActivityCatalogAvailability Availability = ActivityCatalogAvailability.Addable)
    : IRequest<ActivityAuthoringCatalogView>;
