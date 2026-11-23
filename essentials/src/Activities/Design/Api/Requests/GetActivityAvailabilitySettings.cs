using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Requests;

public sealed record GetActivityAvailabilitySettings(string? Scope = null) : IRequest<ActivityAvailabilitySettings>;
