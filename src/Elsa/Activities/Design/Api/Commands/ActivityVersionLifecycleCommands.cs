using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record RetireReusableActivityVersion(
    [property: JsonIgnore] string VersionId,
    ActivityDefinitionVersionLifecycle ExpectedLifecycle,
    string Reason,
    ActivityRecommendationDecision? RecommendationDecision = null) : ICommand<ReusableActivityVersionLifecycleView>;

public sealed record RestoreReusableActivityVersion(
    [property: JsonIgnore] string VersionId,
    ActivityDefinitionVersionLifecycle ExpectedLifecycle,
    string Reason) : ICommand<ReusableActivityVersionLifecycleView>;

public sealed record RevokeReusableActivityVersion(
    [property: JsonIgnore] string VersionId,
    ActivityDefinitionVersionLifecycle ExpectedLifecycle,
    string Reason,
    ActivityRecommendationDecision? RecommendationDecision = null) : ICommand<ReusableActivityVersionLifecycleView>;

public interface IActivityVersionSelectionPolicy
{
    bool CanSelectDirectly(ActivityDefinitionVersionLifecycle lifecycle);

    bool CanUseClosedDependency(ActivityDefinitionVersionLifecycle lifecycle);
}

/// <summary>
/// Retirement is a catalog-selection fact; it does not invalidate an exact dependency already closed into
/// an immutable parent template. Revocation is intentionally stronger and is rejected at both selection seams.
/// </summary>
public sealed class DefaultActivityVersionSelectionPolicy : IActivityVersionSelectionPolicy
{
    public bool CanSelectDirectly(ActivityDefinitionVersionLifecycle lifecycle) =>
        lifecycle == ActivityDefinitionVersionLifecycle.Active;

    public bool CanUseClosedDependency(ActivityDefinitionVersionLifecycle lifecycle) =>
        lifecycle is ActivityDefinitionVersionLifecycle.Active or ActivityDefinitionVersionLifecycle.Retired;
}
