namespace Elsa.Activities.Design.Persistence.Core.Contracts;

/// <summary>
/// Marks an Activities Design persistence seam for which exactly one implementation may be
/// selected in an application. Persistence backends use this provider-neutral marker to reject
/// mixed-family composition without relying on type-name heuristics.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class ActivityDesignPersistenceReplacementContractAttribute : Attribute
{
}
