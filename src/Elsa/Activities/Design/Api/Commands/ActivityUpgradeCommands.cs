using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record CreateActivityUpgradePlan(
    IReadOnlyList<ActivityVersionReplacement> Replacements,
    IReadOnlyList<ActivityUpgradeRoot> Roots,
    bool IncludeTransitiveDependents = true,
    bool CreateDraftsForPublishedDependents = false) : ICommand<ActivityUpgradePlanView>;

public sealed record ApplyActivityUpgradePlan(
    string PlanId,
    IReadOnlyList<string>? SelectedStepIds = null) : ICommand<ActivityUpgradeApplyResultView>;
