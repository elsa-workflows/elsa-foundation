namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// Evaluates design-time activity addability for catalog-backed picker surfaces.
/// </summary>
public interface IActivityAvailabilityEvaluator
{
    IReadOnlyCollection<IActivityDefinition> FilterAddable(IEnumerable<IActivityDefinition> activities);
}
