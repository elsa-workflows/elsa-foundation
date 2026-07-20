using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using System.Text.Json.Serialization;
using RouteParam = FastEndpoints.RouteParamAttribute;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record CreateActivityUpgradePlan(
    IReadOnlyList<ActivityVersionReplacement> Replacements,
    IReadOnlyList<ActivityUpgradeRoot> Roots,
    bool IncludeTransitiveDependents = true,
    bool CreateDraftsForPublishedDependents = false) : ICommand<ActivityUpgradePlanView>;

public sealed record ApplyActivityUpgradePlan(
    [property: RouteParam, JsonIgnore] string PlanId,
    string StageId,
    string IdempotencyKey) : ICommand<ActivityUpgradeApplyResultView>;

public sealed record RefreshActivityUpgradePlan(
    [property: RouteParam, JsonIgnore] string PlanId,
    IReadOnlyList<ActivityUpgradePublicationReceipt> Publications) : ICommand<ActivityUpgradePlanView>;
