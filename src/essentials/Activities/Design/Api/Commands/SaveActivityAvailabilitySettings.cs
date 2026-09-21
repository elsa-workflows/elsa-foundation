using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record SaveActivityAvailabilitySettings(
    string? Scope,
    ActivityAvailabilityManagementMode Mode,
    ActivityAvailabilityRuleSet? Rules
) : ICommand<ActivityAvailabilitySettings>;
