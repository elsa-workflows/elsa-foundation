using Elsa.Activities.Design.Api.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Requests;

public sealed record GetDefinition(string Id) : IRequest<ActivityDefinitionDetailsView>;
