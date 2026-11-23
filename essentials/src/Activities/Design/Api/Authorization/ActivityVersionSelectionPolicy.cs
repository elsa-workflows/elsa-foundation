using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Commands;

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
