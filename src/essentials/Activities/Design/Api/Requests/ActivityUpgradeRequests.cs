using Elsa.Activities.Design.Api.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Requests;

public sealed record GetActivityUpgradePlan(string PlanId) : IRequest<ActivityUpgradePlanView>;

public sealed record GetActivityUpgradeApplyReceipt(
    string PlanId,
    string ReceiptId) : IRequest<ActivityUpgradeApplyReceiptView>;
